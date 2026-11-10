using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

internal static class BuffInteractionTests
{
    public static void Register()
    {
        PT.Group("buff-interaction-w", Phase.World, Run);
        PT.Group("buff-draw-w", Phase.World, RunDraw);
    }

    private static int _draws;
    private static void Draw(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float orig,
        Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch self, Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D texture,
        Il2CppMicrosoft.Xna.Framework.Vector2 position, Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle> source,
        Il2CppMicrosoft.Xna.Framework.Graphics.Color color, float rotation, Il2CppMicrosoft.Xna.Framework.Vector2 origin,
        float scale, Il2CppMicrosoft.Xna.Framework.Graphics.SpriteEffects effects, float depth)
    {
        if (BuffInteractionProbe.Active && color == Il2CppMicrosoft.Xna.Framework.Graphics.Color.Red &&
            source.HasValue && source.Value.Width == 16 && source.Value.Height == 16)
            _draws++;
        orig(self, texture, position, source, color, rotation, origin, scale, effects, depth);
    }

    private static void RunDraw()
    {
        var player = Main.LocalPlayer;
        var type = ModContent.BuffType<TestBuffInteraction>();
        var prior = player.FindBuffIndex(type);
        var oldTime = prior >= 0 ? player.buffTime[prior] : 0;
        _draws = 0;
        BuffInteractionProbe.Reset();
        BuffInteractionProbe.Active = true;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float += Draw;
        player.AddBuff(type, 3600, true);
        PT.Later("BUFF-D1", 60, c =>
        {
            try
            {
                c.True(BuffInteractionProbe.PreDraws > 0, "实际图标帧触发PreDraw");
                c.True(BuffInteractionProbe.PostDraws > 0, "实际图标帧触发PostDraw");
                c.True(_draws > 0, "真实SpriteBatch收到作者红色与16x16裁剪参数");
            }
            finally
            {
                BuffInteractionProbe.Active = false;
                BuffInteractionProbe.Reset();
                On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float -= Draw;
                var slot = player.FindBuffIndex(type);
                if (slot >= 0)
                {
                    if (prior >= 0) player.buffTime[slot] = oldTime;
                    else player.DelBuff(slot);
                }
            }
        });
    }

    private static void Run()
    {
        var player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            PT.SkipCase("BUFF-I1", "玩家还没就绪");
            return;
        }
        var type = ModContent.BuffType<TestBuffInteraction>();
        var types = new int[player.buffType.Length];
        var times = new int[player.buffTime.Length];
        for (var i = 0; i < types.Length; i++) types[i] = player.buffType[i];
        for (var i = 0; i < times.Length; i++) times[i] = player.buffTime[i];
        var gui = new GUIBuffs();
        BuffInteractionProbe.Active = true;
        try
        {
            void Prepare()
            {
                BuffInteractionProbe.Reset();
                for (var i = 0; i < player.buffType.Length; i++) player.buffType[i] = 0;
                for (var i = 0; i < player.buffTime.Length; i++) player.buffTime[i] = 0;
                player.buffType[0] = type;
                player.buffTime[0] = 777;
                player.buffType[1] = Terraria.ID.BuffID.WellFed;
                player.buffTime[1] = 333;
            }
            PT.Case("BUFF-I1", c =>
            {
                Prepare();
                BuffInteractionProbe.VetoGlobal = true;
                player.DelBuff(0);
                c.Eq(BuffInteractionProbe.GlobalClicks, 0, "逻辑删除不调用RightClick");
                c.Eq(BuffInteractionProbe.ModClicks, 0, "逻辑删除不调用Mod RightClick");
                c.Eq(BuffInteractionProbe.RemovedTime, 777, "OnRemove收到删除前time");
                c.Eq(BuffInteractionProbe.RemovedCount, 1, "OnRemove只调用一次");
            });
            PT.Case("BUFF-I2", c =>
            {
                Prepare();
                BuffInteractionProbe.VetoGlobal = true;
                gui.RemoveBuff(0);
                c.Eq(player.buffType[0], type, "Global否决手动取消");
                c.Eq(BuffInteractionProbe.GlobalClicks, 1, "手动调用Global RightClick");
                c.Eq(BuffInteractionProbe.ModClicks, 0, "Global否决短路Mod");
                Prepare();
                BuffInteractionProbe.VetoMod = true;
                gui.RemoveBuff(0);
                c.Eq(player.buffType[0], type, "Mod否决手动取消");
                c.Eq(BuffInteractionProbe.GlobalClicks, 1, "Global先执行");
                c.Eq(BuffInteractionProbe.ModClicks, 1, "Mod RightClick执行");
            });
            PT.Case("BUFF-I3", c =>
            {
                Prepare();
                BuffInteractionProbe.VetoRemove = true;
                player.DelBuff(0);
                c.Eq(player.buffType[0], type, "既有CanRemove仍可否决逻辑删除");
                c.Eq(BuffInteractionProbe.RemovedCount, 0, "否决不发OnRemove");
                Prepare();
                gui.RemoveBuff(0);
                c.Eq(BuffInteractionProbe.GlobalClicks, 1, "正常手动Global一次");
                c.Eq(BuffInteractionProbe.ModClicks, 1, "正常手动Mod一次");
                c.True(player.buffType[0] != type, "正常手动取消完成");
                c.Eq(BuffInteractionProbe.RemovedTime, 777, "手动取消保留移槽前time");
            });
        }
        finally
        {
            BuffInteractionProbe.Active = false;
            BuffInteractionProbe.Reset();
            for (var i = 0; i < types.Length; i++) player.buffType[i] = types[i];
            for (var i = 0; i < times.Length; i++) player.buffTime[i] = times[i];
        }
    }
}
