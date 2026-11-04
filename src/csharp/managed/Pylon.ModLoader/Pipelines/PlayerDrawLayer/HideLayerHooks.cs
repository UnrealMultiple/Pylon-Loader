using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>原版玩家绘制链上的层（按绘制顺序）。作者的 <see cref="ModPlayer.HideDrawLayer"/> 用这些名字点名不画的层。</summary>
public enum VanillaDrawLayer
{
    None = 0,
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
}

/// <summary>
/// 逐层挂原版玩家绘制链：每个层单独一个钩子，作者说"这一层别画"就不调 orig。
/// 层的顺序与游戏一致；命中的层直接跳过，不产生额外分配（所有钩子共用 <see cref="Skip"/> 判断）。
/// </summary>
internal static class HideLayerHooks
{
    internal static void Install()
    {
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_BackHair += BackHair;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_02_MountBehindPlayer += MountBehindPlayer;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_03_Carpet += Carpet;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_03_PortableStool += PortableStool;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_04_ElectrifiedDebuffBack += ElectrifiedDebuffBack;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_05_ForbiddenSetRing += ForbiddenSetRing;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_3_BackHead += BackHead;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_2_JimsCloak += JimsCloak;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_05_2_SafemanSun += SafemanSun;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_06_WebbedDebuffBack += WebbedDebuffBack;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_07_LeinforsHairShampoo += LeinforsHairShampoo;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_08_Backpacks += Backpacks;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_08_1_Tails += Tails;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_10_BackAcc += BackAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_09_Wings += Wings;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_1_BalloonFronts += BalloonFronts;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_11_Balloons += Balloons;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_Skin += Skin;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_Skin_Composite += SkinComposite;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_SkinComposite_BackArmShirt += SkinCompositeBackArmShirt;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_13_ArmorBackCoat += ArmorBackCoat;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_13_Leggings += Leggings;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_14_Shoes += Shoes;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_15_SkinLongCoat += SkinLongCoat;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_16_ArmorLongCoat += ArmorLongCoat;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_17_Torso += Torso;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_17_TorsoComposite += TorsoComposite;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_18_OffhandAcc += OffhandAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_19_WaistAcc += WaistAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_20_NeckAcc += NeckAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head += Head;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head_TheFace += HeadTheFace;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head_TheFace_Eyelid += HeadTheFaceEyelid;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_1_Magiluminescence += Magiluminescence;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_22_FaceAcc += FaceAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_23_MountFront += MountFront;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_24_Pulley += Pulley;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_25_Shield += Shield;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_26_SolarShield += SolarShield;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_27_HeldItem += HeldItem;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_28_ArmOverItem += ArmOverItem;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_28_ArmOverItemComposite += ArmOverItemComposite;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_29_OnhandAcc += OnhandAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_30_BladedGlove += BladedGlove;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_31_ProjectileOverArm += ProjectileOverArm;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc += FrontAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc_FrontPart += FrontAccFrontPart;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc_BackPart += FrontAccBackPart;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_33_FrozenOrWebbedDebuff += FrozenOrWebbedDebuff;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_34_ElectrifiedDebuffFront += ElectrifiedDebuffFront;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_35_IceBarrier += IceBarrier;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_36_CTG += CTG;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_37_BeetleBuff += BeetleBuff;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_38_EyebrellaCloud += EyebrellaCloud;
    }

    /// <summary>这一层该不该画（按玩家身上的 ModPlayer 判；没人开这个钩子时一次字典查找就返回）。</summary>
    private static bool Skip(PlayerDrawSet drawinfo, VanillaDrawLayer layer) =>
        PlayerDrawInfoPipeline.Hidden(drawinfo.drawPlayer, layer);

