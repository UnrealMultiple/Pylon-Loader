using System;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 生物无敌。1:1 对应原版 <c>NPCDamgeDebugTool</c>（杂项 · 漏洞利用）：
/// 每 0.05 秒扫描 NPC，把生命抬到协议上溢上限（byte→short→int 档位），
/// 再发 153 号包（NPCDebuffDamage）同步负伤害，服务器侧同样抬生命。
/// 联机自担风险，默认关闭。
/// </summary>
public sealed class NpcImmortalFeature : CheatFeature
{
    private static long _lastSend;

    public override string Id => "misc.npcimmortal";
    public override string Title => "生物无敌";
    public override CheatCategory Category => CheatCategory.Misc;
    public override string SubPage => "漏洞利用";

    public override void Tick()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is not { NpcImmortal: true }) return;

        var now = Environment.TickCount64;
        if (now - _lastSend < 50) return;
        _lastSend = now;

        var npcs = Main.npc;
        if (npcs is null) return;

        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is null || !npc.active || npc.immortal) continue;

            var lifeMax = npc.lifeMax;
            // 生命抬到当前档位上限，保证后续伤害不会击杀（原版逐档：127 → 32767 → int.MaxValue）
            var maxLifeWithoutOverflowing = lifeMax > short.MaxValue
                ? int.MaxValue
                : lifeMax > sbyte.MaxValue
                    ? short.MaxValue
                    : sbyte.MaxValue;

            var life = npc.life;
            var damage = Math.Max(life - maxLifeWithoutOverflowing, short.MinValue);
            if (damage >= 0) continue;

            var index = i;
            var packet = new RawPacket();
            packet.Packet(153, w =>
            {
                w.Write((sbyte)index);
                w.Write((short)damage);
            });
            if (packet.Send())
            {
                npc.life = life - damage;
            }
        }
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("生物无敌", "NPC IMMORTAL");
        RowToggle("启用", "##npcimmortal", cfg.NpcImmortal, v => cfg.NpcImmortal = v);
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
