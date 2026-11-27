using System;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 同步箱子大小。1:1 对应原版 <c>SyncChestSizeTool</c>（杂项 · 漏洞利用）：
/// 每 0.2 秒发 100 个 155 号包（SyncChestSize），把 size 字段写成 32767，
/// 客户端因此能往箱子里塞超量物品。联机自担风险，默认关闭。
/// </summary>
public sealed class SyncChestSizeFeature : CheatFeature
{
    private const int BatchSize = 100;
    private const short ExploitSize = 32767;

    private static int _currentIndex;
    private static long _lastSend;

    public override string Id => "misc.syncchest";
    public override string Title => "同步箱子大小";
    public override CheatCategory Category => CheatCategory.Misc;
    public override string SubPage => "漏洞利用";

    public override void Tick()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is not { ChestExploit: true })
        {
            _currentIndex = 0;
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastSend < 200) return;
        _lastSend = now;

        var maxChests = Main.maxChests;
        if (maxChests <= 0) maxChests = 1;

        var packet = new RawPacket();
        for (var i = 0; i < BatchSize; i++)
        {
            var index = i;
            packet.Packet(155, w =>
            {
                w.Write((short)((_currentIndex + index) % maxChests));
                w.Write(ExploitSize);
            });
        }
        if (packet.Send())
        {
            _currentIndex += BatchSize;
            _currentIndex %= maxChests;
        }
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("箱子漏洞", "CHEST EXPLOIT");

        ModernUI.BeginRow();
        ModernUI.RowLabel("当前索引", _currentIndex.ToString());
        ModernUI.EndRow();
        RowToggle("启用箱子容量漏洞", "##chestexploit", cfg.ChestExploit, v => cfg.ChestExploit = v);

        ModernUI.EndCard();
    }

    private static void RowToggle(string label, string id, bool value, Action<bool> set)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        if (ModernUI.Toggle(id, value))
        {
            set(!value);
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();
    }
}
