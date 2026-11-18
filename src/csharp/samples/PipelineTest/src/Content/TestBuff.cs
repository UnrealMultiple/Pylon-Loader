using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试增益：金色箭头图标；挂上后每个玩家回 2 点生命并记一次 UpdatePlayer。贴图 assets/buffs/TestBuff.png。</summary>
public sealed class TestBuff : ModBuff
{
    public static int UpdateCount;

    public override string DisplayName => "测试增益";
    public override string Description => "这是一行测试说明（每帧回 2 点生命）";

    public override void SetStaticDefaults()
    {
        Main.debuff[Type] = false;
        Main.buffNoSave[Type] = false;
    }

    public override void Update(Player player, ref int buffIndex)
    {
        UpdateCount++;
        // lifeRegen 是"回复单位"：1 点/帧 ≈ 每秒 0.5 血；给 20 才肉眼可见（约 10 血/秒）。
        player.lifeRegen += 20;
    }

    public override void ModifyBuffText(ref string buffName, ref string tip, ref int rare)
    {
        if (!BuffProbe.Active) return;
        BuffProbe.TextCalls++;
        BuffProbe.TextNameIn = buffName;
        BuffProbe.TextTipIn = tip;
        if (BuffProbe.ModifyText)
        {
            buffName = "改X";
            tip = "改Y";
        }
    }
}

/// <summary>第二个测试增益：ReApply / NPC Update 探针。</summary>
public sealed class TestBuff2 : ModBuff
{
    public override string TexturePath => "assets/buffs/TestBuff.png";

    public override void SetStaticDefaults() => Main.debuff[Type] = false;

    public override void Update(Player player, ref int buffIndex) => BuffProbe.Mod2Updates++;

    public override void Update(NPC npc, ref int buffIndex) => BuffProbe.Mod2NpcUpdates++;

    public override bool ReApply(Player player, int time, int buffIndex)
    {
        if (!BuffProbe.Active) return false;
        BuffProbe.ModReApplies++;
        return BuffProbe.VetoReApplyMod;
    }

    public override bool ReApply(NPC npc, int time, int buffIndex)
    {
        if (!BuffProbe.Active) return false;
        BuffProbe.ModNpcReApplies++;
        return BuffProbe.VetoReApplyMod;
    }
}

/// <summary>全局 buff 探针：Update / ReApply / 文字 / 自定义说明分派计数。</summary>
public sealed class TestBuffGlobal : GlobalBuff
{
    /// <summary>Update 计数不参与 BuffProbe.Reset：世界窗口用例跨组取差值，共享门控会被交互组关掉。</summary>
    public static int PlayerUpdates, NpcUpdates;

    public override void Update(int type, Player player, ref int buffIndex) => PlayerUpdates++;

    public override void Update(int type, NPC npc, ref int buffIndex) => NpcUpdates++;

    public override bool ReApply(int type, Player player, int time, int buffIndex)
    {
        if (!BuffProbe.Active) return false;
        BuffProbe.GlobalReApplies++;
        return BuffProbe.VetoReApplyGlobal;
    }

    public override bool ReApply(int type, NPC npc, int time, int buffIndex)
    {
        if (!BuffProbe.Active) return false;
        BuffProbe.GlobalNpcReApplies++;
        return BuffProbe.VetoReApplyGlobal;
    }

    public override void ModifyBuffText(int type, ref string buffName, ref string tip, ref int rare)
    {
        if (BuffProbe.Active) BuffProbe.GlobalTextCalls++;
    }

    public override void CustomBuffTipSize(string buffTip, List<Vector2> sizes)
    {
        if (!BuffProbe.Active) return;
        BuffProbe.TipSizeCalls++;
        if (BuffProbe.AppendTipSize) sizes.Add(new Vector2(BuffProbe.TipWidth, BuffProbe.TipHeight));
    }

    public override void DrawCustomBuffTip(string buffTip, SpriteBatch spriteBatch, int originX, int originY)
    {
        if (!BuffProbe.Active) return;
        BuffProbe.DrawTipCalls++;
        BuffProbe.TipOriginX = originX;
        BuffProbe.TipOriginY = originY;
    }
}

internal static class BuffProbe
{
    internal static bool Active, ModifyText, AppendTipSize, VetoReApplyMod, VetoReApplyGlobal;
    internal static float TipWidth, TipHeight, TipOriginX, TipOriginY;
    internal static string TextNameIn = "", TextTipIn = "";
    internal static int Mod2Updates, Mod2NpcUpdates, GlobalPlayerUpdates, GlobalNpcUpdates;
    internal static int ModReApplies, GlobalReApplies, ModNpcReApplies, GlobalNpcReApplies;
    internal static int TextCalls, GlobalTextCalls, TipSizeCalls, DrawTipCalls;

    internal static void Reset()
    {
        Active = ModifyText = AppendTipSize = VetoReApplyMod = VetoReApplyGlobal = false;
        TipWidth = TipHeight = TipOriginX = TipOriginY = 0f;
        TextNameIn = TextTipIn = "";
        Mod2Updates = Mod2NpcUpdates = GlobalPlayerUpdates = GlobalNpcUpdates = 0;
        ModReApplies = GlobalReApplies = ModNpcReApplies = GlobalNpcReApplies = 0;
        TextCalls = GlobalTextCalls = TipSizeCalls = DrawTipCalls = 0;
    }
}
