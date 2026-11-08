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

/// <summary>带方块实体的测试方块：放下即建实体，拆掉即摘实体（实体由 TileEntity 属性声明）。</summary>
public sealed class TestEntityTile : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        AddMapEntry(new Color(120, 90, 160));
    }

    /// <summary>这块方块放下时要建的实体（写类名，不是 FullName）。</summary>
    public override string? TileEntity => nameof(TestTileEntity);
}

/// <summary>测试方块实体：每帧回调、放置 / 拆除、存档 / 同步都计数，且每个实体有自己的状态。</summary>
public sealed class TestTileEntity : ModTileEntity
{
    public static int Updates, PreGlobals, PostGlobals, KillCalls, Saves, LoadDataCalls, NetSends;

    /// <summary>每个实体自己的一份状态（验证新实体不是共用模板字段）。</summary>
    public int Ticks;

    public override bool IsTileValidForEntity(int x, int y)
    {
        if (!WorldGen.InWorld(x, y)) return false;
        return Main.tile[x, y].type == ModContent.TileType<TestEntityTile>();
    }

    public override void Update()
    {
        Updates++;
        Ticks++;
    }

    public override void PreGlobalUpdate() => PreGlobals++;

    public override void PostGlobalUpdate() => PostGlobals++;

    public override void OnKill() => KillCalls++;

    public override void SaveData(TagCompound tag)
    {
        Saves++;
        tag["ticks"] = Ticks;
    }

    public override void LoadData(TagCompound tag)
    {
        LoadDataCalls++;
        Ticks = tag.GetInt("ticks");
    }

    public override void NetSend(NetWriter writer)
    {
        NetSends++;
        writer.WriteInt32(Ticks);
    }

    public override void NetReceive(NetReader reader) => Ticks = reader.ReadInt32();
}
