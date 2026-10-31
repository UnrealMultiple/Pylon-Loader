using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>自定义方块实体：数据/代码挂在放下的家具上，一个类型一个实例，实体在物体的左上角（Position）。</summary>
public abstract class ModTileEntity : ModType
{
    /// <summary>这一个实体的 ID（放进世界时由管线分配；mod 实体 ID 从 0x40000000 起，不在游戏注册表里）。</summary>
    public int ID { get; internal set; }

    /// <summary>所在格的坐标（物体的左上角）。</summary>
    public Point16 Position { get; internal set; }

    /// <summary>有没有覆写 Update（覆写了才进世界的更新循环）。</summary>
    public bool RequiresUpdates => Plan != null && Plan.Has(nameof(Update));

    /// <summary>Mod 名/类名；Mod 还没挂上时是 '?'。</summary>
    public virtual string FullName => (Mod != null ? Mod.Name : "?") + "/" + Name;

    /// <summary>必覆写：这个格子还是不是这个实体（世界加载和服务器放置时查，不过就丢弃）。</summary>
    public virtual bool IsTileValidForEntity(int x, int y) => false;

    /// <summary>注册时调一次（整个类一次，装数据用）。</summary>
    public virtual void Load() { }

    /// <summary>世界的每次更新（只在服务器/单人；客户端的数据走 NetSend）。</summary>
    public virtual void Update() { }

    /// <summary>游戏自己的实体更新之前，一次世界更新一次。</summary>
    public virtual void PreGlobalUpdate() { }

    /// <summary>游戏自己的实体更新之后，一次世界更新一次。</summary>
    public virtual void PostGlobalUpdate() { }

    /// <summary>服务器侧：应客户端请求放进世界时（还没广播给其他人）。</summary>
    public virtual void OnNetPlace() { }

    /// <summary>被 Kill(i, j) 摘掉之前。</summary>
    public virtual void OnKill() { }

    /// <summary>存档时写自己的数据。</summary>
    public virtual void SaveData(TagCompound tag) { }

    /// <summary>读档时取回自己的数据。</summary>
    public virtual void LoadData(TagCompound tag) { }

    /// <summary>自定义放置钩子（配 PlacementHook 用）；默认 -1 = 交给通用放置。</summary>
    public virtual int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate) => -1;
}

// 未移植：静态 ByID/ByPosition 与 CountInWorld/TryGet（靠 TileEntityLoader 注册表反查）、
// Place/Kill/Find 与 Generic_Hook*（放置/摘除/反查流程）、NetSend/NetReceive（原 JS 自造的 NetWriter/NetReader
// 本版游戏没有，网络同步归 Harmony 层管线）、static register（ModType 派生类由管线自动发现）。
