using Terraria;
using Terraria.Enums;

namespace Pylon.ModLoader;

/// <summary>作用于所有 tile（游戏的与 mod 的）的横切类；一个类一个实例，钩子收到类型自己挑。</summary>
public abstract class GlobalTile : ModType
{
    /// <summary>只放共享数值（TileObjectData 在这段时间里可以改原版方块的形状/锚点）。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>所有内容安装完成之后一次（跨 Mod 引用放这里）。</summary>
    public virtual void PostSetupContent() { }

    /// <summary>SetupContent 之后一次：改 Main.tileMerge 的地方。</summary>
    public virtual void PostSetupTileMerge() { }

    /// <summary>这个 tile 能不能被挖；返回 false = 挖不动（blockDamaged 是可写引用）。</summary>
    public virtual bool CanKillTile(int i, int j, int type, ref bool blockDamaged) => true;

    /// <summary>这个 tile 要没了之前（三个都是可写引用）。</summary>
    public virtual void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem) { }

    /// <summary>出几粒尘土（num 是可写引用；只会被调小）。</summary>
    public virtual void NumDust(int i, int j, int type, bool fail, ref int num) { }

    /// <summary>每一粒尘土；返回 false = 这粒不出，dustType 可换尘土类型。</summary>
    public virtual bool CreateDust(int i, int j, int type, ref int dustType) => true;

    /// <summary>挖掉的声音；返回 false = 不播。</summary>
    public virtual bool KillSound(int i, int j, int type, bool fail) => true;

    /// <summary>这个方块掉不掉东西；返回 false = 什么都不掉（多格家具按自己的代码掉）。</summary>
    public virtual bool CanDrop(int i, int j, int type) => true;

    /// <summary>这块的掉落之后：用来掉别的东西。</summary>
    public virtual void Drop(int i, int j, int type) { }

    /// <summary>玩家放置这个 tile；返回 false = 放不下去。</summary>
    public virtual bool CanPlace(int i, int j, int type) => true;

    /// <summary>玩家刚把它放下（item 是手里那个）。</summary>
    public virtual void PlaceInWorld(int i, int j, int type, Item item) { }

    /// <summary>投射物炸这一格；返回 false = 这格留着。</summary>
    public virtual bool CanExplode(int i, int j, int type) => true;

    /// <summary>玩家的锤子敲这格；返回 false = 保持原样。</summary>
    public virtual bool Slope(int i, int j, int type) => true;

    /// <summary>世界的随机更新（underground = 走的是地下那套）。</summary>
    public virtual void RandomUpdate(int i, int j, int type, bool underground) { }

    /// <summary>电线信号；返回 false = 信号到不了这格（游戏的与 HitWire 都不跑）。</summary>
    public virtual bool PreHitWire(int i, int j, int type) => true;

    /// <summary>电线信号通过这格。</summary>
    public virtual void HitWire(int i, int j, int type) { }

    /// <summary>玩家点了这格。</summary>
    public virtual void RightClick(int i, int j, int type) { }

    /// <summary>光标/手指在这格上面（近处）。</summary>
    public virtual void MouseOver(int i, int j, int type) { }

    /// <summary>光标/手指离得远时也在这格上面。</summary>
    public virtual void MouseOverFar(int i, int j, int type) { }

    /// <summary>每帧一次，整个类型共用（Main.tileFrame、Main.tileFrameCounter）。</summary>
    public virtual void AnimateTile() { }

    /// <summary>type 这种 tile 在玩家附近还算作哪些类型（制作站）。</summary>
    public virtual int[] AdjTiles(int type) => [];

    /// <summary>摇树：在掉落之前（treeType 是 TreeTypes）。</summary>
    public virtual void PreShakeTree(int x, int y, TreeTypes treeType) { }

    /// <summary>摇树：返回 true = 主要物品已经掉了（游戏不再掉它的）。</summary>
    public virtual bool ShakeTree(int x, int y, TreeTypes treeType) => false;
}
