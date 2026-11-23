using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.ObjectInteractions;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>低频方块交互：锁动作、坐卧目标与锤坡；不以回调位集屏蔽默认虚方法。</summary>
internal static class TileInteractionHooks
{
    private static readonly HashSet<(Type Type, string Hook)> Reported = [];

    internal static void Install()
    {
        On.Terraria.Chest.IsLocked_int_int += IsLocked;
        On.Terraria.Chest.Unlock += Unlock;
        On.Terraria.Chest.Lock += Lock;
        On.Terraria.GameContent.PlayerSittingHelper.GetSittingTargetInfo += Sitting;
        On.Terraria.GameContent.PlayerSleepingHelper.GetSleepingTargetInfo += Sleeping;
        On.Terraria.WorldGen.SlopeTile += Slope;
        On.Terraria.WorldGen.PoundTile += Pound;
        On.Terraria.Player.CheckSpawn += CheckSpawn;
        On.Terraria.GameContent.ObjectInteractions.TileSmartInteractCandidateProvider.FillPotentialTargetTiles += SmartInteract;
    }

    private static void SmartInteract(
        On.Terraria.GameContent.ObjectInteractions.TileSmartInteractCandidateProvider.orig_FillPotentialTargetTiles orig,
        TileSmartInteractCandidateProvider self, SmartInteractScanSettings settings)
    {
        orig(self, settings);

        if (Main.tile is null) return;

        var lx = Math.Max(0, settings.LX);
        var ly = Math.Max(0, settings.LY);
        var hx = Math.Min(Main.maxTilesX - 1, settings.HX);
        var hy = Math.Min(Main.maxTilesY - 1, settings.HY);
        var targets = self.targets;

        for (var i = lx; i <= hx; i++)
            for (var j = ly; j <= hy; j++)
            {
                var tile = Main.tile[i, j];
                if (!tile.active()) continue;

                var mod = TileTable.Of(tile.type);
                if (mod is null) continue;
                if (!mod.HookEnabled(HookId.HasSmartInteract) && !mod.HookEnabled(HookId.RightClick)) continue;

                bool smart;
                try
                {
                    smart = mod.HasSmartInteract(i, j, settings);
                }
                catch (Exception e)
                {
                    Fail(mod, nameof(ModTile.HasSmartInteract), e);
                    continue;
                }

                if (!smart) continue;

                var value = self.JoinValue(i, j);
                var exists = false;
                for (var k = 0; k < targets.Count; k++)
                {
                    if (targets[k] != value) continue;
                    exists = true;
                    break;
                }

                if (!exists) targets.Add(value);
            }
    }

    private static ModTile? At(int x, int y)
    {
        if (Main.tile is null || x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY) return null;
        var tile = Main.tile[x, y];
        return tile.active() ? TileTable.Of(tile.type) : null;
    }

    private static void Fail(ModType author, string hook, Exception e)
    {
        if (Reported.Add((author.GetType(), hook)))
            ContentErrors.Line($"[tile] {author.GetType().Name}.{hook} 异常:{Safe.Dump(e)}");
    }

    private static bool IsLocked(On.Terraria.Chest.orig_IsLocked_int_int orig, int x, int y)
    {
        var tile = At(x, y);
        if (tile is null) return orig(x, y);
        try { return tile.IsLockedChest(x, y); }
        catch (Exception e)
        {
            Fail(tile, nameof(ModTile.IsLockedChest), e);
            return false;
        }
    }

    private static bool Unlock(On.Terraria.Chest.orig_Unlock orig, int x, int y)
    {
        var tile = At(x, y);
        if (tile is null) return orig(x, y);
        var shift = -36;
        var dust = 11;
        var manual = false;
        try
        {
            if (!tile.UnlockChest(x, y, ref shift, ref dust, ref manual)) return false;
        }
        catch (Exception e)
        {
            Fail(tile, nameof(ModTile.UnlockChest), e);
            return false;
        }
        if (!manual) Shift(x, y, tile.Type, shift, dust);
        return true;
    }

    private static bool Lock(On.Terraria.Chest.orig_Lock orig, int x, int y)
    {
        var tile = At(x, y);
        if (tile is null) return orig(x, y);
        var shift = 36;
        var manual = false;
        try
        {
            if (!tile.LockChest(x, y, ref shift, ref manual)) return false;
        }
        catch (Exception e)
        {
            Fail(tile, nameof(ModTile.LockChest), e);
            return false;
        }
        if (!manual) Shift(x, y, tile.Type, shift, -1);
        return true;
    }