    private static void BackHair(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_01_BackHair orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.BackHair);
        if (!Skip(drawinfo, VanillaDrawLayer.BackHair)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.BackHair);
    }

    private static void MountBehindPlayer(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_02_MountBehindPlayer orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.MountBehindPlayer);
        if (!Skip(drawinfo, VanillaDrawLayer.MountBehindPlayer)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.MountBehindPlayer);
    }

    private static void Carpet(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_03_Carpet orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Carpet);
        if (!Skip(drawinfo, VanillaDrawLayer.Carpet)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Carpet);
    }

    private static void PortableStool(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_03_PortableStool orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.PortableStool);
        if (!Skip(drawinfo, VanillaDrawLayer.PortableStool)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.PortableStool);
    }

    private static void ElectrifiedDebuffBack(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_04_ElectrifiedDebuffBack orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ElectrifiedDebuffBack);
        if (!Skip(drawinfo, VanillaDrawLayer.ElectrifiedDebuffBack)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ElectrifiedDebuffBack);
    }

    private static void ForbiddenSetRing(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_05_ForbiddenSetRing orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ForbiddenSetRing);
        if (!Skip(drawinfo, VanillaDrawLayer.ForbiddenSetRing)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ForbiddenSetRing);
    }

    private static void BackHead(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_01_3_BackHead orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.BackHead);
        if (!Skip(drawinfo, VanillaDrawLayer.BackHead)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.BackHead);
    }

    private static void JimsCloak(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_01_2_JimsCloak orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.JimsCloak);
        if (!Skip(drawinfo, VanillaDrawLayer.JimsCloak)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.JimsCloak);
    }

    private static void SafemanSun(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_05_2_SafemanSun orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.SafemanSun);
        if (!Skip(drawinfo, VanillaDrawLayer.SafemanSun)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.SafemanSun);
    }

    private static void WebbedDebuffBack(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_06_WebbedDebuffBack orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.WebbedDebuffBack);
        if (!Skip(drawinfo, VanillaDrawLayer.WebbedDebuffBack)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.WebbedDebuffBack);
    }

    private static void LeinforsHairShampoo(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_07_LeinforsHairShampoo orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.LeinforsHairShampoo);
        if (!Skip(drawinfo, VanillaDrawLayer.LeinforsHairShampoo)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.LeinforsHairShampoo);
    }

    private static void Backpacks(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_08_Backpacks orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Backpacks);
        if (!Skip(drawinfo, VanillaDrawLayer.Backpacks)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Backpacks);
    }

    private static void Tails(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_08_1_Tails orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Tails);
        if (!Skip(drawinfo, VanillaDrawLayer.Tails)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Tails);
    }

    private static void BackAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_10_BackAcc orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.BackAcc);
        if (!Skip(drawinfo, VanillaDrawLayer.BackAcc)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.BackAcc);
    }

    private static void Wings(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_09_Wings orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Wings);
        if (!Skip(drawinfo, VanillaDrawLayer.Wings)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Wings);
    }

    private static void BalloonFronts(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_1_BalloonFronts orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.BalloonFronts);
        if (!Skip(drawinfo, VanillaDrawLayer.BalloonFronts)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.BalloonFronts);
    }

    private static void Balloons(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_11_Balloons orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Balloons);
        if (!Skip(drawinfo, VanillaDrawLayer.Balloons)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Balloons);
    }

    private static void Skin(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_Skin orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Skin);
        if (!Skip(drawinfo, VanillaDrawLayer.Skin)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Skin);
    }

    private static void SkinComposite(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_Skin_Composite orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.SkinComposite);
        if (!Skip(drawinfo, VanillaDrawLayer.SkinComposite)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.SkinComposite);
    }

    private static void SkinCompositeBackArmShirt(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_SkinComposite_BackArmShirt orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.SkinCompositeBackArmShirt);
        if (!Skip(drawinfo, VanillaDrawLayer.SkinCompositeBackArmShirt)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.SkinCompositeBackArmShirt);
    }

    private static void ArmorBackCoat(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_13_ArmorBackCoat orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ArmorBackCoat);
        if (!Skip(drawinfo, VanillaDrawLayer.ArmorBackCoat)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ArmorBackCoat);
    }

    private static void Leggings(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_13_Leggings orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Leggings);
        if (!Skip(drawinfo, VanillaDrawLayer.Leggings)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Leggings);
    }

    private static void Shoes(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_14_Shoes orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Shoes);
        if (!Skip(drawinfo, VanillaDrawLayer.Shoes)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Shoes);
    }

    private static void SkinLongCoat(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_15_SkinLongCoat orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.SkinLongCoat);
        if (!Skip(drawinfo, VanillaDrawLayer.SkinLongCoat)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.SkinLongCoat);
    }

    private static void ArmorLongCoat(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_16_ArmorLongCoat orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ArmorLongCoat);
        if (!Skip(drawinfo, VanillaDrawLayer.ArmorLongCoat)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ArmorLongCoat);
    }

    private static void Torso(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_17_Torso orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Torso);
        if (!Skip(drawinfo, VanillaDrawLayer.Torso)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Torso);
    }

    private static void TorsoComposite(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_17_TorsoComposite orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.TorsoComposite);
        if (!Skip(drawinfo, VanillaDrawLayer.TorsoComposite)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.TorsoComposite);
    }

    private static void OffhandAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_18_OffhandAcc orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.OffhandAcc);
        if (!Skip(drawinfo, VanillaDrawLayer.OffhandAcc)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.OffhandAcc);
    }

    private static void WaistAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_19_WaistAcc orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.WaistAcc);
        if (!Skip(drawinfo, VanillaDrawLayer.WaistAcc)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.WaistAcc);
    }

    private static void NeckAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_20_NeckAcc orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.NeckAcc);
        if (!Skip(drawinfo, VanillaDrawLayer.NeckAcc)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.NeckAcc);
    }

    private static void Head(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_Head orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Head);
        if (!Skip(drawinfo, VanillaDrawLayer.Head)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Head);
    }

    private static void HeadTheFace(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_Head_TheFace orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.HeadTheFace);
        if (!Skip(drawinfo, VanillaDrawLayer.HeadTheFace)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.HeadTheFace);
    }

    private static void HeadTheFaceEyelid(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_Head_TheFace_Eyelid orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.HeadTheFaceEyelid);
        if (!Skip(drawinfo, VanillaDrawLayer.HeadTheFaceEyelid)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.HeadTheFaceEyelid);
    }

    private static void Magiluminescence(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_1_Magiluminescence orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Magiluminescence);
        if (!Skip(drawinfo, VanillaDrawLayer.Magiluminescence)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Magiluminescence);
    }

    private static void FaceAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_22_FaceAcc orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.FaceAcc);
        if (!Skip(drawinfo, VanillaDrawLayer.FaceAcc)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.FaceAcc);
    }

    private static void MountFront(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_23_MountFront orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.MountFront);
        if (!Skip(drawinfo, VanillaDrawLayer.MountFront)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.MountFront);
    }

    private static void Pulley(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_24_Pulley orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Pulley);
        if (!Skip(drawinfo, VanillaDrawLayer.Pulley)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Pulley);
    }

    private static void Shield(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_25_Shield orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.Shield);
        if (!Skip(drawinfo, VanillaDrawLayer.Shield)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.Shield);
    }

    private static void SolarShield(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_26_SolarShield orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.SolarShield);
        if (!Skip(drawinfo, VanillaDrawLayer.SolarShield)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.SolarShield);
    }

    private static void HeldItem(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_27_HeldItem orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.HeldItem);
        if (!Skip(drawinfo, VanillaDrawLayer.HeldItem)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.HeldItem);
    }

    private static void ArmOverItem(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_28_ArmOverItem orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ArmOverItem);
        if (!Skip(drawinfo, VanillaDrawLayer.ArmOverItem)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ArmOverItem);
    }

    private static void ArmOverItemComposite(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_28_ArmOverItemComposite orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ArmOverItemComposite);
        if (!Skip(drawinfo, VanillaDrawLayer.ArmOverItemComposite)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ArmOverItemComposite);
    }

    private static void OnhandAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_29_OnhandAcc orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.OnhandAcc);
        if (!Skip(drawinfo, VanillaDrawLayer.OnhandAcc)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.OnhandAcc);
    }

    private static void BladedGlove(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_30_BladedGlove orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.BladedGlove);
        if (!Skip(drawinfo, VanillaDrawLayer.BladedGlove)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.BladedGlove);
    }

    private static void ProjectileOverArm(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_31_ProjectileOverArm orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ProjectileOverArm);
        if (!Skip(drawinfo, VanillaDrawLayer.ProjectileOverArm)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ProjectileOverArm);
    }

    private static void FrontAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_32_FrontAcc orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.FrontAcc);
        if (!Skip(drawinfo, VanillaDrawLayer.FrontAcc)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.FrontAcc);
    }

    private static void FrontAccFrontPart(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_32_FrontAcc_FrontPart orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.FrontAccFrontPart);
        if (!Skip(drawinfo, VanillaDrawLayer.FrontAccFrontPart)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.FrontAccFrontPart);
    }

    private static void FrontAccBackPart(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_32_FrontAcc_BackPart orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.FrontAccBackPart);
        if (!Skip(drawinfo, VanillaDrawLayer.FrontAccBackPart)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.FrontAccBackPart);
    }

    private static void FrozenOrWebbedDebuff(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_33_FrozenOrWebbedDebuff orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.FrozenOrWebbedDebuff);
        if (!Skip(drawinfo, VanillaDrawLayer.FrozenOrWebbedDebuff)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.FrozenOrWebbedDebuff);
    }

    private static void ElectrifiedDebuffFront(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_34_ElectrifiedDebuffFront orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.ElectrifiedDebuffFront);
        if (!Skip(drawinfo, VanillaDrawLayer.ElectrifiedDebuffFront)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.ElectrifiedDebuffFront);
    }

    private static void IceBarrier(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_35_IceBarrier orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.IceBarrier);
        if (!Skip(drawinfo, VanillaDrawLayer.IceBarrier)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.IceBarrier);
    }

    private static void CTG(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_36_CTG orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.CTG);
        if (!Skip(drawinfo, VanillaDrawLayer.CTG)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.CTG);
    }

    private static void BeetleBuff(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_37_BeetleBuff orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.BeetleBuff);
        if (!Skip(drawinfo, VanillaDrawLayer.BeetleBuff)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.BeetleBuff);
    }

    private static void EyebrellaCloud(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_38_EyebrellaCloud orig, PlayerDrawSet drawinfo)
    {
        PlayerDrawLayerChain.Before(drawinfo, VanillaDrawLayer.EyebrellaCloud);
        if (!Skip(drawinfo, VanillaDrawLayer.EyebrellaCloud)) orig(drawinfo);
        PlayerDrawLayerChain.After(drawinfo, VanillaDrawLayer.EyebrellaCloud);
    }
}
