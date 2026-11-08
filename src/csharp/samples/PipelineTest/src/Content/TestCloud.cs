using Pylon.ModLoader;

namespace PipelineTest.Content;

/// <summary>测试云：权重 3、颜色染洋红。贴图 64x64（棋盘）。</summary>
public sealed class TestCloud : ModCloud
{
    public static int OnSpawnCount;

    /// <summary>云贴图不在基类默认路径里：显式声明。</summary>
    public override string TexturePath => "assets/clouds/TestCloud.png";

    public override int SpawnChance() => 3;

    public override Il2CppMicrosoft.Xna.Framework.Graphics.Color CloudColor(
        Terraria.Cloud cloud, Il2CppMicrosoft.Xna.Framework.Graphics.Color color) =>
        new(255, 100, 255, color.A);

    public override void OnSpawn(Terraria.Cloud cloud) => OnSpawnCount++;
}

/// <summary>稀有测试云：RareCloud=true，权重 1；贴图是带字的高级云。</summary>
public sealed class TestRareCloud : ModCloud
{
    public override string TexturePath => "assets/clouds/TestRareCloud.png";

    public override bool RareCloud => true;

    public override int SpawnChance() => 1;
}
