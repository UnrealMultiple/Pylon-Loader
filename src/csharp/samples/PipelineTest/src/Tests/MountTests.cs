using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;

namespace PipelineTest.Tests;

/// <summary>
/// 坐骑管线。
/// mount（Ready）：类型注册、MountData 字段、贴图层、MountID.Sets 扩长、buff 标记、物品 mountType。
/// mount-w（World）：骑上/下来、每帧效果/动画、跳跃、贴身数据往返、绘制。
/// </summary>
internal static class MountTests
{
    public static void Register()
    {
        PT.Group("mount", Phase.Ready, RunReady);
        PT.Group("mount-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("MOUNT-R1", c =>
        {
            var type = ModContent.MountType<Content.TestMount>();
            c.Eq(type, MountID.Count - 1, "mod 坐骑占最后一个号");
            c.NotNull(Mount.mounts[type], "Mount.mounts[type] 已建");
            c.True(MountID.Sets.Cart.Length >= MountID.Count, "Sets 表长 >= 总数");
        });

        PT.Case("MOUNT-R2", c =>
        {
            var type = ModContent.MountType<Content.TestMount>();
            var data = Mount.mounts[type];
            c.NotNull(data, "Mount.mounts[type]");
            if (data is null) return;

            c.Eq(data.totalFrames, 4, "totalFrames");
            c.Eq(data.jumpHeight, 5, "jumpHeight");
            c.Eq(data.runSpeed, 11f, "runSpeed");
            c.Eq(data.buff, ModContent.BuffType<Content.TestMountBuff>(), "buff");
            c.NotNull(data.backTexture, "Back 贴图层");
            c.NotNull(data.frontTexture, "Front 贴图层");
            c.True(data.textureWidth > 0 && data.textureHeight > 0,
                $"textureWidth={data.textureWidth} textureHeight={data.textureHeight}");
            c.NotNull(data.playerYOffsets, "playerYOffsets");
        });

        PT.Case("MOUNT-R3", c =>
        {
            var type = ModContent.MountType<Content.TestMount>();
            c.True(type < MountID.Sets.CanDash.Length, $"CanDash 表长 {MountID.Sets.CanDash.Length} > type");
            c.True(type < MountID.Sets.Cart.Length, $"Cart 表长 {MountID.Sets.Cart.Length} > type");
            c.Eq(Mount.mounts.Length, MountID.Count, "Mount.mounts 长度 == MountID.Count");
        });

        PT.Case("MOUNT-R4", c =>
        {
            var buffType = ModContent.BuffType<Content.TestMountBuff>();
            c.True(BuffID.Sets.MountType[buffType] >= 0, $"BuffID.Sets.MountType={BuffID.Sets.MountType[buffType]}");
            c.True(Main.buffNoTimeDisplay[buffType], "buffNoTimeDisplay");
            c.True(Main.buffNoSave[buffType], "buffNoSave");
        });

        PT.Case("MOUNT-R5", c =>
        {
            var itemType = ModContent.ItemType<Content.TestMountItem>();
            var item = ContentSamples.ItemsByType[itemType];
            c.NotNull(item, "TestMountItem 模板");
            c.Eq(item?.mountType ?? -1, ModContent.MountType<Content.TestMount>(), "item.mountType");
        });

        PT.Case("MOUNT-R6", c =>
        {
            var mountType = ModContent.MountType<Content.TestMount>();
            c.Eq(MountLoader.MountCount, Terraria.ID.MountID.Count, "MountCount = MountID.Count");
            c.True(MountLoader.GetMount(mountType) == ModContent.GetInstance<Content.TestMount>(), "GetMount 反查");
            c.True(MountLoader.GetMount(1) is null, "原版号 GetMount = null");
            c.True(MountLoader.IsModMount(ModContent.GetInstance<Content.TestMount>().MountData), "IsModMount(mod)");
            c.True(!MountLoader.IsModMount(Terraria.Mount.mounts[1]), "IsModMount(原版) = false");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("MOUNT-W1", "玩家还没就绪");
            return;
        }

        var type = ModContent.MountType<Content.TestMount>();
        var buffType = ModContent.BuffType<Content.TestMountBuff>();

        PT.Case("MOUNT-W1", c =>
        {
            // 标准做法：挂测试坐骑 buff，buff 每帧骑回本坐骑并续时到 300；断言走轮询——
            // 骑乘一旦掉落立刻带现场（buff 槽/时间/Update 计数/玩家状态）收口，没掉就巡到 150 帧。
            Content.TestMount.Calls.Clear();
            Content.TestMount.Ticks = 0;
            Content.TestMount.JumpHeightSeen = -1;
            Content.TestMount.DrawSeen = false;
            player.AddBuff(buffType, 300, true);

            var mountRounds = 0;
            var deathLogged = false;
            void PollMount(Check cc)
            {
                mountRounds++;
                if (!player.mount.Active && !deathLogged)
                {
                    deathLogged = true;
                    var slot = player.FindBuffIndex(buffType);
                    PT.Info($"[probe] 掉落现场：第 {mountRounds} 轮（约第 {mountRounds * 15} 更新帧）slot={slot} " +
                            $"buffTime={(slot >= 0 ? player.buffTime[slot] : -1)} buffUpdate={Content.TestMountBuff.UpdateCount} " +
                            $"effects={Calls(nameof(Content.TestMount.UpdateEffects))} " +
                            $"frame={Calls(nameof(Content.TestMount.UpdateFrame))} " +
                            $"dead={player.dead} life={player.statLife}/{player.statLifeMax2}");
                    PT.Info($"[probe] 最近 buff 摘除：{Content.DrawSiteProbe.BuffRemovalReport()}");
                }

                if (mountRounds < 10)
                {
                    cc.Pending();
                    PT.RetryLater("MOUNT-W1", 15, PollMount);
                    return;
                }

                cc.True(player.mount.Active && player.mount.Type == type,
                    $"骑乘 active={player.mount.Active} type={player.mount.Type}（巡检 {mountRounds} 轮）");
                cc.Eq(Calls(nameof(Content.TestMount.SetMount)), 1,
                    $"SetMount 次数={Calls(nameof(Content.TestMount.SetMount))}");

                var effects = Calls(nameof(Content.TestMount.UpdateEffects));
                cc.True(effects >= 100, $"UpdateEffects 累计={effects}");
                cc.Eq(Content.TestMount.Ticks, effects, $"贴身计数 Ticks={Content.TestMount.Ticks}");

                cc.True(Calls(nameof(Content.TestMount.UpdateFrame)) >= 100,
                    $"UpdateFrame 累计={Calls(nameof(Content.TestMount.UpdateFrame))}");
                cc.True(Calls(nameof(Content.TestMount.JumpHeight)) >= 1,
                    "JumpHeight 被调（骑乘跳跃计算）");
                cc.Eq(Content.TestMount.JumpHeightSeen, 5, $"JumpHeightSeen={Content.TestMount.JumpHeightSeen}");

                cc.True(Calls("Draw0") >= 30, $"Draw Back 层={Calls("Draw0")}");
                cc.True(Calls("Draw2") >= 30, $"Draw Front 层={Calls("Draw2")}");
            }

            PT.Later("MOUNT-W1", 15, PollMount);
        });

        PT.Case("MOUNT-W4", c =>
        {
            Il2CppSystem.String payload = "作者数据";
            player.mount._mountSpecificData = payload;

            var sameFrame = player.mount._mountSpecificData;
            PT.Info($"MOUNT-W4 探针：写后同帧读 = {sameFrame?.ToString() ?? "<null>"}");

            c.True(sameFrame?.ToString() == "作者数据", "_mountSpecificData 同帧往返");
            player.mount._mountSpecificData = null;
        });

        PT.Case("MOUNT-W3", c =>
        {
            // 收尾排在 W1 的 150 帧断言之后（帧号错开），不然同一帧就把骑乘拆了。
            PT.Later("MOUNT-W3", 160, cc =>
            {
                var dismounts = Calls(nameof(Content.TestMount.Dismount));
                var ticksBefore = Content.TestMount.Ticks;
                player.ClearBuff(buffType);
                player.mount.Dismount(player);
                cc.True(!player.mount.Active, $"下来后 mount.Active={player.mount.Active}");
                cc.True(Calls(nameof(Content.TestMount.Dismount)) > dismounts, "作者 Dismount 被调");
                cc.True(ticksBefore > 0, $"骑乘期间 UpdateEffects 累计={ticksBefore}");
            });
        });
    }
    private static int Calls(string name) => Content.TestMount.Calls.TryGetValue(name, out var v) ? v : 0;
}
