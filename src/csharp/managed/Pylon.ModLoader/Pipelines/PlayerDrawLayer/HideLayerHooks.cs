using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>
/// 原版玩家绘制链的逐层入口：每一层调用前后各放一次作者层的槽位（见 <see cref="PlayerDrawLayerLoader"/>），
/// 作者说要藏这一层就不调 orig。原版的调用顺序与条件判断一个字节不改。
/// </summary>
internal static class HideLayerHooks
{
    internal static void Install()
    {
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_BackHair += Layer_01_BackHair;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_02_MountBehindPlayer += Layer_02_MountBehindPlayer;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_03_Carpet += Layer_03_Carpet;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_03_PortableStool += Layer_03_PortableStool;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_04_ElectrifiedDebuffBack += Layer_04_ElectrifiedDebuffBack;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_05_ForbiddenSetRing += Layer_05_ForbiddenSetRing;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_3_BackHead += Layer_01_3_BackHead;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_01_2_JimsCloak += Layer_01_2_JimsCloak;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_05_2_SafemanSun += Layer_05_2_SafemanSun;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_06_WebbedDebuffBack += Layer_06_WebbedDebuffBack;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_07_LeinforsHairShampoo += Layer_07_LeinforsHairShampoo;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_08_Backpacks += Layer_08_Backpacks;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_08_1_Tails += Layer_08_1_Tails;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_10_BackAcc += Layer_10_BackAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_09_Wings += Layer_09_Wings;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_1_BalloonFronts += Layer_12_1_BalloonFronts;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_11_Balloons += Layer_11_Balloons;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_Skin += Layer_12_Skin;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_Skin_Composite += Layer_12_Skin_Composite;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_12_SkinComposite_BackArmShirt += Layer_12_SkinComposite_BackArmShirt;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_13_ArmorBackCoat += Layer_13_ArmorBackCoat;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_13_Leggings += Layer_13_Leggings;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_14_Shoes += Layer_14_Shoes;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_15_SkinLongCoat += Layer_15_SkinLongCoat;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_16_ArmorLongCoat += Layer_16_ArmorLongCoat;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_17_Torso += Layer_17_Torso;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_17_TorsoComposite += Layer_17_TorsoComposite;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_18_OffhandAcc += Layer_18_OffhandAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_19_WaistAcc += Layer_19_WaistAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_20_NeckAcc += Layer_20_NeckAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head += Layer_21_Head;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head_TheFace += Layer_21_Head_TheFace;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_Head_TheFace_Eyelid += Layer_21_Head_TheFace_Eyelid;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_21_1_Magiluminescence += Layer_21_1_Magiluminescence;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_22_FaceAcc += Layer_22_FaceAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_23_MountFront += Layer_23_MountFront;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_24_Pulley += Layer_24_Pulley;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_25_Shield += Layer_25_Shield;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_26_SolarShield += Layer_26_SolarShield;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_27_HeldItem += Layer_27_HeldItem;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_28_ArmOverItem += Layer_28_ArmOverItem;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_28_ArmOverItemComposite += Layer_28_ArmOverItemComposite;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_29_OnhandAcc += Layer_29_OnhandAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_30_BladedGlove += Layer_30_BladedGlove;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_31_ProjectileOverArm += Layer_31_ProjectileOverArm;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc += Layer_32_FrontAcc;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc_FrontPart += Layer_32_FrontAcc_FrontPart;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_32_FrontAcc_BackPart += Layer_32_FrontAcc_BackPart;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_33_FrozenOrWebbedDebuff += Layer_33_FrozenOrWebbedDebuff;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_34_ElectrifiedDebuffFront += Layer_34_ElectrifiedDebuffFront;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_35_IceBarrier += Layer_35_IceBarrier;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_36_CTG += Layer_36_CTG;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_37_BeetleBuff += Layer_37_BeetleBuff;
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_38_EyebrellaCloud += Layer_38_EyebrellaCloud;

        PlayerDrawLayerLoader.Rebuild();
    }

