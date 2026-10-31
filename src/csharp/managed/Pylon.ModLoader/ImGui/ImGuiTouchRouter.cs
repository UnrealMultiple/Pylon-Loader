using System.Collections.Generic;
using UnityEngine;

namespace Pylon.ModLoader.UI
{
    /// <summary>触摸穿透拦截：注册本帧菜单窗口矩形，游戏侧 CursorManager 命中即忽略世界/触摸输入。</summary>
    internal static class ImGuiTouchRouter
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

        internal static void RequestInstall()
        {
            if (_installed) return;
            _installed = true;
            On.CursorManager.ShouldIgnoreForWorld += ShouldIgnoreForWorld;
            On.CursorManager.ShouldIgnoreTouch += ShouldIgnoreTouch;
        }

        private static bool ShouldIgnoreForWorld(On.CursorManager.orig_ShouldIgnoreForWorld orig,
                                                 CursorManager self, Vector2 position)
        {
            var result = orig(self, position);
            if (!result && HitWindowAt(position.x / Kx, (ScrH - position.y) / Ky))
                return true;
            return result;
        }

        private static bool ShouldIgnoreTouch(On.CursorManager.orig_ShouldIgnoreTouch orig,
                                              CursorManager self, int touchIndex)
        {
            var result = orig(self, touchIndex);
            if (result) return true;
            if (self == null) return false;
            Vector2 pos;
            try { pos = self.GetTouchPosition(touchIndex); }
            catch { return false; }
            return HitWindowAt(pos.x / Kx, (ScrH - pos.y) / Ky);
        }
    }
}
