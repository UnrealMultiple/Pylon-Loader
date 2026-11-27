using System;
using System.Numerics;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 图格检查器。1:1 对应原版 <c>InspectorTileTool</c>（检查器 · 图格）：
/// 输入图格坐标，显示 active/类型/帧/墙/液体/斜坡/半砖/碰撞类型，支持传送。
/// </summary>
public sealed class InspectorTileFeature : CheatFeature
{
    private int _tileX;
    private int _tileY;

    public override string Id => "inspector.tile";
    public override string Title => "图格检查器";
    public override CheatCategory Category => CheatCategory.Inspector;
    public override string SubPage => "图格";

    public override void DrawMenu()
    {
        var maxX = Main.maxTilesX;
        var maxY = Main.maxTilesY;
        if (maxX <= 0 || maxY <= 0)
        {
            ModernUI.BeginCard("图格检查器", "TILE");
            ModernUI.DimText("世界未加载");
            ModernUI.EndCard();
            return;
        }

        ModernUI.BeginCard("图格检查器", "TILE");

        var xText = _tileX.ToString();
        ModernUI.BeginRow();
        ModernUI.RowLabel("图格X");
        if (ModernUI.TextInput("##tilex", ref xText) && int.TryParse(xText, out var px))
            _tileX = px;
        ModernUI.EndRow();
        var yText = _tileY.ToString();
        ModernUI.BeginRow();
        ModernUI.RowLabel("图格Y");
        if (ModernUI.TextInput("##tiley", ref yText) && int.TryParse(yText, out var py))
            _tileY = py;
        ModernUI.EndRow();
        _tileX = Math.Clamp(_tileX, 0, maxX - 1);
        _tileY = Math.Clamp(_tileY, 0, maxY - 1);

        var index = Game.TileIndex(_tileX, _tileY);
        var header = index >= 0 ? Terraria.TileData.GetSHeader(index) : (short)0;
        var type = Game.TileType(_tileX, _tileY);
        var halfBrick = ((header >> 10) & 1) != 0;
        var slope = (header >> 12) & 0x7;
        // 原版 TileData::GetCollisionType：0 空 / 1 实体 / 2 半砖 / 3..7 斜坡
        int collisionType;
        if (!Game.TileActive(_tileX, _tileY)) collisionType = 0;
        else if (halfBrick) collisionType = 2;
        else if (slope > 0) collisionType = 2 + slope;
        else
        {
            var solid = Main.tileSolid;
            var solidTop = Main.tileSolidTop;
            collisionType = type >= 0 && solid is not null && type < solid.Length &&
                            solid[type] && (solidTop is null || type >= solidTop.Length || !solidTop[type])
                ? 1 : 0;
        }

        ModernUI.BeginRow();
        ModernUI.RowLabel("图格", $"({_tileX}, {_tileY})");
        ModernUI.EndRow();
        InfoRow("Active", Game.TileActive(_tileX, _tileY) ? "是" : "否");
        InfoRow("类型", type.ToString());
        InfoRow("FrameX/Y", $"{Terraria.TileData.GetFrameX(index)}, {Terraria.TileData.GetFrameY(index)}");
        InfoRow("墙", (index >= 0 ? Terraria.TileData.GetWall(index) : 0).ToString());
        InfoRow("液体", (index >= 0 ? Terraria.TileData.GetLiquid(index) : 0).ToString());
        InfoRow("斜坡", slope.ToString());
        InfoRow("半砖", halfBrick ? "是" : "否");
        InfoRow("碰撞类型", collisionType.ToString());

        if (ModernUI.GhostButton("传送到此图格", default, ModernUI.Icon.None, rightAlign: true))
            Game.LocalPlayer?.Teleport(new XnaVector2(_tileX, _tileY), 0, 0);

        ModernUI.EndCard();
    }

    private static void InfoRow(string label, string value)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label, value);
        ModernUI.EndRow();
    }
}
