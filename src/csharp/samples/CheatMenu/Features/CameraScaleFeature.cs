using System;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 视野缩放。1:1 对应原版 <c>CameraScaleTool</c>（杂项 · 解锁）：
/// hook <c>XNAWorldCameraSettings.get_MaxPixelScale</c> 替代上限、
/// <c>ApplyCamera</c> 后按「同步视野大小」把 ZoomScale 写回。
/// </summary>
public sealed class CameraScaleFeature : CheatFeature
{
    private static bool _hookFailedLogged;

    public override string Id => "misc.camerazoom";
    public override string Title => "相机缩放";
    public override CheatCategory Category => CheatCategory.Misc;
    public override string SubPage => "解锁";

    public override void Install()
    {
        // get_MaxPixelScale 是 private 方法；On.* 代理同样可用。
        On.XNAWorldCameraSettings.get_MaxPixelScale += HookMaxPixelScale;
        On.XNAWorldCameraSettings.ApplyCamera += ApplyCameraHook;
    }

    // ---------------- hooks ----------------

    private static float HookMaxPixelScale(On.XNAWorldCameraSettings.orig_get_MaxPixelScale orig,
        XNAWorldCameraSettings self)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { CameraScaleEnabled: true })
            return cfg.MaxPixelScale;
        return orig(self);
    }

    private static void ApplyCameraHook(On.XNAWorldCameraSettings.orig_ApplyCamera orig,
        XNAWorldCameraSettings self)
    {
        orig(self);
        var cfg = CheatFeatureHost.Config;
        if (cfg is { CameraScaleEnabled: true, SyncViewSize: true } && self is not null)
        {
            try { self.ZoomScale = cfg.ZoomScale; }
            catch
            {
                if (!_hookFailedLogged)
                {
                    _hookFailedLogged = true;
                    CheatMenuMod.Log?.Warn("[camerascale] ZoomScale 写入失败：" +
                        "基类 DeviceVPPICalculator.ZoomScale 可能未生成 setter 代理");
                }
            }
        }
    }

    // ---------------- UI ----------------

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("视野缩放", "CAMERA ZOOM");

        RowToggle("启用视野调整", "##camenabled", cfg.CameraScaleEnabled, v => cfg.CameraScaleEnabled = v);
        if (cfg.CameraScaleEnabled)
        {
            RowToggle("同步视野大小", "##camsync", cfg.SyncViewSize, v => cfg.SyncViewSize = v);

            var zoom = cfg.ZoomScale;
            if (ModernUI.RowSlider("##zoom", "缩放倍率", "0.1-20", ref zoom, 0.1f, 20f, "0.00"))
            {
                cfg.ZoomScale = Math.Clamp(zoom, 0.1f, 20f);
                CheatFeatureHost.MarkDirty();
            }

            var maxPx = cfg.MaxPixelScale;
            if (ModernUI.RowSlider("##maxpx", "视野上限", "1-20", ref maxPx, 1f, 20f, "0.00"))
            {
                cfg.MaxPixelScale = Math.Clamp(maxPx, 1f, 20f);
                CheatFeatureHost.MarkDirty();
            }
        }

        ModernUI.EndCard();
    }

    private static void RowToggle(string label, string id, bool value, Action<bool> set)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        if (ModernUI.Toggle(id, value))
        {
            set(!value);
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();
    }
}
