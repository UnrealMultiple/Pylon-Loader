using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Pipelines;
using Pylon.ModLoader.SaveData;
using Terraria.DataStructures;

namespace Pylon.ModLoader.Content;
/// <summary>自定义方块实体：数据/代码挂在放下的家具上，一个类型一个实例，实体在物体的左上角（Position）。</summary>
public abstract class ModTileEntity : ModType
{
    /// <summary>这一个实体的 ID（放进世界时由管线分配；mod 实体 ID 从 0x40000000 起，不在游戏注册表里）。</summary>
    public int ID { get; internal set; }

    /// <summary>世界里所有 mod 方块实体：ID → 实例（只读视图，随放置/拆除更新）。</summary>
    public static IReadOnlyDictionary<int, ModTileEntity> ByID => TileEntityBinding.ByIdView;

    /// <summary>世界里所有 mod 方块实体：左上角格 → 实例。</summary>
    public static IReadOnlyDictionary<Point16, ModTileEntity> ByPosition => TileEntityBinding.ByPositionView;

    /// <summary>所在格的坐标（物体的左上角）。</summary>
    public Point16 Position { get; internal set; }

    /// <summary>有没有覆写 Update（覆写了才进世界的更新循环）。</summary>
    public bool RequiresUpdates => Plan.Has(HookId.Update);

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

    /// <summary>服务器把这个实体发给客户端时写自己的同步数据；只写"会变的东西"，位置与类型由管线带。</summary>
    public virtual void NetSend(NetWriter writer) { }

    /// <summary>客户端收到服务器发来的这个实体时读；读的顺序必须和 <see cref="NetSend"/> 一致。</summary>
    public virtual void NetReceive(NetReader reader) { }

    /// <summary>
    /// 实体建立成功后的回调（对位 TML PlacementHook）：
    /// 管线在放置/读档建好实体后，对新建实例调用一次（服务端/单机）。
    /// 返回 -1 = 没有别的返回值，管线忽略。
    /// </summary>
    public virtual int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate) => -1;

    /// <summary>
    /// 为世界里的一个新实体复制一份实例：默认浅拷贝模板。
    /// 管线不反射造对象，所以"每个实体一份状态"靠这个方法；作者有可变引用字段（列表、缓存）时覆写它给一份干净的。
    /// </summary>
    protected internal virtual ModTileEntity NewInstance() => (ModTileEntity)MemberwiseClone();

    /// <summary>把实体放到 (i, j)（物体的左上角）；返回新实体的 ID，越界或这格已有 mod 实体时 -1。</summary>
    public int Place(int i, int j) => TileEntityPipeline.Instance.Place(this, i, j);

    /// <summary>摘掉 (i, j) 上本类型的实体：先 <see cref="OnKill"/>，再从表里除名。</summary>
    public void Kill(int i, int j) => TileEntityPipeline.Instance.Kill(this, i, j);

    /// <summary>(i, j) 上本类型的实体 ID；没有 = -1。</summary>
    public int Find(int i, int j) => TileEntityPipeline.Find(this, i, j);

    /// <summary>世界里有多少个 mod 方块实体。</summary>
    public static int CountInWorld() => TileEntityBinding.Count;
}
