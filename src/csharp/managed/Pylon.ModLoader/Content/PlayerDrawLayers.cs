using Terraria.DataStructures;
using static Pylon.ModLoader.PlayerDrawLayer;

namespace Pylon.ModLoader;

/// <summary>
/// 原版玩家绘制链上的层（按游戏自己的调用顺序），当锚点用：
/// 作者的层按 <see cref="PlayerDrawLayer.GetDefaultPosition"/> 插在它们之间，原版层本身仍由游戏画。
/// 变换/头部层/可见性条件照游戏的分组来，作者拿它们当锚点时行为与直接改原版链一致。
/// </summary>
public static class PlayerDrawLayers
{
    private static void TorsoPlus(ref PlayerDrawSet info) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_extra_TorsoPlus(info);

    private static void TorsoMinus(ref PlayerDrawSet info) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_extra_TorsoMinus(info);

    private static void MountPlus(ref PlayerDrawSet info) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_extra_MountPlus(info);

    private static void MountMinus(ref PlayerDrawSet info) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_extra_MountMinus(info);

    private static bool Robed(PlayerDrawSet info) => info.drawPlayer.wearsRobe && info.drawPlayer.body != 166;

    /// <summary>躯干组变换：衣服/手/配饰这一批层都套它。</summary>
    public static readonly Transformation TorsoGroup = new VanillaPlayerDrawTransform(TorsoPlus, TorsoMinus);

    /// <summary>坐骑组：在躯干组之外再套一层。</summary>
    public static readonly Transformation MountGroup = new VanillaPlayerDrawTransform(MountPlus, MountMinus, TorsoGroup);

