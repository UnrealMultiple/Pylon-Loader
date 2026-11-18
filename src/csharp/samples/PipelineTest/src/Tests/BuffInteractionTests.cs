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

    private static int _draws, _itemDrawCalls, _iconDrawCalls;
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

    private static void CountItemDraw(On.GUIBuffs.orig_ItemDraw orig, GUIBuffs self, ItemGrid_Layout grid,
                                      int index, Il2CppMicrosoft.Xna.Framework.Vector2 position, float scale)
    {
        _itemDrawCalls++;
        orig(self, grid, index, position, scale);
    }

    private static int CountDrawIcon(On.Terraria.Main.orig_DrawBuffIcon orig, int drawBuffText, int buffSlotOnPlayer, int x, int y)
    {
        _iconDrawCalls++;
        return orig(drawBuffText, buffSlotOnPlayer, x, y);
    }

    private static void RunDraw()
    {
        var player = Main.LocalPlayer;
        var type = ModContent.BuffType<TestBuffInteraction>();
        var prior = player.FindBuffIndex(type);
        var oldTime = prior >= 0 ? player.buffTime[prior] : 0;
        _draws = 0;
        _itemDrawCalls = 0;
        _iconDrawCalls = 0;
        BuffInteractionProbe.Reset();
        BuffInteractionProbe.Active = true;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float += Draw;
        On.GUIBuffs.ItemDraw += CountItemDraw;
        On.Terraria.Main.DrawBuffIcon += CountDrawIcon;
        player.AddBuff(type, 3600, true);
        PT.Later("BUFF-D1", 60, c =>
        {
            try
            {
                PT.Info($"入口计数：GUIBuffs.ItemDraw={_itemDrawCalls} Main.DrawBuffIcon={_iconDrawCalls} PreDraws={BuffInteractionProbe.PreDraws}");
                c.True(BuffInteractionProbe.PreDraws > 0, "实际图标帧触发PreDraw");
                c.True(BuffInteractionProbe.PostDraws > 0, "实际图标帧触发PostDraw");
                c.True(_draws > 0, "真实SpriteBatch收到作者红色与16x16裁剪参数");
            }
            finally
            {
                BuffInteractionProbe.Active = false;
                BuffInteractionProbe.Reset();
                On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float -= Draw;
                On.GUIBuffs.ItemDraw -= CountItemDraw;
                On.Terraria.Main.DrawBuffIcon -= CountDrawIcon;
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
                gui.RemoveBuff(0);
                c.Eq(BuffInteractionProbe.GlobalClicks, 1, "正常手动Global一次");
                c.Eq(BuffInteractionProbe.ModClicks, 1, "正常手动Mod一次");
                c.True(player.buffType[0] != type, "正常手动取消完成");
            });
            PT.Case("BUFF-I4", c =>
            {
                ClearAll(player);
                BuffProbe.Reset();
                BuffProbe.Active = true;
                var type2 = ModContent.BuffType<TestBuff2>();
                player.AddBuff(type2, 600, true);
                var slot = player.FindBuffIndex(type2);
                if (slot < 0)
                {
                    c.Fail($"TestBuff2 挂上失败 slot={slot}（debuff={Main.debuff[type2]}）");
                    BuffProbe.Active = false;
                    return;
                }
                c.Eq(BuffProbe.ModReApplies, 0, "首次施加不调 ReApply");
                c.Eq(BuffProbe.GlobalReApplies, 0, "首次施加不调全局 ReApply");

                player.AddBuff(type2, 900, true);
                c.Eq(BuffProbe.GlobalReApplies, 1, "重复施加全局 ReApply 先行");
                c.Eq(BuffProbe.ModReApplies, 1, "重复施加 Mod ReApply");
                c.Eq(player.buffTime[slot], 900, "ReApply false → 原版续时到 900");

                BuffProbe.VetoReApplyMod = true;
                player.AddBuff(type2, 1200, true);
                c.Eq(BuffProbe.ModReApplies, 2, "否决时 Mod ReApply 照调");
                c.Eq(player.buffTime[slot], 900, "Mod true 否决续时");

                BuffProbe.VetoReApplyMod = false;
                BuffProbe.VetoReApplyGlobal = true;
                player.AddBuff(type2, 1500, true);
                c.Eq(BuffProbe.GlobalReApplies, 3, "全局否决时第 3 次调用");
                c.Eq(BuffProbe.ModReApplies, 2, "全局 true 短路 Mod（不新增）");
                c.Eq(player.buffTime[slot], 900, "全局 true 否决续时");

                BuffProbe.VetoReApplyGlobal = false;
                BuffProbe.Active = false;
            });
            PT.Case("BUFF-I5", c =>
            {
                NPC? npc = null;
                foreach (var candidate in new[] { Terraria.ID.NPCID.Zombie, Terraria.ID.NPCID.Bunny, Terraria.ID.NPCID.BlueSlime })
                {
                    var index = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                        (int)player.Center.X, (int)player.Center.Y - 120,
                        candidate, 0, 0f, 0f, 0f, 0f, 0);
                    if (index < 0 || index >= Main.npc.Length || Main.npc[index] is not { active: true } spawned) continue;

                    var immune = spawned.buffImmune;
                    var type2 = ModContent.BuffType<TestBuff2>();
                    if (immune is null || (uint)type2 >= (uint)immune.Length || !immune[type2])
                    {
                        npc = spawned;
                        break;
                    }

                    PT.Info($"候选 {candidate} 对 {type2} 免疫，换下一个");
                    spawned.active = false;
                }

                if (npc is null)
                {
                    c.Fail("没有可用非免疫 NPC（全部候选拒绝 mod 增益）");
                    return;
                }

                BuffProbe.Reset();
                BuffProbe.Active = true;
                var type = ModContent.BuffType<TestBuff2>();
                npc.AddBuff(type, 600, true);
                var slot = npc.FindBuffIndex(type);
                if (slot < 0)
                {
                    c.Fail($"NPC 挂上失败 slot={slot}");
                    BuffProbe.Active = false;
                    npc.active = false;
                    return;
                }
                c.Eq(BuffProbe.ModNpcReApplies, 0, "NPC 首次施加不调");

                npc.AddBuff(type, 900, true);
                c.Eq(BuffProbe.GlobalNpcReApplies, 1, "NPC 重复施加全局先行");
                c.Eq(BuffProbe.ModNpcReApplies, 1, "NPC 重复施加 Mod");
                c.Eq(npc.buffTime[slot], 900, "NPC ReApply false 续时");

                BuffProbe.VetoReApplyMod = true;
                npc.AddBuff(type, 1200, true);
                c.Eq(BuffProbe.ModNpcReApplies, 2, "NPC 否决时照调");
                c.Eq(npc.buffTime[slot], 900, "NPC Mod true 否决续时");

                BuffProbe.VetoReApplyMod = false;
                BuffProbe.Active = false;
                npc.active = false;
            });
            PT.Case("BUFF-I6", c =>
            {
                var textType = ModContent.BuffType<TestBuff>();
                BuffProbe.Reset();
                BuffProbe.Active = true;
                BuffProbe.ModifyText = true;

                var name = Lang.GetBuffName(textType);
                c.Eq(BuffProbe.TextCalls, 1, "GetBuffName 分派 ModifyBuffText 一次");
                c.Eq(name, "改X", "名字被改写");
                c.Eq(BuffProbe.TextNameIn, "测试增益", "name 路径带入原版名字");
                c.Eq(BuffProbe.TextTipIn, "这是一行测试说明（每帧回 2 点生命）", "name 路径带入原版说明");

                var tip = Main.GetBuffTooltip(player, textType);
                c.Eq(tip, "改Y", "GetBuffTooltip 返回改写说明");
                c.Eq(BuffProbe.TextCalls, 2, "tooltip 分派一次（重入门挡内部二次派发）");
                c.Eq(BuffProbe.GlobalTextCalls, 2, "全局文字链同步分派");

                BuffProbe.ModifyText = false;
                BuffProbe.Active = false;
            });
            PT.SkipCase("BUFF-I7",
                "CustomBuffTipSize/DrawCustomBuffTip 分派已接 On.Main.MouseText_DrawBuffTooltip；真实触发需要悬停 buff 图标，" +
                "触摸注入被 SecurityException 拒、直调原版体在异常时原生 abort（00:47 崩溃证据），按 ENV 受限记录");
        }
        finally
        {
            BuffInteractionProbe.Active = false;
            BuffInteractionProbe.Reset();
            for (var i = 0; i < types.Length; i++) player.buffType[i] = types[i];
            for (var i = 0; i < times.Length; i++) player.buffTime[i] = times[i];
        }
    }

    private static void ClearAll(Player player)
    {
        for (var i = 0; i < player.buffType.Length; i++)
        {
            player.buffType[i] = 0;
            player.buffTime[i] = 0;
        }
    }
}
