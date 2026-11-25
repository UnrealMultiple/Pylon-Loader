using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Terraria;

namespace CheatMenu.Features;

/// <summary>
/// 幽灵模式。1:1 对应原版 <c>GhostTool</c>：
/// 本地玩家跳过地形/液体/斜坡碰撞与台阶步进，AddBuff 353 被拦下；
/// 每帧按方向键直接改 position/velocity（无重力、无坠落伤害）。
/// </summary>
public sealed class GhostFeature : CheatFeature
{
    public override string Id => "player.ghost";
    public override string Title => "幽灵模式";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "主要";

    public override void Install()
    {
        On.Terraria.Player.SlopeDownMovement += SlopeDownMovement;
        On.Terraria.Player.TileCollision += TileCollision;
        On.Terraria.Player.DryCollision += DryCollision;
        On.Terraria.Player.WetCollision += WetCollision;
        On.Terraria.Player.SlopingCollision += SlopingCollision;
        On.Terraria.Player.AddBuff += AddBuff;
        On.Terraria.Collision.StepDown += StepDown;
        On.Terraria.Collision.StepUp += StepUp;
        On.Terraria.Player.Update += Update;
    }

    private static bool Active() =>
        CheatFeatureHost.Config is { GhostEnabled: true } && !Main.gameMenu;

    private static bool ActiveFor(Player player) =>
        Active() && player is not null && player.whoAmI == Main.myPlayer;

    private static void SlopeDownMovement(On.Terraria.Player.orig_SlopeDownMovement orig, Player self)
    {
        if (ActiveFor(self)) return;
        orig(self);
    }

    private static Vector2 TileCollision(On.Terraria.Player.orig_TileCollision orig, Player self,
        Vector2 position, Vector2 velocity, bool fallThrough, bool ignorePlats)
    {
        if (ActiveFor(self)) return velocity;
        return orig(self, position, velocity, fallThrough, ignorePlats);
    }

    private static void DryCollision(On.Terraria.Player.orig_DryCollision orig, Player self,
        bool canFallThrough, bool ignorePlats)
    {
        if (ActiveFor(self)) return;
        orig(self, canFallThrough, ignorePlats);
    }

    private static void WetCollision(On.Terraria.Player.orig_WetCollision orig, Player self,
        bool fallThrough, bool ignorePlats, float movementSpeed)
    {
        if (ActiveFor(self)) return;
        orig(self, fallThrough, ignorePlats, movementSpeed);
    }

    private static void SlopingCollision(On.Terraria.Player.orig_SlopingCollision orig, Player self,
        bool canFallThrough, bool ignorePlats)
    {
        if (ActiveFor(self)) return;
        orig(self, canFallThrough, ignorePlats);
    }

    private static void AddBuff(On.Terraria.Player.orig_AddBuff orig, Player self,
        int type, int time, bool fromNetPvP)
    {
        // 原版只拦幽灵 buff（353），且作用于本地玩家。
        if (ActiveFor(self) && type == 353) return;
        orig(self, type, time, fromNetPvP);
    }

    private static void StepDown(On.Terraria.Collision.orig_StepDown orig, ref Vector2 pos, ref Vector2 vel,
        int width, int height, ref float stepSpeed, ref float gfxOffY, int gravDir, bool waterWalk)
    {
        if (Active()) return;
        orig(ref pos, ref vel, width, height, ref stepSpeed, ref gfxOffY, gravDir, waterWalk);
    }

    private static void StepUp(On.Terraria.Collision.orig_StepUp orig, ref Vector2 pos, ref Vector2 vel,
        int width, int height, ref float stepSpeed, ref float gfxOffY, int gravDir, bool holdsMatching,
        int specialChecksMode)
    {
        if (Active()) return;
        orig(ref pos, ref vel, width, height, ref stepSpeed, ref gfxOffY, gravDir, holdsMatching, specialChecksMode);
    }

    private static void Update(On.Terraria.Player.orig_Update orig, Player self, int i)
    {
        if (ActiveFor(self)) ApplyMovement(self);
        orig(self, i);
    }

    /// <summary>原版 Ghost()：清零重力/坠落伤害，按方向键平移。</summary>
    private static void ApplyMovement(Player player)
    {
        var cfg = CheatFeatureHost.Config;
        var speed = Math.Clamp(cfg?.GhostSpeed ?? 25, 1, 100);

        player.maxFallSpeed = 0f;
        player.gravity = 0f;
        player.noFallDmg = true;

        var pos = player.position;
        player.oldPosition = pos;

        var move = new Vector2(0f, 0f);
        if (player.controlLeft) move.X -= 1f;
        if (player.controlRight) move.X += 1f;
        if (player.controlUp) move.Y -= 1f;
        if (player.controlDown) move.Y += 1f;

        player.position = pos + move * speed;
        player.velocity = move * speed;
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("幽灵模式", "GHOST");
        ModernUI.BeginRow();
        ModernUI.RowLabel("幽灵模式");
        if (ModernUI.Toggle("##ghost", cfg.GhostEnabled))
        {
            cfg.GhostEnabled = !cfg.GhostEnabled;
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();

        if (cfg.GhostEnabled)
        {
            var speed = (float)cfg.GhostSpeed;
            if (ModernUI.RowSlider("##ghostspeed", "移动速度", "1-100", ref speed, 1f, 100f, "0"))
            {
                cfg.GhostSpeed = Math.Clamp((int)speed, 1, 100);
                CheatFeatureHost.MarkDirty();
            }
        }

        ModernUI.EndCard();
    }
}