        public static readonly PlayerDrawLayer BackHair = new VanillaPlayerDrawLayer(nameof(BackHair), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_BackHair(i), TorsoGroup, isHeadLayer: true);
        public static readonly PlayerDrawLayer MountBehindPlayer = new VanillaPlayerDrawLayer(nameof(MountBehindPlayer), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_02_MountBehindPlayer(i));
        public static readonly PlayerDrawLayer Carpet = new VanillaPlayerDrawLayer(nameof(Carpet), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_03_Carpet(i));
        public static readonly PlayerDrawLayer PortableStool = new VanillaPlayerDrawLayer(nameof(PortableStool), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_03_PortableStool(i));
        public static readonly PlayerDrawLayer ElectrifiedDebuffBack = new VanillaPlayerDrawLayer(nameof(ElectrifiedDebuffBack), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_04_ElectrifiedDebuffBack(i), TorsoGroup);
        public static readonly PlayerDrawLayer ForbiddenSetRing = new VanillaPlayerDrawLayer(nameof(ForbiddenSetRing), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_05_ForbiddenSetRing(i), TorsoGroup);
        public static readonly PlayerDrawLayer BackHead = new VanillaPlayerDrawLayer(nameof(BackHead), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_3_BackHead(i), TorsoGroup, isHeadLayer: true);
        public static readonly PlayerDrawLayer JimsCloak = new VanillaPlayerDrawLayer(nameof(JimsCloak), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_2_JimsCloak(i), TorsoGroup);
        public static readonly PlayerDrawLayer SafemanSun = new VanillaPlayerDrawLayer(nameof(SafemanSun), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_05_2_SafemanSun(i), TorsoGroup);
        public static readonly PlayerDrawLayer WebbedDebuffBack = new VanillaPlayerDrawLayer(nameof(WebbedDebuffBack), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_06_WebbedDebuffBack(i), TorsoGroup);
        public static readonly PlayerDrawLayer LeinforsHairShampoo = new VanillaPlayerDrawLayer(nameof(LeinforsHairShampoo), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_07_LeinforsHairShampoo(i), TorsoGroup, isHeadLayer: true);
        public static readonly PlayerDrawLayer Backpacks = new VanillaPlayerDrawLayer(nameof(Backpacks), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_08_Backpacks(i));
        public static readonly PlayerDrawLayer Tails = new VanillaPlayerDrawLayer(nameof(Tails), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_08_1_Tails(i), TorsoGroup);
        public static readonly PlayerDrawLayer BackAcc = new VanillaPlayerDrawLayer(nameof(BackAcc), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_10_BackAcc(i), TorsoGroup);
        public static readonly PlayerDrawLayer Wings = new VanillaPlayerDrawLayer(nameof(Wings), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_09_Wings(i));
        public static readonly PlayerDrawLayer BalloonFronts = new VanillaPlayerDrawLayer(nameof(BalloonFronts), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_1_BalloonFronts(i));
        public static readonly PlayerDrawLayer Balloons = new VanillaPlayerDrawLayer(nameof(Balloons), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_11_Balloons(i));
        public static readonly PlayerDrawLayer Skin = new VanillaPlayerDrawLayer(nameof(Skin), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_Skin(i));
        public static readonly PlayerDrawLayer SkinComposite = new VanillaPlayerDrawLayer(nameof(SkinComposite), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_Skin_Composite(i));
        public static readonly PlayerDrawLayer SkinCompositeBackArmShirt = new VanillaPlayerDrawLayer(nameof(SkinCompositeBackArmShirt), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_SkinComposite_BackArmShirt(i));
        public static readonly PlayerDrawLayer ArmorBackCoat = new VanillaPlayerDrawLayer(nameof(ArmorBackCoat), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_13_ArmorBackCoat(i));
        public static readonly PlayerDrawLayer Leggings = new VanillaPlayerDrawLayer(nameof(Leggings), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_13_Leggings(i), condition: i => !Robed(i));
        public static readonly PlayerDrawLayer Shoes = new VanillaPlayerDrawLayer(nameof(Shoes), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_14_Shoes(i));
        public static readonly PlayerDrawLayer SkinLongCoat = new VanillaPlayerDrawLayer(nameof(SkinLongCoat), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_15_SkinLongCoat(i), TorsoGroup);
        public static readonly PlayerDrawLayer ArmorLongCoat = new VanillaPlayerDrawLayer(nameof(ArmorLongCoat), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_16_ArmorLongCoat(i), TorsoGroup);
        public static readonly PlayerDrawLayer Torso = new VanillaPlayerDrawLayer(nameof(Torso), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_17_Torso(i), TorsoGroup);
        public static readonly PlayerDrawLayer TorsoComposite = new VanillaPlayerDrawLayer(nameof(TorsoComposite), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_17_TorsoComposite(i), TorsoGroup);
        public static readonly PlayerDrawLayer OffhandAcc = new VanillaPlayerDrawLayer(nameof(OffhandAcc), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_18_OffhandAcc(i), TorsoGroup);
        public static readonly PlayerDrawLayer WaistAcc = new VanillaPlayerDrawLayer(nameof(WaistAcc), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_19_WaistAcc(i), TorsoGroup);
        public static readonly PlayerDrawLayer NeckAcc = new VanillaPlayerDrawLayer(nameof(NeckAcc), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_20_NeckAcc(i), TorsoGroup);
        public static readonly PlayerDrawLayer Head = new VanillaPlayerDrawLayer(nameof(Head), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head(i), TorsoGroup, isHeadLayer: true, condition: i => !i.mountHandlesHeadDraw);
        public static readonly PlayerDrawLayer HeadTheFace = new VanillaPlayerDrawLayer(nameof(HeadTheFace), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head_TheFace(i));
        public static readonly PlayerDrawLayer HeadTheFaceEyelid = new VanillaPlayerDrawLayer(nameof(HeadTheFaceEyelid), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head_TheFace_Eyelid(i));
        public static readonly PlayerDrawLayer Magiluminescence = new VanillaPlayerDrawLayer(nameof(Magiluminescence), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_1_Magiluminescence(i), TorsoGroup);
        public static readonly PlayerDrawLayer FaceAcc = new VanillaPlayerDrawLayer(nameof(FaceAcc), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_22_FaceAcc(i), TorsoGroup, isHeadLayer: true, condition: i => !i.mountHandlesHeadDraw);
        public static readonly PlayerDrawLayer MountFront = new VanillaPlayerDrawLayer(nameof(MountFront), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_23_MountFront(i), TorsoGroup);
        public static readonly PlayerDrawLayer Pulley = new VanillaPlayerDrawLayer(nameof(Pulley), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_24_Pulley(i), TorsoGroup);
        public static readonly PlayerDrawLayer Shield = new VanillaPlayerDrawLayer(nameof(Shield), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_25_Shield(i), TorsoGroup);
        public static readonly PlayerDrawLayer SolarShield = new VanillaPlayerDrawLayer(nameof(SolarShield), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_26_SolarShield(i), MountGroup);
        public static readonly PlayerDrawLayer HeldItem = new VanillaPlayerDrawLayer(nameof(HeldItem), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_27_HeldItem(i));
        public static readonly PlayerDrawLayer ArmOverItem = new VanillaPlayerDrawLayer(nameof(ArmOverItem), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_28_ArmOverItem(i), TorsoGroup);
        public static readonly PlayerDrawLayer ArmOverItemComposite = new VanillaPlayerDrawLayer(nameof(ArmOverItemComposite), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_28_ArmOverItemComposite(i), TorsoGroup);
        public static readonly PlayerDrawLayer OnhandAcc = new VanillaPlayerDrawLayer(nameof(OnhandAcc), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_29_OnhandAcc(i), TorsoGroup);
        public static readonly PlayerDrawLayer BladedGlove = new VanillaPlayerDrawLayer(nameof(BladedGlove), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_30_BladedGlove(i), TorsoGroup);
        public static readonly PlayerDrawLayer ProjectileOverArm = new VanillaPlayerDrawLayer(nameof(ProjectileOverArm), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_31_ProjectileOverArm(i));
        public static readonly PlayerDrawLayer FrontAcc = new VanillaPlayerDrawLayer(nameof(FrontAcc), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc(i));
        public static readonly PlayerDrawLayer FrontAccFrontPart = new VanillaPlayerDrawLayer(nameof(FrontAccFrontPart), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc_FrontPart(i));
        public static readonly PlayerDrawLayer FrontAccBackPart = new VanillaPlayerDrawLayer(nameof(FrontAccBackPart), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc_BackPart(i));
        public static readonly PlayerDrawLayer FrozenOrWebbedDebuff = new VanillaPlayerDrawLayer(nameof(FrozenOrWebbedDebuff), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_33_FrozenOrWebbedDebuff(i));
        public static readonly PlayerDrawLayer ElectrifiedDebuffFront = new VanillaPlayerDrawLayer(nameof(ElectrifiedDebuffFront), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_34_ElectrifiedDebuffFront(i));
        public static readonly PlayerDrawLayer IceBarrier = new VanillaPlayerDrawLayer(nameof(IceBarrier), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_35_IceBarrier(i));
        public static readonly PlayerDrawLayer CTG = new VanillaPlayerDrawLayer(nameof(CTG), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_36_CTG(i));
        public static readonly PlayerDrawLayer BeetleBuff = new VanillaPlayerDrawLayer(nameof(BeetleBuff), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_37_BeetleBuff(i));
        public static readonly PlayerDrawLayer EyebrellaCloud = new VanillaPlayerDrawLayer(nameof(EyebrellaCloud), static (ref PlayerDrawSet i) => Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_38_EyebrellaCloud(i));

