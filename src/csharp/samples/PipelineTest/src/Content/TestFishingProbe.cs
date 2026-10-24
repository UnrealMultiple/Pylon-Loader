using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace PipelineTest.Content;

// 专用探针：只针对测试玩家/弹幕开启，避免实际捕鱼或其它测试污染计数。
internal static class FishingProbe
{
    internal static bool Active, AlterLine, ThrowCaught;
    internal static int PlayerIndex = -1, ProjectileIndex = -1;
    internal static int CaughtCalls, ProjectileCalls, ItemCalls;
    internal static string Order = "";
    internal static Vector2 ItemInputOffset;
    internal static Color ItemInputColor;
    internal static Item LastFish;
    internal static readonly Vector2 ProjectileOffset = new Vector2(30f, -20f);
    internal static readonly Vector2 ItemOffset = new Vector2(12f, -8f);
    internal static readonly Color ProjectileColor = new Color(255, 40, 170, 255);
    internal static readonly Color ItemColor = new Color(30, 240, 90, 255);

    internal static void Reset()
    {
        AlterLine = ThrowCaught = false;
        CaughtCalls = ProjectileCalls = ItemCalls = 0;
        Order = "";
        LastFish = null;
        ItemInputOffset = Vector2.Zero;
        ItemInputColor = default;
    }

    internal static bool Matches(Projectile projectile) =>
        Active && projectile.whoAmI == ProjectileIndex && projectile.owner == PlayerIndex;
}

public sealed class TestFishingPlayer : ModPlayer
{
    public override void ModifyCaughtFish(Item fish)
    {
        if (!FishingProbe.Active || Player.whoAmI != FishingProbe.PlayerIndex) return;
        FishingProbe.CaughtCalls++;
        FishingProbe.LastFish = fish;
        if (FishingProbe.ThrowCaught) throw new InvalidOperationException("PT fishing caught probe");
        // 保留原版鱼种，仅写一个不依赖 RNG 的数量，检查是否在入包前修改真正的物品。
        fish.stack = 3;
    }
}

public sealed class TestFishingBobber : ModProjectile
{
    public override string TexturePath => "assets/projectiles/TestProjectile.png";

    public override void SetDefaults()
    {
        Projectile.width = Projectile.height = 14;
        Projectile.aiStyle = 61;
        Projectile.friendly = true;
        Projectile.tileCollide = false;
        Projectile.timeLeft = 600;
    }

    public override void ModifyFishingLine(ref Vector2 lineOriginOffset, ref Color lineColor)
    {
        if (!FishingProbe.Matches(Projectile)) return;
        FishingProbe.ProjectileCalls++;
        FishingProbe.Order += "P";
        if (!FishingProbe.AlterLine) return;
        lineOriginOffset += FishingProbe.ProjectileOffset;
        lineColor = FishingProbe.ProjectileColor;
    }
}

public sealed class TestFishingPole : ModItem
{
    public override string TexturePath => "assets/items/TestItem.png";
    public override string DisplayName => "捕鱼入口探针鱼竿";

    public override void SetDefaults()
    {
        Item.width = Item.height = 20;
        Item.maxStack = 1;
        Item.fishingPole = 20;
        Item.shoot = ModContent.ProjectileType<TestFishingBobber>();
        Item.shootSpeed = 8f;
    }

    public override void ModifyFishingLine(Projectile bobber, ref Vector2 lineOriginOffset, ref Color lineColor)
    {
        if (!FishingProbe.Matches(bobber)) return;
        FishingProbe.ItemCalls++;
        FishingProbe.Order += "I";
        FishingProbe.ItemInputOffset = lineOriginOffset;
        FishingProbe.ItemInputColor = lineColor;
        if (!FishingProbe.AlterLine) return;
        lineOriginOffset += FishingProbe.ItemOffset;
        lineColor = FishingProbe.ItemColor;
    }
}
