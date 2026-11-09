using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>表情管线的探针：建泡/每帧/绘制/菜单取帧/全局钩子的计数。</summary>
public static class EmoteProbe
{
    public static readonly List<int> Frames = [];
    public static readonly HashSet<IntPtr> Bound = [];
    public static int Spawns, Updates, Pres, Posts, MenuFrames, GlobalSpawns;

    public static void Reset()
    {
        Frames.Clear();
        Bound.Clear();
        Spawns = Updates = Pres = Posts = MenuFrames = GlobalSpawns = 0;
    }
}

/// <summary>解锁的 mod 表情：进"通用"分类，NPC 用原版爱心回应。</summary>
public sealed class TestEmoteBubble : ModEmoteBubble
{
    public override string DisplayName => "Test Bubble";

    public override int NPCReaction => EmoteID.EmotionLove;

    public override void SetStaticDefaults() => AddToCategory(0);

    public override void OnSpawn()
    {
        EmoteProbe.Spawns++;
        EmoteProbe.Bound.Add(EmoteBubble.Pointer);
    }

    public override bool UpdateFrame()
    {
        EmoteProbe.Updates++;
        return true;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Texture2D texture, Vector2 position, Rectangle frame,
                                 Vector2 origin, SpriteEffects spriteEffects)
    {
        EmoteProbe.Pres++;
        EmoteProbe.Frames.Add(frame.X);
        return true;
    }

    public override void PostDraw(SpriteBatch spriteBatch, Texture2D texture, Vector2 position, Rectangle frame,
                                  Vector2 origin, SpriteEffects spriteEffects) => EmoteProbe.Posts++;

    public override Rectangle? GetFrameInEmoteMenu(int frame, int frameCounter)
    {
        EmoteProbe.MenuFrames++;
        return null;
    }
}

/// <summary>没解锁的表情：菜单里不该出现。</summary>
public sealed class TestLockedEmote : ModEmoteBubble
{
    public override void SetStaticDefaults() => AddToCategory(0);

    public override bool IsUnlocked() => false;
}

/// <summary>横切钩子：原版表情建泡也要派。</summary>
public sealed class TestGlobalEmote : GlobalEmoteBubble
{
    public override void OnSpawn(EmoteBubble bubble)
    {
        if (bubble is not null && bubble.emote == EmoteID.EmotionLove) EmoteProbe.GlobalSpawns++;
    }
}