    private static void Shift(int x, int y, int type, int shift, int dust)
    {
        Terraria.Audio.SoundEngine.PlaySound(SoundID.Unlock, x * 16, y * 16, 1, 1f, 0f);
        for (var i = x; i <= x + 1 && i < Main.maxTilesX; i++)
            for (var j = y; j <= y + 1 && j < Main.maxTilesY; j++)
            {
                var tile = Main.tile[i, j];
                if (!tile.active() || tile.type != type) continue;
                tile.frameX = (short)(tile.frameX + shift);
                if (dust < 0) continue;
                for (var k = 0; k < 4; k++)
                    Dust.NewDust(new Vector2(i * 16, j * 16), 16, 16, dust, 0, 0, 0, default, 1);
            }
    }

    private static Vector2 Position(TileRestingInfo info, int extraRow) =>
        new(info.AnchorTilePosition.X * 16 + 8 + info.TargetDirection * info.DirectionOffset + info.FinalOffset.X,
            (info.AnchorTilePosition.Y + extraRow) * 16 + 16 + info.FinalOffset.Y);

    private static bool Sitting(On.Terraria.GameContent.PlayerSittingHelper.orig_GetSittingTargetInfo orig,
                                Player player, int x, int y, out int direction, out Vector2 position,
                                out Vector2 seatOffset, out ExtraSeatInfo extra)
    {
        if (!orig(player, x, y, out direction, out position, out seatOffset, out extra)) return false;
        var tile = At(x, y);
        if (tile is null) return true;
        var info = new TileRestingInfo(x, y, seatOffset, direction, 6) { ExtraInfo = extra };
        try { tile.ModifySittingTargetInfo(x, y, info); }
        catch (Exception e) { Fail(tile, nameof(ModTile.ModifySittingTargetInfo), e); }
        direction = info.TargetDirection;
        position = Position(info, 0);
        seatOffset = info.VisualOffset;
        extra = info.ExtraInfo;
        return true;
    }

    private static bool Sleeping(On.Terraria.GameContent.PlayerSleepingHelper.orig_GetSleepingTargetInfo orig,
                                 int x, int y, out int direction, out Vector2 anchor, out Vector2 visualOffset)
    {
        if (!orig(x, y, out direction, out anchor, out visualOffset)) return false;
        var tile = At(x, y);
        if (tile is null) return true;
        var info = new TileRestingInfo((int)MathF.Round((anchor.X - 8) / 16),
                                      (int)MathF.Round((anchor.Y - 16) / 16) - 1, visualOffset, direction);
        try { tile.ModifySleepingTargetInfo(x, y, info); }
        catch (Exception e) { Fail(tile, nameof(ModTile.ModifySleepingTargetInfo), e); }
        direction = info.TargetDirection;
        anchor = Position(info, 1);
        visualOffset = info.VisualOffset;
        return true;
    }

    private static bool AllowSlope(int x, int y)
    {
        if (Main.tile is null || x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY) return true;
        var type = Main.tile[x, y].type;
        var tile = TileTable.Of(type);
        if (tile is not null)
        {
            try { if (!tile.Slope(x, y)) return false; }
            catch (Exception e) { Fail(tile, nameof(ModTile.Slope), e); }
        }
        var globals = GlobalTileRegistry.For(HookId.Slope);
        for (var index = 0; index < globals.Count; index++)
        {
            var global = globals[index];
            try { if (!global.Slope(x, y, type)) return false; }
            catch (Exception e) { Fail(global, nameof(GlobalTile.Slope), e); }
        }
        return true;
    }

    private static bool Slope(On.Terraria.WorldGen.orig_SlopeTile orig, int x, int y, int slope,
                              bool noEffects, bool quiet) =>
        AllowSlope(x, y) && orig(x, y, slope, noEffects, quiet);

    private static bool Pound(On.Terraria.WorldGen.orig_PoundTile orig, int x, int y) =>
        AllowSlope(x, y) && orig(x, y);

    /// <summary>
    /// TileID.Sets.IsValidSpawnPoint：标了 true 的 mod 方块在出生判定里临时换装成床
    /// （原版 CheckSpawn 只认床；跟 Bunny/tML 同一套交换手法）。
    /// </summary>
    private static bool CheckSpawn(On.Terraria.Player.orig_CheckSpawn orig, int x, int y)
    {
        if (Main.tile is not null && x >= 0 && x < Main.maxTilesX && y > 0 && y < Main.maxTilesY)
        {
            var tile = Main.tile[x, y - 1];
            if (tile.active())
            {
                var type = tile.type;
                if (TileTable.Has(type) && TileIdTml.IsSpawnPoint(type))
                {
                    tile.type = TileID.Beds;
                    try { return orig(x, y); }
                    finally
                    {
                        var restore = Main.tile[x, y - 1];
                        restore.type = type;
                    }
                }
            }
        }

        return orig(x, y);
    }
}
