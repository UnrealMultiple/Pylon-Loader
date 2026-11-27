using System;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.ID;
using Terraria.ID;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 宝石法杖形态切换。1:1 对应原版 <c>GemStaffForm2Tool</c>（常驻后台，不出卡片）：
/// hook <c>Player.PackGemStaffFeatures</c>——开启且手持宝石法杖时置位
/// AllGems(bit7)+AllGems2(bit9)，齐射 7 种宝石弹幕；
/// hook <c>GUIQuickActionButton.Draw</c>——「智能交互」按钮（QuickActionControl.Interact）
/// 按下沿翻转形态开关。ItemID.Sets 的 HasItemSwap/ItemSwapIds 延迟初始化，每帧重试成功即止。
/// </summary>
public sealed class GemStaffForm2Feature : CheatFeature
{
    private const int InteractAction = 18;

    private static readonly int[] Staves = [6171, 739, 740, 741, 742, 743, 744, 3377];

    private static bool _swapInited;
    private static bool _form2Toggled;
    private static readonly bool[] PrevPressed = new bool[64];

    public override string Id => "misc.gemstaff2";
    public override string Title => "宝石法杖形态切换";
    public override CheatCategory Category => CheatCategory.Misc;
    /// <summary>原版不出卡片（HasUI=false），保留空串即不进子页。</summary>
    public override string SubPage => null;

    public override void Install()
    {
        On.Terraria.Player.PackGemStaffFeatures += HookPackGemStaffFeatures;
        // Draw 有两个重载，MonoMod 把委托名改编成带签名的形式（见 interop/generated 元数据）。
        On.GUIQuickActionButton.Draw_QuickActionControl_QuickActionButton_Layout_bool_floatRef_bool_bool
            += HookQuickActionDraw;
    }

    public override void Tick()
    {
        EnsureSwapInited();
    }

    // ---------------- hooks ----------------

    private static float HookPackGemStaffFeatures(On.Terraria.Player.orig_PackGemStaffFeatures orig,
        Player self, int shoot)
    {
        var result = orig(self, shoot);
        var cfg = CheatFeatureHost.Config;
        if (cfg is { GemStaffForm2Enabled: true } && _form2Toggled && IsGemStaffHeld(self))
        {
            var bits = BitConverter.SingleToUInt32Bits(result);
            bits |= 1u << 9 | 1u << 7;    // AllGems2 + AllGems
            result = BitConverter.UInt32BitsToSingle(bits);
        }
        return result;
    }

    private static void HookQuickActionDraw(
        On.GUIQuickActionButton.orig_Draw_QuickActionControl_QuickActionButton_Layout_bool_floatRef_bool_bool orig,
        QuickActionButton_Layout.QuickActionControl actionType, QuickActionButton_Layout layout,
        bool pressed, ref float scale, bool ignoreChecks, bool ignoreIcon)
    {
        // 数组延迟初始化（每帧尝试，成功即止）
        EnsureSwapInited();

        var index = (int)actionType;
        if (index >= 0 && index < PrevPressed.Length)
        {
            var wasPressed = PrevPressed[index];
            PrevPressed[index] = pressed;

            var cfg = CheatFeatureHost.Config;
            if (pressed && !wasPressed && cfg is { GemStaffForm2Enabled: true } &&
                index == InteractAction && IsGemStaffHeld(Game.LocalPlayer))
            {
                _form2Toggled = !_form2Toggled;
            }
        }
        orig(actionType, layout, pressed, ref scale, ignoreChecks, ignoreIcon);
    }

    // ---------------- helpers ----------------

    /// <summary>手持物品是否为宝石法杖（6171 或单宝石法杖 739~744 / 3377）。</summary>
    private static bool IsGemStaffHeld(Player player)
    {
        var held = player?.HeldItem;
        if (held is null) return false;
        var type = held.type;
        return type == 6171 || (type >= 739 && type <= 744) || type == 3377;
    }

    /// <summary>ItemID.Sets 的两个数组在游戏加载物品后才初始化；每帧重试，成功一次即止。</summary>
    private static void EnsureSwapInited()
    {
        if (_swapInited) return;

        try
        {
            var has = ItemID.Sets.HasItemSwap;
            var ids = ItemID.Sets.ItemSwapIds;
            if (has is null || ids is null) return;

            foreach (var id in Staves)
            {
                if (id >= has.Length || id >= ids.Length) return;
                has[id] = true;
                ids[id] = id;
            }
            _swapInited = true;
        }
        catch
        {
            // 数组未就绪：下一帧再来
        }
    }

    public override void DrawMenu() { }
}
