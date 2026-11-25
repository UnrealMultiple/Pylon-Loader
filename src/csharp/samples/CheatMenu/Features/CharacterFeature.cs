using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 「角色处理」静态快捷卡。1:1 对应原版 <c>MenuPages.cpp</c> 里手写的 quick-actions 卡：
/// 传送到出生点 / 回满生命与魔力。
/// </summary>
public sealed class CharacterFeature : CheatFeature
{
    public override string Id => "player.character";
    public override string Title => "角色处理";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "主要";

    public override void DrawMenu()
    {
        ModernUI.BeginCard("角色处理", "QUICK ACTIONS");

        if (ModernUI.GhostButton("传送到出生点", default, ModernUI.Icon.Star, rightAlign: true))
        {
            var player = Game.LocalPlayer;
            if (player is not null)
            {
                // 原版包装把格子坐标 ×16 后调 Player.Teleport。
                player.Teleport(new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f), 0, 0);
            }
        }

        if (ModernUI.GhostButton("回满生命与魔力", default, ModernUI.Icon.Seal, rightAlign: true))
        {
            var player = Game.LocalPlayer;
            if (player is not null)
            {
                player.statLife = player.statLifeMax2;
                player.statMana = player.statManaMax2;
            }
        }

        ModernUI.EndCard();
    }
}