    /// <summary>全部原版层，顺序就是游戏调用顺序。</summary>
    public static readonly IReadOnlyList<PlayerDrawLayer> VanillaLayers =
    [
        BackHair,
        MountBehindPlayer,
        Carpet,
        PortableStool,
        ElectrifiedDebuffBack,
        ForbiddenSetRing,
        BackHead,
        JimsCloak,
        SafemanSun,
        WebbedDebuffBack,
        LeinforsHairShampoo,
        Backpacks,
        Tails,
        BackAcc,
        Wings,
        BalloonFronts,
        Balloons,
        Skin,
        SkinComposite,
        SkinCompositeBackArmShirt,
        ArmorBackCoat,
        Leggings,
        Shoes,
        SkinLongCoat,
        ArmorLongCoat,
        Torso,
        TorsoComposite,
        OffhandAcc,
        WaistAcc,
        NeckAcc,
        Head,
        HeadTheFace,
        HeadTheFaceEyelid,
        Magiluminescence,
        FaceAcc,
        MountFront,
        Pulley,
        Shield,
        SolarShield,
        HeldItem,
        ArmOverItem,
        ArmOverItemComposite,
        OnhandAcc,
        BladedGlove,
        ProjectileOverArm,
        FrontAcc,
        FrontAccFrontPart,
        FrontAccBackPart,
        FrozenOrWebbedDebuff,
        ElectrifiedDebuffFront,
        IceBarrier,
        CTG,
        BeetleBuff,
        EyebrellaCloud,
    ];

