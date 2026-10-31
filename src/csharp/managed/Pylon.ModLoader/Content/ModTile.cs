using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>自定义方块/家具（一类型一实例）；多格家具的形状在 SetStaticDefaults 里用 TileObjectData。</summary>
public abstract class ModTile : ModType
{
    /// <summary>一条地图项：颜色 + 名字（名字是本地化键）。</summary>
    public struct MapEntry
    {
        public Color Color;
        public LocalizedText Name;
    }

    /// <summary>一条掉落：样式（-1 = 所有样式）→ 物品 ID。</summary>
    public struct ItemDropEntry
    {
        public int Style;
        public int ItemType;
    }

    /// <summary>贴图相对 Mod 目录的路径（tile 是帧表，非单图）。</summary>
    public override string TexturePath => $"assets/tiles/{Name}.png";

    /// <summary>砸/挖这块时的尘土（-1 = 不出尘土）。</summary>
    public virtual int DustType => 0;

    /// <summary>砸这块时的声音（-1 = 用游戏的）。</summary>
    public virtual int HitSound => -1;

    /// <summary>低于这个镐力的镐子挖不动。</summary>
    public virtual int MinPick => 0;

    /// <summary>抗性：每次敲的伤害除以它。</summary>
    public virtual int MineResist => 1;

    /// <summary>挖掉时掉的物品 ID（0 = 用放置这块的 ModItem）。</summary>
    public virtual int ItemDrop => 0;

    /// <summary>也算作这些制作站（TileID.WorkBenches 之类）。</summary>
    public virtual int[] AdjTiles => [];

    /// <summary>贴图里一帧动画的高度（0 = 不按竖直帧表动画）。</summary>
    public virtual int AnimationFrameHeight => 0;

    /// <summary>false：SetDrawPositions/AnimateIndividualTile/SetSpriteEffects 每次绘制都跑，不走缓存。</summary>
    public virtual bool CacheDrawData => true;

    /// <summary>改回原版方块的 ID（0 = 不改；Pylon 尚未实现，见 docs/mods/09-blocos.md）。</summary>
    public virtual int VanillaFallbackOnModDeletion => 0;

    /// <summary>描边贴图的路径（TileID.Sets.HasOutlines）；文件不在就没有描边。</summary>
    public virtual string HighlightTexture => Texture + "_Highlight";

    /// <summary>已登记的 <see cref="AddMapEntry"/>，按调用顺序；游戏侧读取用。</summary>
    public IReadOnlyList<MapEntry> MapEntries => _mapEntries;

    /// <summary>已登记的 <see cref="RegisterItemDrop"/>，按调用顺序；掉落查询用。</summary>
    public IReadOnlyList<ItemDropEntry> ItemDrops => _itemDrops;

    private readonly List<MapEntry> _mapEntries = [];
    private readonly List<ItemDropEntry> _itemDrops = [];

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>SetStaticDefaults 之后紧接着（实心方块的 tileNoSunLight 已经写好）。</summary>
    public virtual void PostSetDefaults() { }

    /// <summary>所有内容安装完成之后一次（跨 Mod 引用放这里）。</summary>
    public virtual void PostSetupContent() { }

    /// <summary>登记一条地图项：颜色 + 名字（null = 用 <see cref="CreateMapEntryName"/>）；键拼法与注册在管线里做。</summary>
    public virtual void AddMapEntry(Color color, LocalizedText? name = null)
    {
        _mapEntries.Add(new MapEntry { Color = color, Name = name ?? CreateMapEntryName() });
    }

    /// <summary>地图上这块的名字（默认 MapObject.&lt;类名&gt; 的本地化键）。</summary>
    public virtual LocalizedText CreateMapEntryName() => Language.GetText($"MapObject.{Name}");

    /// <summary>登记掉落：不传样式 = 所有样式，传了就只给这些样式。</summary>
    public virtual void RegisterItemDrop(int itemType, params int[] styles)
    {
        if (styles == null || styles.Length == 0)
        {
            _itemDrops.Add(new ItemDropEntry { Style = -1, ItemType = itemType });
            return;
        }

        foreach (var style in styles) _itemDrops.Add(new ItemDropEntry { Style = style, ItemType = itemType });
    }

    /// <summary>这格用第几个地图选项（<see cref="AddMapEntry"/> 的次序）。</summary>
    public virtual int GetMapOption(int i, int j) => 0;

    /// <summary>镐子能不能挖这块；返回 false = 挖不动。</summary>
    public virtual bool CanKillTile(int i, int j, ref bool blockDamaged) => true;

