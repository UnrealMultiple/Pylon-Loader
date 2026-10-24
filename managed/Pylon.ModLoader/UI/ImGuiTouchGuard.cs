using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Pylon.ModLoader.Patch;

namespace Pylon.ModLoader.UI
{
    /// <summary>触摸穿透拦截（游戏侧 CursorManager 的 ShouldIgnore* 过滤）。</summary>
    internal static class ImGuiTouchGuard
    {
        private readonly struct Rect(float x, float y, float w, float h)
        {
            public readonly float X = x, Y = y, W = w, H = h;
        }

        private static readonly List<Rect> Rects = [];
        internal static float Kx = 1f, Ky = 1f, ScrH = 1f;

        private static bool _installed;

        internal static void BeginFrame() => Rects.Clear();

        internal static void AddWindowRect(float x, float y, float w, float h,
                                           float kx, float ky, float scrH)
        {
            if (w <= 0 || h <= 0) return;
            Kx = kx; Ky = ky; ScrH = scrH;
            Rects.Add(new Rect(x, y, w, h));
        }

        /// <summary>覆盖层坐标命中测试</summary>
        internal static bool HitWindowAt(float overlayX, float overlayY)
        {
            foreach (var r in Rects)
            {
                if (overlayX >= r.X && overlayX <= r.X + r.W &&
                    overlayY >= r.Y && overlayY <= r.Y + r.H)
                    return true;
            }
            return false;
        }

        internal static void RequestInstall(Harmony harmony, ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            GamePatcher.Install(harmony, m => logger?.Info(m), typeof(ImGuiTouchGuard).Assembly);
        }
    }

    [GamePatch(typeof(CursorManager), nameof(CursorManager.ShouldIgnoreForWorld), ParamCount = 1)]
    internal static class CursorIgnoreWorldPatch
    {
        [Post]
        private static void After(Vector2 __0, ref bool __result)
        {
            if (ImGuiTouchGuard.HitWindowAt(__0.x / ImGuiTouchGuard.Kx, (ImGuiTouchGuard.ScrH - __0.y) / ImGuiTouchGuard.Ky))
                __result = true;
        }
    }

    [GamePatch(typeof(CursorManager), nameof(CursorManager.ShouldIgnoreTouch), ParamCount = 1)]
    internal static class CursorIgnoreTouchPatch
    {
        [Post]
        private static void After(CursorManager __instance, int __0, ref bool __result)
        {
            if (__instance == null) return;
            Vector2 pos;
            try { pos = __instance.GetTouchPosition(__0); }
            catch { return; }
            if (ImGuiTouchGuard.HitWindowAt(pos.x / ImGuiTouchGuard.Kx, (ImGuiTouchGuard.ScrH - pos.y) / ImGuiTouchGuard.Ky))
                __result = true;
        }
    }
}