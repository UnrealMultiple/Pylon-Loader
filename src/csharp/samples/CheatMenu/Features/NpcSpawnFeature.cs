using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.NaviteAPI.ImGui;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// NPC 生成浏览器。1:1 对应原版 <c>NPCSpawnTool</c>（实体 · NPC生成）：
/// 搜索框 + 图标网格（MT2 的 npcs/NPC_&lt;id&gt;.png），点击即生成。
/// 联机走 61 号包（SpawnBoss）；单机在玩家周围随机找空地生成；肉山（113）横跨屏幕。
/// </summary>
public sealed class NpcSpawnFeature : CheatFeature
{
    private const float ButtonSize = 88f;
    private const float IconSize = 72f;

    private string _search = string.Empty;
    private string _lastSearch;
    private List<(int Id, string Name)> _npcNames;
    private readonly List<(int Id, string Name)> _filtered = new();
    private bool _scrollReset = true;

    public override string Id => "entity.npcspawn";
    public override string Title => "NPC 生成器";
    public override CheatCategory Category => CheatCategory.Physical;
    public override string SubPage => "NPC生成";

    // ---------------- UI ----------------

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;

        ModernUI.BeginCard("NPC 生成器", "NPC SPAWNER");

        ModernUI.BeginRow();
        ModernUI.RowLabel("搜索");
        var search = _search ?? string.Empty;
        if (ModernUI.TextInput("##npcsearch", ref search))
            _search = search ?? string.Empty;
        ModernUI.EndRow();
        ModernUI.DimText("按 NPC 名称或 ID 搜索");

        BuildNpcList();
        UpdateFilteredList();

        ModernUI.BeginRow();
        ModernUI.RowLabel("匹配", $"{_filtered.Count} 项");
        ModernUI.EndRow();

        var count = (float)(cfg?.NpcSpawnCount ?? 1);
        ModernUI.BeginRow();
        ModernUI.RowLabel("数量");
        ModernUI.EndRow();
        if (ModernUI.RowSlider("##npccount", "", "1-200", ref count, 1f, 200f, "0"))
        {
            cfg.NpcSpawnCount = (int)MathF.Round(Math.Clamp(count, 1f, 200f));
            CheatFeatureHost.MarkDirty();
        }

        if (_npcNames is { Count: 0 })
            ModernUI.TextColored("未找到任何 NPC（列表为空）",
                new System.Numerics.Vector4(0.9f, 0.6f, 0.2f, 1f));

        // 图标网格（滚动区）
        var scrollHeight = ImGui.GetFontSize() * 13f;
        ModScrollArea.Begin("##npcspawn_scroll", new System.Numerics.Vector2(0f, scrollHeight),
            reset: _scrollReset);
        _scrollReset = false;
        IconGrid.DrawVirtualGrid(_filtered, ButtonSize, IconSize, DrawNpcIcon,
            (id, _) => SpawnMany(id));
        ModScrollArea.End();