    /// <summary>这块要没了之前；三个都是可写引用（fail = 只是敲了一下）。</summary>
    public virtual void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem) { }

    /// <summary>一件多格家具整个没了；(i, j) 是左上角，frameX/frameY 是它的帧。</summary>
    public virtual void KillMultiTile(int i, int j, int frameX, int frameY) { }

    /// <summary>出几粒尘土（最多被游戏压到 10）。</summary>
    public virtual void NumDust(int i, int j, bool fail, ref int num) { }

    /// <summary>每一粒尘土；返回 false = 这粒不出，type 可换尘土类型。</summary>
    public virtual bool CreateDust(int i, int j, ref int type) => true;

    /// <summary>挖掉的声音；返回 false = 不播。</summary>
    public virtual bool KillSound(int i, int j, bool fail) => true;

    /// <summary>这块掉不掉东西。</summary>
    public virtual bool CanDrop(int i, int j) => true;

    /// <summary>掉哪些物品（类型）；返 null = 用默认的那个。</summary>
    public virtual int[] GetItemDrops(int i, int j) => null!;

    /// <summary>玩家刚把它放下去（item 是手上的那个）。</summary>
    public virtual void PlaceInWorld(int i, int j, Item item) { }

    /// <summary>这块发的光（0..1）；要 Main.tileLighted[Type]，每 3 帧算一次。</summary>
    public virtual void ModifyLight(int i, int j, ref float r, ref float g, ref float b) { }

    /// <summary>每帧一次，整个类型共用（Main.tileFrame）。</summary>
    public virtual void AnimateTile(ref int frame, ref int frameCounter) { }

    /// <summary>某一格的帧（两个偏移加在 tile 表坐标上）。</summary>
    public virtual void AnimateIndividualTile(int type, int i, int j, ref int frameXOffset, ref int frameYOffset) { }

    /// <summary>这一格画在哪、画哪块（宽/竖偏移/高/帧）。</summary>
    public virtual void SetDrawPositions(int i, int j, ref int width, ref int offsetY, ref int height,
                                         ref int tileFrameX, ref int tileFrameY) { }

    /// <summary>镜像（1 = 水平翻转）。</summary>
    public virtual void SetSpriteEffects(int i, int j, ref SpriteEffects spriteEffects) { }

    /// <summary>你自己画；返回 false = 连默认画法都不画。</summary>
    public virtual bool PreDraw(int i, int j, SpriteBatch spriteBatch) => true;

    /// <summary>默认画法之上再画（火焰之类）。</summary>
    public virtual void PostDraw(int i, int j, SpriteBatch spriteBatch) { }

    /// <summary>顺手要一次 SpecialDraw（Main.instance.TilesRenderer.AddSpecialLegacyPoint）。</summary>
    public virtual void DrawEffects(int i, int j, SpriteBatch spriteBatch, TileDrawData drawData) { }

    /// <summary>上面要来的那次绘制（飘着的遗物之类）。</summary>
    public virtual void SpecialDraw(int i, int j, SpriteBatch spriteBatch) { }

    /// <summary>这格的尘土和粒子（只在屏幕上时）。</summary>
    public virtual void EmitParticles(int i, int j, Tile tile, int tileFrameX, int tileFrameY, Color tileLight, bool visible) { }

    /// <summary>这块在玩家附近（closer = 在屏幕上）。</summary>
    public virtual void NearbyEffects(int i, int j, bool closer) { }

    /// <summary>玩家点了这块；返回 true = 已经处理。</summary>
    public virtual bool RightClick(int i, int j) => false;

    /// <summary>光标/手指在这块上面。</summary>
    public virtual void MouseOver(int i, int j) { }

    /// <summary>光标/手指离得远时也在这块上面。</summary>
    public virtual void MouseOverFar(int i, int j) { }

    /// <summary>是不是智能交互的目标；默认跟着 <see cref="RightClick"/> 有没有被覆写。</summary>
    public virtual bool HasSmartInteract(int i, int j, object settings) =>
        Plan != null && Plan.Has(nameof(RightClick));

    /// <summary>有电的线穿过这块。</summary>
    public virtual void HitWire(int i, int j) { }

    /// <summary>树苗长大；默认就是让游戏长（WorldGen.GrowTree）。</summary>
    public virtual bool GrowSapling(int i, int j, bool underground, int treeHeightAddon, bool ignoreWalls)
    {
        var grew = WorldGen.GrowTree(i, j, treeHeightAddon, ignoreWalls);
        if (grew && WorldGen.PlayerLOS(i, j)) WorldGen.TreeGrowFXCheck(i, j);
        return grew;
    }

    /// <summary>锤子敲这块；返回 false = 保持原样。</summary>
    public virtual bool Slope(int i, int j) => true;

    /// <summary>世界的随机更新（草药在这里长）。</summary>
    public virtual void RandomUpdate(int i, int j) { }

    /// <summary>重算这格的边框；返回 false = 游戏不重算（你自己算了）。</summary>
    public virtual bool TileFrame(int i, int j, ref bool resetFrame, ref bool noBreak) => true;

    /// <summary>坐在哪儿、朝哪边（TileID.Sets.CanBeSatOnForPlayers）。</summary>
    public virtual void ModifySittingTargetInfo(int i, int j, TileRestingInfo info) { }

    /// <summary>躺在哪儿（TileID.Sets.CanBeSleptIn）。</summary>
    public virtual void ModifySleepingTargetInfo(int i, int j, TileRestingInfo info) { }

    /// <summary>这个箱子锁着没。</summary>
    public virtual bool IsLockedChest(int i, int j) => false;

    /// <summary>开锁；手动开（manual）就不走游戏搬帧和尘土那套。</summary>
    public virtual bool UnlockChest(int i, int j, ref int frameXAdjustment, ref int dustType, ref bool manual) => false;

    /// <summary>上锁；同上。</summary>
    public virtual bool LockChest(int i, int j, ref int frameXAdjustment, ref bool manual) => false;

    /// <summary>箱子界面的标题（默认和地图名一样）。</summary>
    public virtual LocalizedText DefaultContainerName(int frameX, int frameY) => CreateMapEntryName();
}