    private static void Layer_01_BackHair(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_01_BackHair orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 0);
        if (!PlayerDrawLayerLoader.IsHidden(0)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 0);
    }

    private static void Layer_02_MountBehindPlayer(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_02_MountBehindPlayer orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 1);
        if (!PlayerDrawLayerLoader.IsHidden(1)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 1);
    }

    private static void Layer_03_Carpet(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_03_Carpet orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 2);
        if (!PlayerDrawLayerLoader.IsHidden(2)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 2);
    }

    private static void Layer_03_PortableStool(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_03_PortableStool orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 3);
        if (!PlayerDrawLayerLoader.IsHidden(3)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 3);
    }

    private static void Layer_04_ElectrifiedDebuffBack(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_04_ElectrifiedDebuffBack orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 4);
        if (!PlayerDrawLayerLoader.IsHidden(4)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 4);
    }

    private static void Layer_05_ForbiddenSetRing(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_05_ForbiddenSetRing orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 5);
        if (!PlayerDrawLayerLoader.IsHidden(5)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 5);
    }

    private static void Layer_01_3_BackHead(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_01_3_BackHead orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 6);
        if (!PlayerDrawLayerLoader.IsHidden(6)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 6);
    }

    private static void Layer_01_2_JimsCloak(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_01_2_JimsCloak orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 7);
        if (!PlayerDrawLayerLoader.IsHidden(7)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 7);
    }

    private static void Layer_05_2_SafemanSun(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_05_2_SafemanSun orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 8);
        if (!PlayerDrawLayerLoader.IsHidden(8)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 8);
    }

    private static void Layer_06_WebbedDebuffBack(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_06_WebbedDebuffBack orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 9);
        if (!PlayerDrawLayerLoader.IsHidden(9)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 9);
    }

    private static void Layer_07_LeinforsHairShampoo(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_07_LeinforsHairShampoo orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 10);
        if (!PlayerDrawLayerLoader.IsHidden(10)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 10);
    }

    private static void Layer_08_Backpacks(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_08_Backpacks orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 11);
        if (!PlayerDrawLayerLoader.IsHidden(11)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 11);
    }

    private static void Layer_08_1_Tails(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_08_1_Tails orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 12);
        if (!PlayerDrawLayerLoader.IsHidden(12)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 12);
    }

    private static void Layer_10_BackAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_10_BackAcc orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 13);
        if (!PlayerDrawLayerLoader.IsHidden(13)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 13);
    }

    private static void Layer_09_Wings(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_09_Wings orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 14);
        if (!PlayerDrawLayerLoader.IsHidden(14)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 14);
    }

    private static void Layer_12_1_BalloonFronts(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_1_BalloonFronts orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 15);
        if (!PlayerDrawLayerLoader.IsHidden(15)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 15);
    }

    private static void Layer_11_Balloons(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_11_Balloons orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 16);
        if (!PlayerDrawLayerLoader.IsHidden(16)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 16);
    }

    private static void Layer_12_Skin(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_Skin orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 17);
        if (!PlayerDrawLayerLoader.IsHidden(17)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 17);
    }

    private static void Layer_12_Skin_Composite(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_Skin_Composite orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 18);
        if (!PlayerDrawLayerLoader.IsHidden(18)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 18);
    }

    private static void Layer_12_SkinComposite_BackArmShirt(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_12_SkinComposite_BackArmShirt orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 19);
        if (!PlayerDrawLayerLoader.IsHidden(19)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 19);
    }

    private static void Layer_13_ArmorBackCoat(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_13_ArmorBackCoat orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 20);
        if (!PlayerDrawLayerLoader.IsHidden(20)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 20);
    }

    private static void Layer_13_Leggings(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_13_Leggings orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 21);
        if (!PlayerDrawLayerLoader.IsHidden(21)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 21);
    }

    private static void Layer_14_Shoes(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_14_Shoes orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 22);
        if (!PlayerDrawLayerLoader.IsHidden(22)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 22);
    }

    private static void Layer_15_SkinLongCoat(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_15_SkinLongCoat orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 23);
        if (!PlayerDrawLayerLoader.IsHidden(23)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 23);
    }

    private static void Layer_16_ArmorLongCoat(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_16_ArmorLongCoat orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 24);
        if (!PlayerDrawLayerLoader.IsHidden(24)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 24);
    }

    private static void Layer_17_Torso(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_17_Torso orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 25);
        if (!PlayerDrawLayerLoader.IsHidden(25)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 25);
    }

    private static void Layer_17_TorsoComposite(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_17_TorsoComposite orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 26);
        if (!PlayerDrawLayerLoader.IsHidden(26)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 26);
    }

    private static void Layer_18_OffhandAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_18_OffhandAcc orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 27);
        if (!PlayerDrawLayerLoader.IsHidden(27)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 27);
    }

    private static void Layer_19_WaistAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_19_WaistAcc orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 28);
        if (!PlayerDrawLayerLoader.IsHidden(28)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 28);
    }

    private static void Layer_20_NeckAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_20_NeckAcc orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 29);
        if (!PlayerDrawLayerLoader.IsHidden(29)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 29);
    }

    private static void Layer_21_Head(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_Head orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 30);
        if (!PlayerDrawLayerLoader.IsHidden(30)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 30);
    }

    private static void Layer_21_Head_TheFace(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_Head_TheFace orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 31);
        if (!PlayerDrawLayerLoader.IsHidden(31)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 31);
    }

    private static void Layer_21_Head_TheFace_Eyelid(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_Head_TheFace_Eyelid orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 32);
        if (!PlayerDrawLayerLoader.IsHidden(32)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 32);
    }

    private static void Layer_21_1_Magiluminescence(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_21_1_Magiluminescence orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 33);
        if (!PlayerDrawLayerLoader.IsHidden(33)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 33);
    }

    private static void Layer_22_FaceAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_22_FaceAcc orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 34);
        if (!PlayerDrawLayerLoader.IsHidden(34)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 34);
    }

    private static void Layer_23_MountFront(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_23_MountFront orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 35);
        if (!PlayerDrawLayerLoader.IsHidden(35)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 35);
    }

    private static void Layer_24_Pulley(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_24_Pulley orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 36);
        if (!PlayerDrawLayerLoader.IsHidden(36)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 36);
    }

    private static void Layer_25_Shield(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_25_Shield orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 37);
        if (!PlayerDrawLayerLoader.IsHidden(37)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 37);
    }

    private static void Layer_26_SolarShield(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_26_SolarShield orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 38);
        if (!PlayerDrawLayerLoader.IsHidden(38)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 38);
    }

    private static void Layer_27_HeldItem(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_27_HeldItem orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 39);
        if (!PlayerDrawLayerLoader.IsHidden(39)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 39);
    }

    private static void Layer_28_ArmOverItem(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_28_ArmOverItem orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 40);
        if (!PlayerDrawLayerLoader.IsHidden(40)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 40);
    }

    private static void Layer_28_ArmOverItemComposite(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_28_ArmOverItemComposite orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 41);
        if (!PlayerDrawLayerLoader.IsHidden(41)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 41);
    }

    private static void Layer_29_OnhandAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_29_OnhandAcc orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 42);
        if (!PlayerDrawLayerLoader.IsHidden(42)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 42);
    }

    private static void Layer_30_BladedGlove(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_30_BladedGlove orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 43);
        if (!PlayerDrawLayerLoader.IsHidden(43)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 43);
    }

    private static void Layer_31_ProjectileOverArm(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_31_ProjectileOverArm orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 44);
        if (!PlayerDrawLayerLoader.IsHidden(44)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 44);
    }

    private static void Layer_32_FrontAcc(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_32_FrontAcc orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 45);
        if (!PlayerDrawLayerLoader.IsHidden(45)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 45);
    }

    private static void Layer_32_FrontAcc_FrontPart(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_32_FrontAcc_FrontPart orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 46);
        if (!PlayerDrawLayerLoader.IsHidden(46)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 46);
    }

    private static void Layer_32_FrontAcc_BackPart(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_32_FrontAcc_BackPart orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 47);
        if (!PlayerDrawLayerLoader.IsHidden(47)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 47);
    }

    private static void Layer_33_FrozenOrWebbedDebuff(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_33_FrozenOrWebbedDebuff orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 48);
        if (!PlayerDrawLayerLoader.IsHidden(48)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 48);
    }

    private static void Layer_34_ElectrifiedDebuffFront(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_34_ElectrifiedDebuffFront orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 49);
        if (!PlayerDrawLayerLoader.IsHidden(49)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 49);
    }

    private static void Layer_35_IceBarrier(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_35_IceBarrier orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 50);
        if (!PlayerDrawLayerLoader.IsHidden(50)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 50);
    }

    private static void Layer_36_CTG(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_36_CTG orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 51);
        if (!PlayerDrawLayerLoader.IsHidden(51)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 51);
    }

    private static void Layer_37_BeetleBuff(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_37_BeetleBuff orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 52);
        if (!PlayerDrawLayerLoader.IsHidden(52)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 52);
    }

    private static void Layer_38_EyebrellaCloud(On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_38_EyebrellaCloud orig, PlayerDrawSet drawInfo)
    {
        PlayerDrawLayerLoader.Before(drawInfo, 53);
        if (!PlayerDrawLayerLoader.IsHidden(53)) orig(drawInfo);
        PlayerDrawLayerLoader.After(drawInfo, 53);
    }
}
