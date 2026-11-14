using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pylon.NaviteAPI.Host;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 系统阶段（ModSystem 等价物的每帧面）：游戏的玩家/NPC/物品更新子阶段内联在
/// DoUpdateInWorld 方法体中部，九个中体桥把时机送到这里再扇出给 mod。
/// </summary>
internal static unsafe class SystemStages
{
    // ---- 阶段编号（内核 mod/SystemBridges.h 同表，增删要两边同步）----
    private const int PlayersBegin = 0;
    private const int PlayersEnd = 1;
    private const int NpcsBegin = 2;
    private const int NpcsEnd = 3;
    private const int GoresEnd = 4;
    private const int ItemsBegin = 5;
    private const int ItemsEnd = 6;
    private const int Transform = 7;
    private const int ClockAdvance = 8;
    private const int DyeReward = 9;

    private sealed class Stage(System.Type klass, string name, string method, int argc, int offset,
                               uint[] expected, HookId[] hooks)
    {
        public System.Type Class => klass;
        public string Name => name;
        public string Method => method;
        public int Argc => argc;
        public int Offset => offset;
        public uint[] Expected => expected; // 4 条指令指纹
        public HookId[] Hooks => hooks;     // 有订阅才装桥
        public bool Installed;
    }

    private static readonly Stage[] Stages =
    [
        new(typeof(Main), "PlayersBegin", "DoUpdateInWorld", 0, 0x1b8,
            [0xd00100bd, 0xd001011a, 0xf00100f9, 0xd000ffdc], [HookId.PreUpdatePlayers]),
        new(typeof(Main), "PlayersEnd", "DoUpdateInWorld", 0, 0x57c,
            [0xd0010028, 0xf940b108, 0x2a1f03f5, 0xf9400108], [HookId.PostUpdatePlayers]),
        new(typeof(Main), "NPCsBegin", "DoUpdateInWorld", 0, 0x884,
            [0xaa1f03e0, 0x3900811f, 0x94063b0a, 0xaa1f03e0], [HookId.PreUpdateNPCs]),
        // 游戏没有独立的 Gores 开始点：PreUpdateGores 与 PostUpdateNPCs 共用 NPCsEnd。
        new(typeof(Main), "NPCsEnd", "DoUpdateInWorld", 0, 0xf40,
            [0xd0010028, 0xf940b108, 0x120002c9, 0xd00100f5], [HookId.PostUpdateNPCs, HookId.PreUpdateGores]),
        new(typeof(Main), "GoresEnd", "DoUpdateInWorld", 0, 0x114c,
            [0x2a1f03f5, 0xf94003a0, 0xb940e008, 0x35000068], [HookId.PostUpdateGores]),
        // 弹幕的 Post 与物品的 Pre 共用 ItemsBegin（PostUpdateProjectiles 已归一到桥，见 ModEnvironmentHooks）。
        new(typeof(Main), "ItemsBegin", "DoUpdateInWorld", 0, 0x1740,
            [0xd000feb7, 0xf000fed8, 0xd000ff99, 0x9001017c], [HookId.PostUpdateProjectiles, HookId.PreUpdateItems]),
        new(typeof(Main), "ItemsEnd", "DoUpdateInWorld", 0, 0x1a14,
            [0x9000ffc8, 0xf9437908, 0xb0010037, 0xd00100b6], [HookId.PostUpdateItems]),
        new(typeof(Main), "Transform", "DoDraw", 1, 0x2468,
            [0x97fc20b9, 0x37000220, 0xf9400360, 0xb940e008], [HookId.ModifyTransformMatrix]),
        new(typeof(Main), "ClockAdvance", "UpdateTime", 0, 0xb8c,
            [0x1e602960, 0xfd024500, 0x97efb85d, 0xf94002e0], [HookId.ModifyTimeRate]),
        // 奖励池是方法局部变量（0xef0 时点落在 x21），入口钩够不到——走中体桥。
        new(typeof(Player), "DyeReward", "GetDyeTraderReward", 1, 0xef0,
            [0xb9401aa1, 0xd000e2b6, 0xb000e238, 0xf94202d6], [HookId.GetDyeTraderReward]),
    ];

