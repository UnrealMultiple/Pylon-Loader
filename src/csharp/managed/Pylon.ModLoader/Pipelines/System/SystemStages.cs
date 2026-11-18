using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Patching;
using Pylon.ModLoader.Pipeline;
using Pylon.NaviteAPI.Host;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>系统阶段（ModSystem 等价物的每帧面）：游戏的玩家/NPC/物品更新子阶段内联在 DoUpdateInWorld 方法体中部，九个中体桥把时机送到这里再扇出给 mod。</summary>
internal static unsafe class SystemStages
{
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
    private const int NaturalRegen = 10;

    private sealed class Stage(System.Type klass, string name, string method, int argc, int offset,
                               uint[] expected, HookId[] hooks, HookId? playerHook = null)
    {
        public System.Type Class => klass;
        public string Name => name;
        public string Method => method;
        public int Argc => argc;
        public int Offset => offset;
        public uint[] Expected => expected;
        public HookId[] Hooks => hooks;
        public HookId? PlayerHook => playerHook;
        public bool Installed;
        public int FirstHit;
        public MidBodyInterception? Interception;
    }

    private static readonly Stage[] Stages =
    [
        new(typeof(Main), "PlayersBegin", "DoUpdateInWorld", 0, 0x1b8,
            [0xf0011c1d, 0xb0011c9a, 0xb0011c79, 0x90011b3c], [HookId.PreUpdatePlayers]),
        new(typeof(Main), "PlayersEnd", "DoUpdateInWorld", 0, 0x57c,
            [0xd0011b88, 0xf9451908, 0x2a1f03f5, 0xf9400108], [HookId.PostUpdatePlayers]),
        new(typeof(Main), "NPCsBegin", "DoUpdateInWorld", 0, 0x884,
            [0xaa1f03e0, 0x3900811f, 0x94063b79, 0xaa1f03e0], [HookId.PreUpdateNPCs]),
        new(typeof(Main), "NPCsEnd", "DoUpdateInWorld", 0, 0xf40,
            [0xb0011b88, 0xf9451908, 0x120002c9, 0x90011c75], [HookId.PostUpdateNPCs, HookId.PreUpdateGores]),
        new(typeof(Main), "GoresEnd", "DoUpdateInWorld", 0, 0x114c,
            [0x2a1f03f5, 0xf94003a0, 0xb940e008, 0x35000068], [HookId.PostUpdateGores]),
        new(typeof(Main), "ItemsBegin", "DoUpdateInWorld", 0, 0x1740,
            [0xb00119f7, 0xf0011a18, 0xb0011af9, 0xb0011cfc], [HookId.PostUpdateProjectiles, HookId.PreUpdateItems]),
        new(typeof(Main), "ItemsEnd", "DoUpdateInWorld", 0, 0x1a14,
            [0xd0011b08, 0xf9447d08, 0xb0011b97, 0xf0011c16], [HookId.PostUpdateItems]),
        new(typeof(Main), "Transform", "DoDraw", 1, 0x2468,
            [0x97fc20b1, 0x37000220, 0xf9400360, 0xb940e008], [HookId.ModifyTransformMatrix]),
        new(typeof(Main), "ClockAdvance", "UpdateTime", 0, 0xb8c,
            [0x1e602960, 0xfd024500, 0x97efb520, 0xf94002e0], [HookId.ModifyTimeRate]),
        new(typeof(Player), "DyeReward", "GetDyeTraderReward", 1, 0xef0,
            [0xb9401aa1, 0x9000fe16, 0xd000fd78, 0xf947d2d6], [], HookId.GetDyeTraderReward),
        new(typeof(Player), "NaturalRegen", "UpdateLifeRegen", 0, 0x828,
            [0xbd481e60, 0xd000b709, 0xf9400300, 0xd000b70a], [], HookId.NaturalLifeRegen),
    ];