        ModernUI.EndCard();
    }

    private void DrawNpcIcon(int id, string name)
    {
        // 图标直接画在按钮矩形上（与原版 NpcButton 相同的等比 contain 布局）
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (IconStore.TryGet(IconStore.Kind.Npc, id, out var texture, out var iw, out var ih))
            IconGrid.DrawIconContained(texture, iw, ih, min, max);
    }

    // ---------------- 列表 ----------------

    private void BuildNpcList()
    {
        if (_npcNames is not null) return;

        // 列表不依赖图标：全量扫描 1..NPCID.Count，图标只影响格子里的图画不画得出来。
        _npcNames = [];
        var maxId = NPCID.Count;
        if (maxId <= 0) maxId = 700;   // 拿不到 Count 时退回旧版本上限
        for (var id = 1; id < maxId; id++)
        {
            _npcNames.Add((id, NpcName(id)));
        }
        _npcNames.Sort((a, b) => a.Id.CompareTo(b.Id));   // 按 id 稳定排序
        _lastSearch = null;
    }

    private static string NpcName(int id)
    {
        try
        {
            var name = Lang.GetNPCNameValue(id);
            return string.IsNullOrEmpty(name) ? "NPC " + id : name;
        }
        catch
        {
            return "NPC " + id;
        }
    }

    private void UpdateFilteredList()
    {
        var lower = (_search ?? string.Empty).ToLowerInvariant();
        if (lower == _lastSearch && _filtered.Count > 0) return;
        _lastSearch = lower;
        _scrollReset = true;   // 结果集变了，列表滚回顶部

        _filtered.Clear();
        if (_npcNames is null) return;
        foreach (var (id, name) in _npcNames)
        {
            if (lower.Length == 0 ||
                name.ToLowerInvariant().Contains(lower) ||
                id.ToString().Contains(lower))
            {
                _filtered.Add((id, name));
            }
        }
    }

    // ---------------- 生成 ----------------

    private void SpawnMany(int type)
    {
        var count = CheatFeatureHost.Config?.NpcSpawnCount ?? 1;
        for (var i = 0; i < count; i++) SpawnNPC(type);
    }

    /// <summary>原版 SpawnNPC：联机走 61 号包；单机随机找空地；肉山横跨屏幕。</summary>
    private static void SpawnNPC(int type)
    {
        var player = Game.LocalPlayer;
        Vector2 position;
        if (player is not null) position = player.Center;
        else position = new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f);

        if (Main.netMode == 1)
        {
            NetMessage.SendData(61, -1, -1, null, Main.myPlayer, (float)type, 0f, 0f, 0, 0, 0);
            return;
        }

        if (type == 113)
        {
            NPC.SpawnWOF(position);
            return;
        }

        var spawnType = type == 125 ? type + 1 : type;   // 双子眼 125 按原版惯例生成 126
        var startTileX = (int)(position.X / 16f);
        var startTileY = (int)(position.Y / 16f);
        GetRandomClearTileWithInRange(startTileX, startTileY, 100, 50, out var tileX, out var tileY);
        SpawnNpcAtTile(tileX, tileY, spawnType);
    }

    private static void SpawnNpcAtTile(int tileX, int tileY, int type)
    {
        // 代理里 EntitySource_WorldEvent 不继承 IEntitySource，用指针包一层
        var worldEvent = new EntitySource_WorldEvent();
        IEntitySource source = new((nint)worldEvent.Pointer);
        NPC.NewNPC(source, tileX * 16, tileY * 16, type, 0, 0f, 0f, 0f, 0f, 255);
    }

    /// <summary>
    /// 原版 NPC::GetRandomClearTileWithInRange：周围 range 内随机找非实体方块（最多 100 次，
    /// 找不到回退起始格）。实体判定 = active 且未致动且 tileSolid 且非半砖/斜坡/泡泡。
    /// </summary>
    private static void GetRandomClearTileWithInRange(int startTileX, int startTileY,
        int tileXRange, int tileYRange, out int tileX, out int tileY)
    {
        tileX = startTileX;
        tileY = startTileY;
        var j = 0;
        do
        {
            if (j == 100)
            {
                tileX = startTileX;
                tileY = startTileY;
                break;
            }
            tileX = startTileX + Main.rand.Next(-tileXRange, tileXRange + 1);
            tileY = startTileY + Main.rand.Next(-tileYRange, tileYRange + 1);
            j++;
        } while (TilePlacementValid(tileX, tileY) && TileSolid(tileX, tileY));
    }

    private static bool TilePlacementValid(int tileX, int tileY) =>
        tileX >= 0 && tileX < Main.maxTilesX && tileY >= 0 && tileY < Main.maxTilesY;

    private static bool TileSolid(int tileX, int tileY)
    {
        if (!TilePlacementValid(tileX, tileY)) return false;
        if (!Game.TileActive(tileX, tileY)) return false;
        var type = Game.TileType(tileX, tileY);
        var tileSolid = Main.tileSolid;
        if (type < 0 || tileSolid is null || type >= tileSolid.Length) return false;
        if (!tileSolid[type]) return false;
        if (Game.TileHalfBrick(tileX, tileY)) return false;
        if (Game.TileSlope(tileX, tileY) != 0) return false;
        if (type == 379) return false;   // TileID.Bubble
        return true;
    }
}