    // 时钟缓存：UpdateTimeRate 后缀算好，ClockAdvance 桥注入 d0（d0 与 dayRate 的换算是
    // 真机验证的行为，不在这里重新推导）。
    private static double _clockRate = 1, _tileRemainder, _eventRemainder;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Build(IModLog log)
    {
        if (HookNative.IsAvailable() == 0)
        {
            log.Info("[system] ShadowHook 不可用，系统阶段不装");
            return;
        }

        var hooks = LoaderHost.Current.Hooks;
        var installed = 0;
        var names = new List<string>();

        // PreUpdateEntities / PostUpdateEverything 用 DoUpdateInWorld 入口钩就够（桥位在 0x1b8 以远，无冲突）。
        if (hooks.Channel(HookId.PreUpdateEntities).Length > 0 ||
            hooks.Channel(HookId.PostUpdateEverything).Length > 0)
        {
            On.Terraria.Main.DoUpdateInWorld += DoUpdateInWorld;
            names.Add("入口 PreUpdateEntities/PostUpdateEverything");
        }

        for (var i = 0; i < Stages.Length; i++)
        {
            var stage = Stages[i];
            if (stage.Installed) continue;
            if (!Gated(hooks, stage.Hooks)) continue;

            try
            {
                var klass = MethodCode.KlassOf(stage.Class);
                if (!MethodCode.TryResolve(klass, stage.Method, stage.Argc, out var body, out _))
                {
                    log.Warn($"[system] {stage.Name}：{stage.Method} 没解析到，跳过");
                    continue;
                }

                var rc = SystemBridgeNative.Install(i, body, stage.Offset, stage.Expected, out _);
                if (rc == -2)
                {
                    log.Warn($"[system] {stage.Name}：桥位指纹不符（游戏版本变了），跳过");
                    continue;
                }
                if (rc != 0)
                {
                    log.Warn($"[system] {stage.Name}：挂钩失败 rc={rc}（{PylonError()}），跳过");
                    continue;
                }

                SystemBridgeNative.Subscribe(i, &OnStage);
                stage.Installed = true;
                installed++;
                names.Add(stage.Name);

                if (i == ClockAdvance) On.Terraria.Main.UpdateTimeRate += UpdateTimeRate;
            }
            catch (Exception e)
            {
                log.Warn($"[system] {stage.Name} 安装失败：" + Safe.Describe(e));
            }
        }

        if (installed > 0)
        {
            log.Info($"[system] 系统阶段已挂（{installed} 桥：{string.Join("/", names)}）");
        }
        else if (names.Count > 0)
        {
            log.Info($"[system] 已挂（{string.Join("/", names)}；中体桥一个都没装上）");
        }
    }

    private static bool Gated(HookBus hooks, HookId[] ids)
    {
        foreach (var id in ids)
        {
            if (hooks.Channel(id).Length > 0) return true;
        }
        return false;
    }

    private static string PylonError()
    {
        var pointer = PatchNative.LastError();
        return pointer == IntPtr.Zero ? "内核无明细" : Marshal.PtrToStringUTF8(pointer) ?? "内核无明细";
    }

    // ===================== 每帧分派 =====================

    [UnmanagedCallersOnly]
    private static void OnStage(int stage, IntPtr value)
    {
        try
        {
            switch (stage)
            {
                case PlayersBegin: Fan(HookId.PreUpdatePlayers); break;
                case PlayersEnd: Fan(HookId.PostUpdatePlayers); break;
                case NpcsBegin: Fan(HookId.PreUpdateNPCs); break;
                case NpcsEnd:
                    Fan(HookId.PostUpdateNPCs);
                    Fan(HookId.PreUpdateGores);
                    break;
                case GoresEnd: Fan(HookId.PostUpdateGores); break;
                case ItemsBegin:
                    Fan(HookId.PostUpdateProjectiles);
                    Fan(HookId.PreUpdateItems);
                    break;
                case ItemsEnd: Fan(HookId.PostUpdateItems); break;
                case Transform: ModifyTransform(); break;
                case ClockAdvance:
                    if (value != IntPtr.Zero) Unsafe.Write((void*)value, _clockRate);
                    break;
                case DyeReward:
                    if (value != IntPtr.Zero) RewriteDyePool(*(DyeFrame*)value);
                    break;
            }
        }
        catch (Exception e)
        {
            // 异常越过 UnmanagedCallersOnly 帧就是进程崩溃，必须吃干净。
            Safe.Once("system.stage", "[system] 阶段分派异常：" + Safe.Describe(e), true);
        }
    }

