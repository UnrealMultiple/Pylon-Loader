using System;
using System.IO;
using Pylon.ModLoader.ImGuiHost;
using Terraria;

namespace CheatMenu.Features;

/// <summary>
/// 重生设置。1:1 对应原版 <c>RespawnTool</c>：
/// 启用后重生等待时间固定为「重生时间」秒；「拦截重生包」直接吞掉 msgType 12。
/// </summary>
public sealed class RespawnFeature : CheatFeature
{
    public override string Id => "player.respawn";
    public override string Title => "重生";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "主要";

    public override void Install()
    {
        On.Terraria.Player.GetRespawnTime += GetRespawnTime;
    }

    private static int GetRespawnTime(On.Terraria.Player.orig_GetRespawnTime orig, Player self, bool pvp)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { RespawnEnabled: true })
            return Math.Clamp(cfg.RespawnSeconds, 1, 30) * 60;
        return orig(self, pvp);
    }

    public override bool OnGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
    {
        // 原版只检查 InterceptRespawnPack，不看 Enabled。
        if (messageType == 12 && CheatFeatureHost.Config is { RespawnIntercept: true }) return true;
        return false;
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("重生", "RESPAWN");
        ModernUI.BeginRow();
        ModernUI.RowLabel("启用重生");
        if (ModernUI.Toggle("##respawn", cfg.RespawnEnabled))
        {
            cfg.RespawnEnabled = !cfg.RespawnEnabled;
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();

        if (cfg.RespawnEnabled)
        {
            ModernUI.BeginRow();
            ModernUI.RowLabel("拦截重生包");
            if (ModernUI.Toggle("##respawnpack", cfg.RespawnIntercept))
            {
                cfg.RespawnIntercept = !cfg.RespawnIntercept;
                CheatFeatureHost.MarkDirty();
            }
            ModernUI.EndRow();

            var seconds = (float)cfg.RespawnSeconds;
            if (ModernUI.RowSlider("##respawntime", "重生时间(秒)", "1-30", ref seconds, 1f, 30f, "0"))
            {
                cfg.RespawnSeconds = Math.Clamp((int)seconds, 1, 30);
                CheatFeatureHost.MarkDirty();
            }
        }

        ModernUI.EndCard();
    }
}