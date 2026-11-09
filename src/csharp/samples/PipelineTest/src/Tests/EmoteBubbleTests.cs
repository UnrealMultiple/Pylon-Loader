using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 25：EmoteBubble（表情气泡）。
/// Ready：编号、菜单分类、解锁过滤。
/// World：建泡登记、OnSpawn、原版表情上的全局钩子、NPC 反应、每帧/绘制/菜单取帧。
/// </summary>
internal static class EmoteBubbleTests
{
    private static int _first, _second;

    public static void Register()
    {
        PT.Group("emotebubble", Phase.Ready, RunReady);
        PT.Group("emotebubble-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("EMO-R1", c =>
        {
            var emote = ModContent.GetInstance<Content.TestEmoteBubble>();
            var locked = ModContent.GetInstance<Content.TestLockedEmote>();

            c.NotNull(emote, "TestEmoteBubble 实例");
            c.NotNull(locked, "TestLockedEmote 实例");
            c.True(emote.Type >= EmoteID.Count, $"编号={emote.Type}（应 >= {EmoteID.Count}）");
            c.True(locked.Type > emote.Type, $"第二个编号={locked.Type}（应比第一个大）");
        });

        // 点击表情时游戏走 GUIEmotesWindow.EmoteOver → EmoteID.Search.GetName(编号) 拼聊天文本。
        // 表里没有 mod 编号时 IdDictionary 直接抛 KeyNotFoundException，穿过 il2cpp 边界就是 SIGABRT。
        PT.Case("EMO-R3", c =>
        {
            var emote = ModContent.GetInstance<Content.TestEmoteBubble>();
            var name = EmoteID.Search.GetName(emote.Type);
            c.True(!string.IsNullOrEmpty(name), $"EmoteID.Search 认得出 mod 编号（{emote.Type} → {name}）");

            // 聊天/悬停显示的是 "EmojiName." + 名字 这条语言键，没登记就会显示原始键名。
            var key = "EmojiName." + emote.Name;
            var text = Terraria.Localization.Language.GetTextValue(key);
            c.True(text != key && !string.IsNullOrEmpty(text), $"语言键 {key} → {text}");
        });

        PT.Case("EMO-R2", c =>
        {
            var emote = ModContent.GetInstance<Content.TestEmoteBubble>();
            c.Eq(emote.DisplayName, "Test Bubble", "显示名");
            c.True(emote.IsUnlocked(), "默认解锁");
            c.True(!ModContent.GetInstance<Content.TestLockedEmote>().IsUnlocked(), "锁定表情");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("EMO-W1", "玩家还没就绪");
            return;
        }

        Content.EmoteProbe.Reset();
        var type = ModContent.GetInstance<Content.TestEmoteBubble>().Type;

        PT.Case("EMO-W1", c =>
        {
            _first = EmoteBubble.NewBubble(type, new WorldUIAnchor(player), 600);
            _second = EmoteBubble.NewBubble(type, new WorldUIAnchor(player.Center + new Vector2(80f, 0f)), 600);

            var byID = EmoteBubble.byID;
            c.True(_first >= 0 && _second >= 0 && _second != _first, $"建泡 id={_first},{_second}");
            c.True(byID is not null && byID.TryGetValue(_first, out var bubble) && bubble is not null && bubble.emote == type,
                "泡泡登记在 byID 里且编号是 mod 编号");
            c.Eq(Content.EmoteProbe.Spawns, 2, "OnSpawn 调用数");
            c.Eq(Content.EmoteProbe.Bound.Count, 2, "两个泡泡各绑各的实例");
        });

        PT.Case("EMO-W2", c =>
        {
            int before = Content.EmoteProbe.GlobalSpawns;
            EmoteBubble.NewBubble(EmoteID.EmotionLove, new WorldUIAnchor(player), 300);
            c.True(Content.EmoteProbe.GlobalSpawns > before, $"全局 OnSpawn 在原版表情上也派（{before}→{Content.EmoteProbe.GlobalSpawns}）");
        });

        PT.Case("EMO-W3", c =>
        {
            var source = player.GetProjectileSource_Misc(0);
            int guide = NPC.NewNPC(source, (int)player.Center.X - 60, (int)player.Center.Y - 30, NPCID.Guide, 0, 0f, 0f, 0f, 0f, 0);
            c.True(guide >= 0 && Main.npc[guide].active, $"生成城镇 NPC（id={guide}）");

            int before = CountEmotes(EmoteID.EmotionLove);
            EmoteBubble.CheckForNPCsToReactToEmoteBubble(type, player);
            c.True(CountEmotes(EmoteID.EmotionLove) > before, "NPC 用作者指定的原版表情回应");

            if (guide >= 0) Main.npc[guide].active = false;
        });

        PT.Case("EMO-W4", c =>
        {
            // 菜单点击走的就是"只在本机显示"这条建泡路径。
            int id = EmoteBubble.NewLocalOnlyBubble(type, new WorldUIAnchor(player), 300);
            var byID = EmoteBubble.byID;
            c.True(id >= 0 && byID is not null && byID.TryGetValue(id, out var bubble) && bubble is not null && bubble.emote == type,
                $"本机建泡 id={id}");
        });

        PT.Case("EMO-W5", c =>
        {
            // 菜单点击走的是这个入口：直接调一次，看它认不认 mod 编号。
            int before = CountEmotes(type);
            EmoteBubble.MakeLocalPlayerEmote(type);
            c.True(CountEmotes(type) > before, $"MakeLocalPlayerEmote 建出了泡泡（{before}→{CountEmotes(type)}）");
        });

        PT.Later("EMO-DRAW", 40, c =>
        {
            c.True(Content.EmoteProbe.Updates > 0, $"UpdateFrame 调用={Content.EmoteProbe.Updates}");
            c.True(Content.EmoteProbe.Pres > 0 && Content.EmoteProbe.Posts > 0, $"PreDraw={Content.EmoteProbe.Pres} PostDraw={Content.EmoteProbe.Posts}");
            var distinct = new HashSet<int>(Content.EmoteProbe.Frames);
            c.True(distinct.Count == 2, $"动画取到两帧（{string.Join(",", distinct)}）");
        });

        // 先把菜单打开（列表与绘制都在下一帧才发生），下一段延迟用例再查。
        PT.Later("EMO-MENU", 60, c =>
        {
            var window = GUIInstance.Active.GUIEmotesWindow;
            c.NotNull(window, "表情菜单");
            window.SetCategory(GUIEmotesWindow.Category.General);
            window.SetDisplayingEmotes(true);
        });

        PT.Later("EMO-MENU-LIST", 100, c =>
        {
            var window = GUIInstance.Active.GUIEmotesWindow;
            var entries = window?._emoteEntries;
            bool listed = false, locked = false;
            var test = ModContent.GetInstance<Content.TestEmoteBubble>().Type;
            var hidden = ModContent.GetInstance<Content.TestLockedEmote>().Type;

            if (entries is not null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    listed |= entries[i] == test;
                    locked |= entries[i] == hidden;
                }
            }

            c.True(listed, "通用分类里列出了解锁的 mod 表情");
            c.True(!locked, "锁定表情不进菜单");
            c.True(Content.EmoteProbe.MenuFrames > 0, $"菜单取帧被调用={Content.EmoteProbe.MenuFrames}");

            window?.SetDisplayingEmotes(false);
        });
    }

    private static int CountEmotes(int emote)
    {
        var byID = EmoteBubble.byID;
        if (byID is null) return 0;

        var count = 0;
        foreach (var pair in byID)
            if (pair.Value is not null && pair.Value.emote == emote) count++;

        return count;
    }
}
