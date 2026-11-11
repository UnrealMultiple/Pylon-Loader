using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace Pylon.ModLoader;

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
            ContentErrors.Report($"[tile] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
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
        // 默认动作只移动同类型的 2×2 格；manual 作者已经处理的帧和特效不能再改一次。
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
}
