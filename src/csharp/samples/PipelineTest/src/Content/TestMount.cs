using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试坐骑 buff：骑乘期间持续续时间并骑回本坐骑。</summary>
public sealed class TestMountBuff : ModBuff
{
    public static int UpdateCount;

    public override string DisplayName => "测试坐骑";

    public override void SetStaticDefaults()
    {
        Main.buffNoTimeDisplay[Type] = true;
        Main.buffNoSave[Type] = true;

        // 原版在 BuffID.Sets 里标记"这个 buff 是坐骑 buff"（不显示时间、骑乘维持）；
        // 框架的坐骑表只补 noTime/noSave，MountType 指向要作者自己写。
        BuffID.Sets.MountType[Type] = ModContent.MountType<TestMount>();
    }

    public override void UpdatePlayer(Player player, int buffIndex)
    {
        UpdateCount++;
        player.mount.SetMount(ModContent.MountType<TestMount>(), player);
        player.buffTime[buffIndex] = 10;
    }
}

/// <summary>
/// 测试坐骑：每个钩子都计数，SetStaticDefaults 填全 MountData；贴图 assets/mounts/TestMount_Back/_Front.png。
/// </summary>
public sealed class TestMount : ModMount
{
    public static readonly Dictionary<string, int> Calls = new();
    public static int Ticks;
    public static bool DrawSeen;
    public static int JumpHeightSeen = -1;
    public static float JumpSpeedSeen = float.NaN;

    public static void Count(string name) => Calls[name] = Calls.GetValueOrDefault(name) + 1;

    public override void SetStaticDefaults()
    {
        Count(nameof(SetStaticDefaults));

        var data = MountData;
        data.jumpHeight = 5;
        data.acceleration = 0.19f;
        data.jumpSpeed = 4f;
        data.blockExtraJumps = false;
        data.constantJump = true;
        data.heightBoost = 20;
        data.fallDamage = 0.5f;
        data.runSpeed = 11f;
        data.dashSpeed = 8f;
        data.flightTimeMax = 0;
        data.fatigueMax = 0;
        data.buff = ModContent.BuffType<TestMountBuff>();
        data.spawnDust = DustID.Cloud;
        data.totalFrames = 4;
        data.playerYOffsets = new[] { 20, 20, 20, 20 };
        data.xOffset = 13;
        data.yOffset = -12;
        data.playerHeadOffset = 22;
        data.bodyFrame = 3;
        data.standingFrameCount = 4;
        data.standingFrameDelay = 12;
        data.standingFrameStart = 0;
        data.runningFrameCount = 4;
        data.runningFrameDelay = 12;
        data.runningFrameStart = 0;
        data.inAirFrameCount = 1;
        data.inAirFrameDelay = 12;
        data.inAirFrameStart = 0;
        data.idleFrameCount = 4;
        data.idleFrameDelay = 12;
        data.idleFrameStart = 0;
        data.idleFrameLoop = true;
        data.swimFrameCount = 1;
        data.swimFrameDelay = 12;
        data.swimFrameStart = 0;

        // 游戏层 Texture2D.Width/Height 不保证已填：尺寸要从 Unity 侧读。
        var back = data.backTexture;
        if (back is { Value: not null })
        {
            Pylon.ModLoader.Textures.PylonTexture.Size(back.Value, out var w, out var h);
            data.textureWidth = w + 20;
            data.textureHeight = h;
        }
    }

    public override void SetMount(Player player, ref bool skipDust)
    {
        Count(nameof(SetMount));
        Ticks = 0;
        skipDust = true;
    }

    public override void Dismount(Player player, ref bool skipDust)
    {
        Count(nameof(Dismount));
        skipDust = true;
    }

    public override void UpdateEffects(Player player)
    {
        Count(nameof(UpdateEffects));
        Ticks++;
    }

    public override bool UpdateFrame(Player mountedPlayer, int state, Vector2 velocity)
    {
        Count(nameof(UpdateFrame));
        return true;
    }

    public override void JumpHeight(Player mountedPlayer, ref int jumpHeight, float xVelocity)
    {
        Count(nameof(JumpHeight));
        JumpHeightSeen = jumpHeight;
    }

    public override void JumpSpeed(Player mountedPlayer, ref float jumpSpeed, float xVelocity)
    {
        Count(nameof(JumpSpeed));
        JumpSpeedSeen = jumpSpeed;
    }

    public override void UseAbility(Player player, Vector2 mousePosition, bool toggleOn) =>
        Count(nameof(UseAbility));

    public override void AimAbility(Player player, Vector2 mousePosition) =>
        Count(nameof(AimAbility));

    public override bool Draw(PlayerDrawSet playerDrawData, int drawType, Player drawPlayer,
        ref Texture2D texture, ref Texture2D glowTexture, ref Vector2 drawPosition, ref Rectangle frame,
        ref Color drawColor, ref Color glowColor, ref float rotation, ref SpriteEffects spriteEffects,
        ref Vector2 drawOrigin, ref float drawScale, float shadow)
    {
        Count(nameof(Draw) + drawType);
        DrawSeen = true;
        return true;
    }
}

/// <summary>使用后骑上测试坐骑的物品。</summary>
public sealed class TestMountItem : ModItem
{
    public override string DisplayName => "测试坐骑物品";

    public override void SetDefaults(Item item)
    {
        item.width = 20;
        item.height = 30;
        item.useTime = 20;
        item.useAnimation = 20;
        item.useStyle = (byte)Terraria.ID.ItemUseStyleID.Swing;
        item.noMelee = true;
        item.mountType = ModContent.MountType<TestMount>();
    }
}