    public static PlayerDrawLayer FirstVanillaLayer => VanillaLayers[0];

    public static PlayerDrawLayer LastVanillaLayer => VanillaLayers[^1];

    /// <summary>插在所有原版层之前。</summary>
    public static Between BeforeFirstVanillaLayer => new(null!, FirstVanillaLayer);

    /// <summary>插在所有原版层之后。</summary>
    public static Between AfterLastVanillaLayer => new(LastVanillaLayer, null!);
}

/// <summary>原版层的变换实现：推入/还原一对函数。</summary>
internal sealed class VanillaPlayerDrawTransform : PlayerDrawLayer.Transformation
{
    public delegate void LayerFunction(ref PlayerDrawSet info);

    private readonly LayerFunction _preDraw, _postDraw;
    private readonly Transformation _parent;

    public VanillaPlayerDrawTransform(LayerFunction preDraw, LayerFunction postDraw, Transformation? parent = null)
    {
        _preDraw = preDraw;
        _postDraw = postDraw;
        _parent = parent!;
    }

    public override Transformation Parent => _parent;

    protected override void PreDraw(ref PlayerDrawSet drawInfo) => _preDraw(ref drawInfo);

    protected override void PostDraw(ref PlayerDrawSet drawInfo) => _postDraw(ref drawInfo);
}

/// <summary>原版层的锚点对象：不参与绘制，只提供位置与语义（顺序、父子、可见性）。</summary>
internal sealed class VanillaPlayerDrawLayer : PlayerDrawLayer
{
    public delegate void DrawFunc(ref PlayerDrawSet info);

    public delegate bool Condition(PlayerDrawSet info);

    private readonly string _name;
    private readonly DrawFunc _drawFunc;
    private readonly Transformation _transform;
    private readonly bool _isHeadLayer;
    private readonly Condition? _condition;

    public VanillaPlayerDrawLayer(string name, DrawFunc drawFunc, Transformation? transform = null,
                                  bool isHeadLayer = false, Condition? condition = null)
    {
        _name = name;
        _drawFunc = drawFunc;
        _transform = transform!;
        _isHeadLayer = isHeadLayer;
        _condition = condition;
    }

    public override string Name => _name;

    public override Transformation Transform => _transform;

    public override bool IsHeadLayer => _isHeadLayer;

    public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) => _condition?.Invoke(drawInfo) ?? true;

    public override PlayerDrawLayer.Position GetDefaultPosition()
    {
        var layers = PlayerDrawLayers.VanillaLayers;
        var index = 0;
        while (index < layers.Count && !ReferenceEquals(layers[index], this)) index++;

        var before = index > 0 ? layers[index - 1] : null!;
        var after = index + 1 < layers.Count ? layers[index + 1] : null!;
        return new Between(before, after);
    }

    // 原版层由游戏自己画，锚点不参与绘制。
    protected override void Draw(ref PlayerDrawSet drawInfo) => _drawFunc(ref drawInfo);
}
