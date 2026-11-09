using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using PlayerDrawLayers = Pylon.ModLoader.PlayerDrawLayers;

namespace PipelineTest.Content;

/// <summary>
/// 断言只认"顺序下标"和"缓存条数"，不猜坐标。
/// </summary>
public static class PlayerDrawProbe
{
    public static int BetweenCount, ChildAfterCount, ChildBeforeCount, HiddenCount, MultipleCount;

    public static int CacheBefore = -1, CacheAfter = -1, TextureWidth = -1;
    public static bool AddOk;
    public static int Added;

    public static bool ShowVisuals;
    public static bool HideTarget;
    public static bool MultipleFirst = true;

    public static void Reset()
    {
        BetweenCount = ChildAfterCount = ChildBeforeCount = HiddenCount = MultipleCount = 0;
        CacheBefore = CacheAfter = TextureWidth = -1;
        AddOk = false;
        Added = 0;
        HideTarget = false;
        MultipleFirst = true;
    }

    /// <summary>往这一帧的绘制缓存里塞一条 DrawData（原版真正渲染的就是这个数组）。</summary>
    public static void AddMarker(PlayerDrawSet drawInfo, Vector2 offset)
    {
        if (drawInfo is null) return;

        try
        {
            CacheBefore = drawInfo.DrawDataCacheCount;

            var texture = Terraria.GameContent.TextureAssets.Item[ItemID.Muramasa].Value;
            if (texture is null) return;

            TextureWidth = texture.Width;

            // 手机版 DrawData 没有 sourceRect 参数，用三参构造。
            AddOk = ModPlayer.AddDrawData(drawInfo,
                new DrawData(texture, drawInfo.Position - Main.screenPosition + offset, Color.White));

            if (!AddOk) return;

            Added++;
            CacheAfter = drawInfo.DrawDataCacheCount;
        }
        catch (Exception e)
        {
            PipelineTestMod.Instance.Logger.Info("[PDL] 加绘制数据失败：" + e.GetType().Name + ": " + e.Message);
        }
    }
}

/// <summary>夹在躯干与副手饰品之间的层。</summary>
public sealed class TestLayerBetween : PlayerDrawLayer
{
    public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.Torso, PlayerDrawLayers.OffhandAcc);

    protected override void Draw(ref PlayerDrawSet drawInfo)
    {
        PlayerDrawProbe.BetweenCount++;
        if (PlayerDrawProbe.ShowVisuals) PlayerDrawProbe.AddMarker(drawInfo, Vector2.Zero);
    }
}

/// <summary>挂成躯干层之后的孩子。</summary>
public sealed class TestLayerChildAfter : PlayerDrawLayer
{
    public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.Torso);

    protected override void Draw(ref PlayerDrawSet drawInfo) => PlayerDrawProbe.ChildAfterCount++;
}

/// <summary>挂成躯干层之前的孩子。</summary>
public sealed class TestLayerChildBefore : PlayerDrawLayer
{
    public override Position GetDefaultPosition() => new BeforeParent(PlayerDrawLayers.Torso);

    protected override void Draw(ref PlayerDrawSet drawInfo) => PlayerDrawProbe.ChildBeforeCount++;
}

/// <summary>由 ModPlayer.HideDrawLayers 决定画不画。</summary>
public sealed class TestLayerHidden : PlayerDrawLayer
{
    public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.Skin, PlayerDrawLayers.Leggings);

    protected override void Draw(ref PlayerDrawSet drawInfo) => PlayerDrawProbe.HiddenCount++;
}

/// <summary>多槽层：同一层按条件落在两个不同位置。</summary>
public sealed class TestLayerMultiple : PlayerDrawLayer
{
    public override Position GetDefaultPosition() => new Multiple
    {
        { new Between(PlayerDrawLayers.Head, PlayerDrawLayers.FaceAcc), i => PlayerDrawProbe.MultipleFirst },
        { new Between(PlayerDrawLayers.BladedGlove, PlayerDrawLayers.ProjectileOverArm), i => !PlayerDrawProbe.MultipleFirst },
    };

    protected override void Draw(ref PlayerDrawSet drawInfo) => PlayerDrawProbe.MultipleCount++;
}

/// <summary>藏层 + 改排序都走 ModPlayer。</summary>
public sealed class TestDrawPlayer : ModPlayer
{
    public override void HideDrawLayers(PlayerDrawSet drawInfo)
    {
        if (PlayerDrawProbe.HideTarget) ModContent.GetInstance<TestLayerHidden>().Hide();
    }
}