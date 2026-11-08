using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ObjectData;

namespace PipelineTest.Content;

/// <summary>
/// 测试墙：挖/尘土/声音/掉落/发光/随机更新/边框/动画全计数；贴图 assets/walls/TestWall.png。
/// </summary>
public sealed class TestWall : ModWall
{
    public static int Kills, Dusts, Sounds, Drops, Lights, Randoms, FrameCalls, LastDrop;
    public static bool LastFail;

    public override void SetStaticDefaults()
    {
        Main.wallHouse[Type] = true;
        AddMapEntry(new Color(150, 150, 150));
    }

    /// <summary>挖墙尘土：石头。</summary>
    public override int DustType => DustID.Stone;

    public override void KillWall(int i, int j, ref bool fail)
    {
        Kills++;
        LastFail = fail;
    }

    public override void NumDust(int i, int j, bool fail, ref int num) => num = fail ? 2 : 5;

    public override bool CreateDust(int i, int j, ref int type)
    {
        Dusts++;
        return true;
    }

    public override bool KillSound(int i, int j, bool fail)
    {
        Sounds++;
        return true;
    }

    public override bool Drop(int i, int j, ref int type)
    {
        Drops++;
        LastDrop = type;
        return true; // 掉默认（放置物品）
    }

    public override bool WallFrame(int i, int j, bool randomizeFrame, ref int style, ref int frameNumber)
    {
        FrameCalls++;
        if (randomizeFrame) frameNumber = 2;
        return true;
    }

    public override void AnimateWall(ref int frame, ref int frameCounter)
    {
        if (++frameCounter >= 5)
        {
            frameCounter = 0;
            frame = (frame + 1) % 2;
        }
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        Lights++;
        r = 0.5f;
    }

    public override void RandomUpdate(int i, int j) => Randoms++;
}

/// <summary>只挂掉落与地图项的对照墙（ItemDrop 属性决定掉什么）。</summary>
public sealed class TestWallPlain : ModWall
{
    /// <summary>挖掉掉的物品：石头（放置物品优先级更高时用不到）。</summary>
    public override int ItemDrop => ItemID.StoneBlock;

    public override void SetStaticDefaults()
    {
        AddMapEntry(new Color(90, 90, 90));
    }
}

/// <summary>放置测试墙的物品。</summary>
public sealed class TestWallItem : ModItem
{
    public override string DisplayName => "测试墙";

    public override void SetDefaults(Item item) => item.DefaultToPlaceableWall((ushort)ModContent.WallType<TestWall>());
}