    private static double _clockRate = 1, _tileRemainder, _eventRemainder;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Build(IModLog log)
    {
        var hookAvailable = HookNative.IsAvailable() != 0;
        if (!hookAvailable)
            log.Info("[system] ShadowHook 不可用：入口钩与 UpdateTimeRate 跳过，中体桥照常安装");

        var installed = 0;
        var names = new List<string>();

        for (var i = 0; i < Stages.Length; i++)
        {
            var stage = Stages[i];
            if (stage.Installed) continue;
            if (!Gated(stage)) continue;

            try
            {
                var klass = MethodCode.KlassOf(stage.Class);
                if (!MethodCode.TryResolve(klass, stage.Method, stage.Argc, out var body, out _))
                {
                    log.Warn($"[system] {stage.Name}：{stage.Method} 没解析到，跳过");
                    continue;
                }

                if (!TryInstallStub(stage, i, body, log))
                {
                    log.Warn($"[system] {stage.Name}: C# stub install failed, stage skipped");
                    continue;
                }
                stage.Installed = true;
                installed++;
                names.Add(stage.Name);
                if (i == ClockAdvance && hookAvailable)
                    On.Terraria.Main.UpdateTimeRate += UpdateTimeRate;
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

    private static bool Gated(Stage stage)
    {
        if (stage.PlayerHook is { } playerHook)
            return PlayerPipeline.Instance.ModsActive(playerHook);
        foreach (var id in stage.Hooks)
        {
            if (SystemLoader.With(id).Length > 0) return true;
        }
        return false;
    }

    private static bool TryInstallStub(Stage stage, int index, IntPtr body, IModLog log)
    {
        var site = unchecked((ulong)body.ToInt64()) + (ulong)stage.Offset;
        if (!CodeImage.TryQuery(site, out var image, out var status) ||
            image.ImageBase == 0 || string.IsNullOrEmpty(image.BuildId))
        {
            log.Warn($"[system] {stage.Name}: image identity unavailable ({status})");
            return false;
        }
        if (site < image.ImageBase || site - image.ImageBase > uint.MaxValue)
        {
            log.Warn($"[system] {stage.Name}: site 0x{site:x} is outside the image");
            return false;
        }

        var fingerprint = new byte[16];
        for (var i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(fingerprint.AsSpan(i * 4, 4), stage.Expected[i]);
        var live = new byte[16];
        fixed (byte* destination = live)
        {
            if (Arm64Native.TryReadMemory(site, destination, 16, out _) != 0)
            {
                log.Warn($"[system] {stage.Name}: site 0x{site:x} is not readable");
                return false;
            }
        }
        for (var i = 0; i < fingerprint.Length; i++)
        {
            if (live[i] == fingerprint[i]) continue;
            log.Warn($"[system] {stage.Name}: 16-byte fingerprint mismatch at 0x{site + (uint)i:x}");
            return false;
        }

        var profile = new CodeSiteProfile(
            image.BuildId,
            "system-stub",
            site,
            (uint)(site - image.ImageBase),
            0,
            16,
            "SystemStages",
            stage.Name,
            Array.AsReadOnly(stage.Expected),
            Array.AsReadOnly(fingerprint));
        InterceptHandler handler = frame => StageIntercept(index, frame);
        if (!MidBodyIntercept.TryInstall(profile, image.BuildId, handler, true,
                out var interception, out var diagnostics, log))
        {
            log.Warn($"[system] {stage.Name}: MidBodyIntercept refused: " +
                     string.Join("; ", diagnostics));
            return false;
        }
        stage.Interception = interception;
        log.Info($"[system] {stage.Name}: stub site=0x{site:x} " +
                 $"strategy={interception!.RelocationStrategy}");
        return true;
    }

    private static void Dispatch(int stage, IntPtr value)
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

    private static bool StageIntercept(int stage, IntPtr frame)
    {
        try
        {
            if (System.Threading.Interlocked.Exchange(ref Stages[stage].FirstHit, 1) == 0)
                Log.Info($"[system] {Stages[stage].Name}: stub first dispatch");
            switch (stage)
            {
                case ClockAdvance:
                    var rate = InterceptFrame.GetDouble(frame, 0);
                    var before = rate;
                    Dispatch(stage, (IntPtr)(void*)&rate);
                    if (rate != before && double.IsFinite(rate) && rate >= 0)
                        InterceptFrame.SetDouble(frame, 0, rate);
                    break;
                case DyeReward:
                    var dye = new DyeFrame(
                        (IntPtr)unchecked((long)InterceptFrame.GetGpr(frame, 19)),
                        (IntPtr)unchecked((long)InterceptFrame.GetGpr(frame, 21)));
                    Dispatch(stage, (IntPtr)(void*)&dye);
                    break;
                case NaturalRegen:
                    var regen = BitConverter.UInt32BitsToSingle((uint)InterceptFrame.GetVectorUInt64(frame, 9, 0));
                    var regenBefore = regen;
                    DispatchNaturalRegen((IntPtr)unchecked((long)InterceptFrame.GetGpr(frame, 19)), ref regen);
                    if (regen != regenBefore && float.IsFinite(regen))
                        *(float*)((byte*)frame + InterceptFrame.VectorOffset + 9 * 16) = regen;
                    break;
                default:
                    Dispatch(stage, IntPtr.Zero);
                    break;
            }
        }
        catch (Exception e)
        {
            Safe.Once("system.stub", "[system] stub 阶段分派异常：" + Safe.Describe(e), true);
        }
        return false;
    }

    private static void Fan(HookId id)
    {
        switch (id)
        {
            case HookId.PreUpdatePlayers: SystemLoader.PreUpdatePlayers(); break;
            case HookId.PostUpdatePlayers: SystemLoader.PostUpdatePlayers(); break;
            case HookId.PreUpdateNPCs: SystemLoader.PreUpdateNPCs(); break;
            case HookId.PostUpdateNPCs: SystemLoader.PostUpdateNPCs(); break;
            case HookId.PreUpdateGores: SystemLoader.PreUpdateGores(); break;
            case HookId.PostUpdateGores: SystemLoader.PostUpdateGores(); break;
            case HookId.PostUpdateProjectiles: SystemLoader.PostUpdateProjectiles(); break;
            case HookId.PreUpdateItems: SystemLoader.PreUpdateItems(); break;
            case HookId.PostUpdateItems: SystemLoader.PostUpdateItems(); break;
        }
    }

    private static void ModifyTransform()
    {
        if (Main.gameMenu) return;

        var transform = Main.GameViewMatrix;
        SystemLoader.ModifyTransformMatrix(ref transform);
        Main.GameViewMatrix = transform;
    }

    private static void DispatchNaturalRegen(IntPtr player, ref float regen)
    {
        if (player == IntPtr.Zero || !PlayerPipeline.Instance.ModsActive(HookId.NaturalLifeRegen)) return;

        var self = new Terraria.Player(player);
        foreach (var mod in PlayerPipeline.Instance.ModsOf(self))
        {
            if (!mod.HookEnabled(HookId.NaturalLifeRegen)) continue;

            try
            {
                mod.NaturalLifeRegen(ref regen);
            }
            catch (Exception e)
            {
                Safe.Report($"{mod.Mod?.Name}/{mod.GetType().Name}.NaturalLifeRegen", e);
            }
        }
    }

    private const int DyePoolCapacity = 512;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DyeFrame(IntPtr player, IntPtr pool)
    {
        public readonly IntPtr Player = player;
        public readonly IntPtr Pool = pool;
    }

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

            if (frame.Player != IntPtr.Zero && PlayerPipeline.Instance.ModsActive(HookId.GetDyeTraderReward))
            {
                var player = new Terraria.Player(frame.Player);
                foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
                {
                    if (!mod.HookEnabled(HookId.GetDyeTraderReward)) continue;

                    try
                    {
                        mod.GetDyeTraderReward(pool);
                    }
                    catch (Exception e)
                    {
                        Safe.Report($"{mod.Mod?.Name}/{mod.GetType().Name}.GetDyeTraderReward", e);
                    }
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

    private static void UpdateTimeRate(On.Terraria.Main.orig_UpdateTimeRate orig)
    {
        orig();

        double time = Main.dayRate, tiles = Main.desiredWorldTilesUpdateRate, events = Main.dayRate;
        double initialTime = time, initialTiles = tiles;

        SystemLoader.ModifyTimeRate(ref time, ref tiles, ref events);

        _clockRate = Rate(time, initialTime);
        var tileRate = Rate(tiles, initialTiles) + _tileRemainder;
        var eventRate = Rate(events, initialTime) + _eventRemainder;
        Main.desiredWorldTilesUpdateRate = (int)Math.Floor(tileRate);
        Main.dayRate = (int)Math.Floor(eventRate);
        _tileRemainder = tileRate % 1;
        _eventRemainder = eventRate % 1;
    }

    private static double Rate(double value, double fallback) =>
        double.IsFinite(value) && value >= 0 && value <= int.MaxValue ? value : fallback;
}
