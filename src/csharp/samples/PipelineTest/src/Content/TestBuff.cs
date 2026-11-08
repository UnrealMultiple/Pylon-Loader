using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试增益：金色箭头图标；挂上后每个玩家回 2 点生命并记一次 UpdatePlayer。贴图 assets/buffs/TestBuff.png。</summary>
public sealed class TestBuff : ModBuff
{
    public static int ApplyCount;
    public static int UpdateCount;

    public override string DisplayName => "测试增益";
    public override string Description => "这是一行测试说明（每帧回 2 点生命）";

    public override void SetStaticDefaults()
    {
        Main.debuff[Type] = false;
        Main.buffNoSave[Type] = false;
    }

    public override void ApplyPlayer(Player player, int buffTime) => ApplyCount++;

    public override void UpdatePlayer(Player player, int buffIndex)
    {
        UpdateCount++;
        // lifeRegen 是"回复单位"：1 点/帧 ≈ 每秒 0.5 血；给 20 才肉眼可见（约 10 血/秒）。
        player.lifeRegen += 20;
    }
}
