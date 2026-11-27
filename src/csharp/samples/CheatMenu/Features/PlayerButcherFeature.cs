using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Terraria.DataStructures;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 屠杀玩家。1:1 对应原版 <c>PlayerButcherTool</c>（实体 · 屠杀玩家）：
/// 发玩家控制包（PlayerControls）→ 按 PlayerDeathReason.ByPlayer 逐击 SendPlayerHurt →
/// 复位控制包。伤害可调；自动模式挂在 PlayerUpdate。
/// </summary>
public sealed class PlayerButcherFeature : CheatFeature
{
    public override string Id => "entity.butcherplayer";
    public override string Title => "屠杀玩家";
    public override CheatCategory Category => CheatCategory.Physical;
    public override string SubPage => "屠杀玩家";

    public override void Tick()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { AutoButcherPlayer: true }) ButcherAll();
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("屠杀玩家 (PVP)", "PVP BUTCHER");

        if (ModernUI.GhostButton("屠杀全部玩家(PVP)", default, ModernUI.Icon.None, rightAlign: true))
        {
            ButcherAll();
        }

        var damage = (float)cfg.ButcherDamage;
        if (ModernUI.RowSlider("##butchdmg", "屠杀伤害", "1-32767", ref damage, 1f, 32767f, "0"))
        {
            cfg.ButcherDamage = (int)Math.Clamp(damage, 1f, 32767f);
            CheatFeatureHost.MarkDirty();
        }

        RowToggle("自动屠杀玩家(PVP)", "##autobutcherpvp", cfg.AutoButcherPlayer, v => cfg.AutoButcherPlayer = v);

        ModernUI.EndCard();
    }

    /// <summary>原版 ButcherAllPlayers：非本机且 hostile 的玩家。</summary>
    private static void ButcherAll()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        var players = Main.player;
        if (players is null) return;
        foreach (var player in players)
        {
            if (player is null || player.whoAmI == Main.myPlayer || !player.hostile) continue;
            ButcherPlayer(player, cfg.ButcherDamage, -1);
        }
    }

    /// <summary>原版 ButcherPlayer：控制包 → N 次 hurt → 复位控制包。</summary>
    private static void ButcherPlayer(Player player, int damage, int hitCount)
    {
        if (player is null) return;

        var trueHitCount = hitCount;
        if (hitCount == -1)
        {
            trueHitCount = (int)MathF.Ceiling(
                (player.statLife + player.statDefense / 2f) / (float)damage);
        }

        WritePlayerControlsPacket(player.position);
        var self = Game.LocalPlayer;
        if (self is null) return;

        var reason = PlayerDeathReason.ByPlayer(Main.myPlayer);
        for (var j = 0; j < trueHitCount; j++)
        {
            NetMessage.SendPlayerHurt(player.whoAmI, reason, damage, 0, true, true, 0, -1, -1);
        }

        NetMessage.SendData(13, -1, -1, null, Main.myPlayer, 0f, 0f, 0f, 0, 0, 0);   // PlayerControls
    }

    /// <summary>
    /// 原版 PacketBuilderExtensions::WritePlayerControlsPacket(pb, position)：
    /// 本地玩家当前控制状态 + 传入位置。PlayerControls = 13 号包。
    /// </summary>
    private static void WritePlayerControlsPacket(Vector2 position)
    {
        var self = Game.LocalPlayer;
        if (self is null) return;

        var packet = new RawPacket();
        packet.Packet(13, w =>
        {
            w.Write((byte)Main.myPlayer);
            w.Write(ControlFlags(self));
            w.Write(MovementFlags(self));
            w.Write(MiscFlags(self));
            w.Write(ExtraFlags(self));
            w.Write((byte)self.selectedItem);
            w.Write(position.X);
            w.Write(position.Y);
            var velocity = self.velocity;
            if (velocity.X != 0f || velocity.Y != 0f)
            {
                w.Write(velocity.X);
                w.Write(velocity.Y);
            }
            var mount = self.mount;
            if (mount.Active)
            {
                w.Write((ushort)mount.Type);
            }
            if (self.PotionOfReturnOriginalUsePosition.HasValue)
            {
                var potion = self.PotionOfReturnOriginalUsePosition.Value;
                w.Write(potion.X);
                w.Write(potion.Y);
                var home = self.PotionOfReturnHomePosition.Value;
                w.Write(home.X);
                w.Write(home.Y);
            }
            if (self.netCameraTarget.HasValue)
            {
                var cam = self.netCameraTarget.Value;
                w.Write(cam.X);
                w.Write(cam.Y);
            }
        });
        packet.Send();
    }

    private static byte ControlFlags(Player p)
    {
        byte f = 0;
        if (p.controlUp) f |= 0x01;
        if (p.controlDown) f |= 0x02;
        if (p.controlLeft) f |= 0x04;
        if (p.controlRight) f |= 0x08;
        if (p.controlJump) f |= 0x10;
        if (p.controlUseItem) f |= 0x20;
        if (p.direction == 1) f |= 0x40;
        return f;
    }

    private static byte MovementFlags(Player p)
    {
        byte f = 0;
        if (p.pulley) f |= 0x01;
        if (p.pulley && p.pulleyDir == 2) f |= 0x02;
        if (p.velocity.X != 0f || p.velocity.Y != 0f) f |= 0x04;
        if (p.vortexStealthActive) f |= 0x08;
        if (p.gravDir == 1f) f |= 0x10;
        if (p.shieldRaised) f |= 0x20;
        if (p.ghost) f |= 0x40;
        if (p.mount.Active) f |= 0x80;
        return f;
    }

    private static byte MiscFlags(Player p)
    {
        byte f = 0;
        if (p.tryKeepingHoveringUp) f |= 0x01;
        if (p.IsVoidVaultEnabled) f |= 0x02;
        if (p.sitting.isSitting) f |= 0x04;
        if (p.downedDD2EventAnyDifficulty) f |= 0x08;
        if (p.petting.isPetting) f |= 0x10;
        if (p.petting.isPetSmall) f |= 0x20;
        if (p.PotionOfReturnOriginalUsePosition.HasValue) f |= 0x40;
        if (p.tryKeepingHoveringDown) f |= 0x80;
        return f;
    }

    private static byte ExtraFlags(Player p)
    {
        byte f = 0;
        if (p.sleeping.isSleeping) f |= 0x01;
        if (p.autoReuseAllWeapons) f |= 0x02;
        if (p.controlDownHold) f |= 0x04;
        if (p.isOperatingAnotherEntity) f |= 0x08;
        if (p.controlUseTile) f |= 0x10;
        if (p.netCameraTarget.HasValue) f |= 0x20;
        if (p.lastItemUseAttemptSuccess) f |= 0x40;
        return f;
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
