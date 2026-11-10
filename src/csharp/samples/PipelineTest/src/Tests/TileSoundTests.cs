using Il2CppMicrosoft.Xna.Framework.Audio;
using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

internal static class TileSoundTests
{
    public static void Register() => PT.Group("tile-sound-w", Phase.World, Run);

    private static SoundEffectInstance Sound(
        On.Terraria.Audio.SoundEngine.orig_PlaySound_int_int_int_int_float_float orig,
        int type, int x, int y, int style, float volumeScale, float pitchOffset)
    {
        if (!TileSoundProbe.Active) return orig(type, x, y, style, volumeScale, pitchOffset);
        TileSoundProbe.SoundCalls++;
        TileSoundProbe.LastSound = type;
        return null!;
    }

    private static void Run()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("TILE-S1", "玩家还没就绪");
            return;
        }

        var x = (int)(player.Center.X / 16f);
        var y = (int)(player.Center.Y / 16f);
        // Tile 是索引式值类型；临时改类型后必须恢复同一格，不能把值复制当成深拷贝。
        var cache = Main.tile[x, y];
        var previousType = cache.type;
        var tileType = ModContent.TileType<TestTileSound>();
        On.Terraria.Audio.SoundEngine.PlaySound_int_int_int_int_float_float += Sound;
        TileSoundProbe.Active = true;
        try
        {
            cache.type = (ushort)tileType;
            PT.Case("TILE-S1", c =>
            {
                TileSoundProbe.Reset();
                WorldGen.KillTile_PlaySounds(x, y, true, cache);
                c.Eq(TileSoundProbe.Order, "MGS", "Mod→Global→自定义音效");
                c.True(TileSoundProbe.LastFail, "fail 原样传递");
                c.Eq(TileSoundProbe.LastType, tileType, "使用缓存类型");
                c.Eq(TileSoundProbe.SoundCalls, 1, "只播放一次");
                c.Eq(TileSoundProbe.LastSound, 0, "零号音效有效");
            });
            PT.Case("TILE-S2", c =>
            {
                TileSoundProbe.Reset();
                TileSoundProbe.ModVeto = true;
                WorldGen.KillTile_PlaySounds(x, y, false, cache);
                c.Eq(TileSoundProbe.Order, "M", "Mod 否决短路");
                c.Eq(TileSoundProbe.SoundCalls, 0, "Mod 否决静音");
                TileSoundProbe.Reset();
                TileSoundProbe.GlobalVeto = true;
                WorldGen.KillTile_PlaySounds(x, y, false, cache);
                c.Eq(TileSoundProbe.Order, "MG", "Global 否决不读音效");
                c.Eq(TileSoundProbe.SoundCalls, 0, "Global 否决静音");
            });
            PT.Case("TILE-S3", c =>
            {
                TileSoundProbe.Reset();
                TileSoundProbe.ThrowMod = true;
                WorldGen.KillTile_PlaySounds(x, y, false, cache);
                c.Eq(TileSoundProbe.Order, "MGS", "Mod 异常后 Global 仍执行");
                c.Eq(TileSoundProbe.SoundCalls, 1, "异常不误静音");
                TileSoundProbe.Reset();
                TileSoundProbe.ThrowGlobal = true;
                WorldGen.KillTile_PlaySounds(x, y, false, cache);
                c.Eq(TileSoundProbe.Order, "MGS", "Global 异常后仍选音效");
            });
            PT.Case("TILE-S4", c =>
            {
                TileSoundProbe.Reset();
                cache.type = (ushort)ModContent.TileType<TestTileSoundPropertyOnly>();
                WorldGen.KillTile_PlaySounds(x, y, false, cache);
                c.Eq(TileSoundProbe.Order, "GS", "只覆写属性仍能选自定义声音");
                c.Eq(TileSoundProbe.SoundCalls, 1, "属性音效只播放一次");
                TileSoundProbe.Reset();
                cache.type = Terraria.ID.TileID.Stone;
                TileSoundProbe.GlobalVeto = true;
                WorldGen.KillTile_PlaySounds(x, y, false, cache);
                c.Eq(TileSoundProbe.GlobalCalls, 1, "Global 也作用于原版方块");
                c.Eq(TileSoundProbe.LastType, (int)Terraria.ID.TileID.Stone, "原版类型传递");
                c.Eq(TileSoundProbe.SoundCalls, 0, "Global 否决原版声音");
            });
        }
        finally
        {
            cache.type = previousType;
            TileSoundProbe.Active = false;
            TileSoundProbe.Reset();
            On.Terraria.Audio.SoundEngine.PlaySound_int_int_int_int_float_float -= Sound;
        }
    }
}
