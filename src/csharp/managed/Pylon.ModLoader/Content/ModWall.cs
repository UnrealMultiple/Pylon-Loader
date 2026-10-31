using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>自定义墙（贴图是 468×180 的帧表）。</summary>
public abstract class ModWall : ModType
{
    /// <summary>一条地图项：颜色 + 名字（名字是本地化键）。</summary>
    public struct MapEntry
    {
        public Color Color;
        public LocalizedText Name;
    }

    /// <summary>贴图相对 Mod 目录的路径（墙是帧表，非单图）。</summary>
    public override string TexturePath => $"assets/walls/{Name}.png";

    /// <summary>挖这面墙时的尘土（-1 = 不出尘土）。</summary>
    public virtual int DustType => 0;

    /// <summary>挖这面墙时的声音（-1 = 用游戏的）。</summary>
    public virtual int HitSound => -1;

    /// <summary>挖掉时掉的物品 ID（0 = 用放置这面墙的 ModItem）。</summary>
    public virtual int ItemDrop => 0;

    /// <summary>改回原版墙的 ID（0 = 不改；Pylon 尚未实现，见 docs/mods/09-blocos.md）。</summary>
    public virtual int VanillaFallbackOnModDeletion => 0;

    /// <summary>已登记的 <see cref="AddMapEntry"/>，按调用顺序；游戏侧读取用。</summary>
    public IReadOnlyList<MapEntry> MapEntries => _mapEntries;

    private readonly List<MapEntry> _mapEntries = [];

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>所有内容安装完成之后一次（跨 Mod 引用放这里）。</summary>
    public virtual void PostSetupContent() { }

    /// <summary>登记一条地图项：颜色 + 名字（null = 用 <see cref="CreateMapEntryName"/>）；键拼法与注册在管线里做。</summary>
    public virtual void AddMapEntry(Color color, LocalizedText? name = null)
    {
        _mapEntries.Add(new MapEntry { Color = color, Name = name ?? CreateMapEntryName() });
    }

    /// <summary>地图上这面墙的名字（默认 MapObject.&lt;类名&gt; 的本地化键）。</summary>
    public virtual LocalizedText CreateMapEntryName() => Language.GetText($"MapObject.{Name}");

    /// <summary>砸到或挖掉这面墙；fail 是可写引用（true = 只出尘土）。</summary>
    public virtual void KillWall(int i, int j, ref bool fail) { }

    /// <summary>出几粒尘土（最多被游戏压到 10）。</summary>
    public virtual void NumDust(int i, int j, bool fail, ref int num) { }

    /// <summary>每一粒尘土；返回 false = 这粒不出，type 可换尘土类型。</summary>
    public virtual bool CreateDust(int i, int j, ref int type) => true;

    /// <summary>挖这面墙的声音；返回 false = 不播。</summary>
    public virtual bool KillSound(int i, int j, bool fail) => true;

    /// <summary>掉落的物品；返回 false = 什么都不掉，type 可换物品。</summary>
    public virtual bool Drop(int i, int j, ref int type) => true;

    /// <summary>这面墙发的光（0..1）；只对屏幕附近的墙、每 3 帧算一次。</summary>
    public virtual void ModifyLight(int i, int j, ref float r, ref float g, ref float b) { }

    /// <summary>每帧一次，整个类型共用；frame 选帧表的那条 180 px。</summary>
    public virtual void AnimateWall(ref int frame, ref int frameCounter) { }

    /// <summary>世界的随机更新（和方块的同一处）。</summary>
    public virtual void RandomUpdate(int i, int j) { }

    /// <summary>边框；返回 false = 保持原来的边框，style/frameNumber 可换边框帧。</summary>
    public virtual bool WallFrame(int i, int j, bool randomizeFrame, ref int style, ref int frameNumber) => true;
}