    private static void Fan(HookId id)
    {
        foreach (var mod in LoaderHost.Current.Hooks.Channel(id))
        {
            try
            {
                switch (id)
                {
                    case HookId.PreUpdatePlayers: mod.PreUpdatePlayers(); break;
                    case HookId.PostUpdatePlayers: mod.PostUpdatePlayers(); break;
                    case HookId.PreUpdateNPCs: mod.PreUpdateNPCs(); break;
                    case HookId.PostUpdateNPCs: mod.PostUpdateNPCs(); break;
                    case HookId.PreUpdateGores: mod.PreUpdateGores(); break;
                    case HookId.PostUpdateGores: mod.PostUpdateGores(); break;
                    case HookId.PostUpdateProjectiles: mod.PostUpdateProjectiles(); break;
                    case HookId.PreUpdateItems: mod.PreUpdateItems(); break;
                    case HookId.PostUpdateItems: mod.PostUpdateItems(); break;
                    case HookId.PreUpdateEntities: mod.PreUpdateEntities(); break;
                    case HookId.PostUpdateEverything: mod.PostUpdateEverything(); break;
                }
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + "." + id, e);
            }
        }
    }

    private static void ModifyTransform()
    {
        if (Main.gameMenu) return;

        var transform = Main.GameViewMatrix;
        foreach (var mod in LoaderHost.Current.Hooks.Channel(HookId.ModifyTransformMatrix))
        {
            try
            {
                mod.ModifyTransformMatrix(ref transform);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".ModifyTransformMatrix", e);
            }
        }
        Main.GameViewMatrix = transform;
    }

    // ===================== 染料商奖励池（GetDyeTraderReward）=====================

    // 桥在 GetDyeTraderReward 方法体 0xef0（局部 List 落在 x21）：指针随 DyeFrame 进来，
    // 池手术全在托管侧走生成代理（等价 runtime_invoke Clear/Add）。
    private const int DyePoolCapacity = 512;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DyeFrame(IntPtr player, IntPtr pool)
    {
        public readonly IntPtr Player = player;
        public readonly IntPtr Pool = pool;
    }

    // 整池改写：读现池 → 派发 → Clear 重写（跳过非正数）→
    // 全空补 0（游戏随后随机取池，空池会崩）；改写中途失败就停在部分重写。
    private static void RewriteDyePool(DyeFrame frame)
    {
        if (frame.Pool == IntPtr.Zero) return;

        try
        {
            var list = new Il2CppSystem.Collections.Generic.List<int>(frame.Pool);
            var items = list._items;
            var size = list._size;
            var count = Math.Min(Math.Max(size, 0), DyePoolCapacity);

            var pool = new List<int>(count);
            if (items is not null)
            {
                for (var i = 0; i < count; i++) pool.Add(items[i]);
            }

            foreach (var mod in LoaderHost.Current.Hooks.Channel(HookId.GetDyeTraderReward))
            {
                try
                {
                    mod.GetDyeTraderReward(ref pool);
                }
                catch (Exception e)
                {
                    Safe.Report(mod.Name + ".GetDyeTraderReward", e);
                }
            }

            list.Clear();
            var added = 0;
            for (var i = 0; i < pool.Count && added < DyePoolCapacity; i++)
            {
                if (pool[i] <= 0) continue;
                list.Add(pool[i]);
                added++;
            }

            if (added == 0) list.Add(0);
        }
        catch (Exception e)
        {
            Safe.Once("system.dye", "[system] 染料商奖励池改写失败，保持原池：" + Safe.Describe(e), true);
        }
    }

    // ===================== 时钟（ModifyTimeRate）=====================

    // 本构建没有可写的 timeRate 字段：三倍率落在 dayRate 与 desiredWorldTilesUpdateRate
    // 两个 int 上（小数用余数累加保精度），d0 由 ClockAdvance 桥注入。
    private static void UpdateTimeRate(On.Terraria.Main.orig_UpdateTimeRate orig)
    {
        orig();

        double time = Main.dayRate, tiles = Main.desiredWorldTilesUpdateRate, events = Main.dayRate;
        double initialTime = time, initialTiles = tiles;

        foreach (var mod in LoaderHost.Current.Hooks.Channel(HookId.ModifyTimeRate))
        {
            try
            {
                mod.ModifyTimeRate(ref time, ref tiles, ref events);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".ModifyTimeRate", e);
            }
        }

        _clockRate = Rate(time, initialTime);
        var tileRate = Rate(tiles, initialTiles) + _tileRemainder;
        var eventRate = Rate(events, initialTime) + _eventRemainder;
        Main.desiredWorldTilesUpdateRate = (int)Math.Floor(tileRate);
        Main.dayRate = (int)Math.Floor(eventRate);
        _tileRemainder = tileRate % 1;
        _eventRemainder = eventRate % 1;
    }

    /// <summary>作者写出的倍率不合法（NaN/负数/超 int）就保持原值，不污染时钟。</summary>
    private static double Rate(double value, double fallback) =>
        double.IsFinite(value) && value >= 0 && value <= int.MaxValue ? value : fallback;

    private static void DoUpdateInWorld(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
    {
        Fan(HookId.PreUpdateEntities);
        orig(self);
        Fan(HookId.PostUpdateEverything);
    }
}
