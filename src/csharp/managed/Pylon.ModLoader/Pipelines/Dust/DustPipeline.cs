using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Tables;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 尘土管线：mod 尘土真进 <c>Main.dust</c> 池，类型号从原版表长（运行期锚点，见 <see cref="DustTables.Vanilla"/>）起按注册序发。
/// 三张按类型索引的原生表（Dust.StaticAlpha/StaticAlphaValue、ChildSafety.SafeDust）随号段扩容，扩表必须先于任何 mod 号进原版：
/// Build/Refresh 都先 Grow 再 Assign，挂钩由门在扩表到位后才放行。
/// 原版循环负责物理与消亡：Update 派发在 <c>Dust.UpdateDust</c> 之前（返回 false 的尘土临时藏起，orig 后恢复）、
/// MidUpdate 在其后（返回 true 抵消本帧横向刹车）；本帧新生的尘土下一帧才开始派发。
/// 派发回调里写 <c>dust.active = false</c> 是 TML 惯用杀法，但手机版只有 <c>Dust.SetNotActive</c> 会把槽位还回 freeDust 栈，
/// 所以三处派发（Update / MidUpdate / PreDraw）都包 kill tracking：记 freeDustCount 最低点，栈里找不到的死尘土补一次 SetNotActive。
/// 绘制走中间态：orig 前把 mod 尘土整体藏匿（active 翻转，原版 pass 画不到），orig 后恢复并用自己的批次只画 mod 尘土。
/// Update 抛异常按 D9 记一条并 SetNotActive 回收；NaN/Inf 数值卫生守卫位置不变（只对要跑物理的尘土检查）。
/// 运行期指纹（锚点长度一致且大于 0、Main.dust ≥6000 槽、NewDust 九参签名存在）不符即停用池路径并报错，此时 Spawn 一律返回 null。
/// </summary>
internal sealed class DustPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static DustPipeline Instance { get; } = new();

    public string Name => "dust";

    private static IModLog Log => LoaderHost.Current.Log;

    private const int PoolSlots = 6000;

    private const int SheetFrameHeight = 30;

    public IReadOnlyList<TableRequest> Tables => [new(DustTables.Set, () => DustTable.Total, early: true)];

    /// <summary>dust 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal struct Loop : IGate
        {
            public static bool Wanted() =>
                ModContent.GetContent<ModDust>().Count > 0 && DustPipeline.Fingerprint(false) && DustPipeline.Grown();

            public static void Install()
            {
                On.Terraria.Dust.NewDust += NewDust;
                On.Terraria.Dust.UpdateDust += UpdateDust;
                On.Terraria.Main.DrawDust += DrawDust;
                Log.Info("[dust] 已挂（mod 尘土进 Main.dust 池：NewDust / UpdateDust / DrawDust）");
            }
        }
    }

    public bool Install() => GateHub<DustPipeline>.Run();

    public void Refresh()
    {
        Grow();
        DustTable.Assign();
    }

    internal void Build()
    {
        if (!Fingerprint(true)) return;

        Grow();
        DustTable.Assign();

        foreach (var dust in DustTable.All) dust.SetStaticDefaults();

        if (DustTable.Count > 0)
            Log.Info($"[dust] 安装：{DustTable.Count} 种尘土（类型 {DustTable.Base}..{DustTable.Total - 1}）");

        GateHub<DustPipeline>.Run();
    }

    private static void Grow()
    {
        if (DustTable.Base <= 0) return;

        var target = DustTable.Base + ModContent.GetContent<ModDust>().Count;
        if (target <= DustTable.Base) return;

        try
        {
            TableMaintenance.Ensure(DustTables.Set, target).GrowAll(Log.Info);
        }
        catch (Exception e)
        {
            Safe.Report("dust.grow", e);
        }
    }

    private static bool Grown()
    {
        if (TableMaintenance.Find(DustTables.Set) is not { } grower || grower.Vanilla <= 0) return false;
        if (!TableMaintenance.Audit(DustTables.Set, out var total, out var grown, out var stale, out var missing)) return false;
        return total > 0 && stale == 0 && missing == 0 && grown == total;
    }

    private static bool _reported;
    private static bool _newDustVerified;

    private static bool Fingerprint(bool report)
    {
        if (DustTable.Base <= 0) return Stop(report, "尘土表长锚点未捕获（SafeDust/StaticAlpha 长度为 0 或不一致）");

        var dusts = Main.dust;
        if (dusts is null || dusts.Length < PoolSlots)
            return Stop(report, $"Main.dust 槽位不足（{(dusts is null ? "未建" : dusts.Length.ToString())}，需要 ≥{PoolSlots}）");

        if (!_newDustVerified)
        {
            var klass = Il2CppClassPointerStore<Terraria.Dust>.NativeClassPtr;
            if (klass == IntPtr.Zero || IL2CPP.il2cpp_class_get_method_from_name(klass, "NewDust", 9) == IntPtr.Zero)
                return Stop(report, "Dust.NewDust 九参签名不存在");
            _newDustVerified = true;
        }

        return true;
    }

    private static bool Stop(bool report, string reason)
    {
        if (report && !_reported)
        {
            _reported = true;
            ContentErrors.Report("[dust] 池路径停用：" + reason);
        }
        return false;
    }

    public static Dust? Spawn(ModDust mod, Vector2 position, Vector2 velocity, int alpha = 0, Color? color = null, float scale = 1f)
    {
        if (mod is null || !mod.Registered || !Fingerprint(true) || !Grown()) return null;

        int index;
        try
        {
            index = Terraria.Dust.NewDust(position, 0, 0, mod.Type, velocity.X, velocity.Y, alpha, color ?? default, scale);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[dust] 生成 {mod.Name} 失败：{Safe.Describe(e)}");
            return null;
        }

        var dusts = Main.dust;
        if (dusts is null || (uint)index >= (uint)dusts.Length) return null;

        return dusts[index];
    }

    private static bool _tracking;
    private static int _minFree;
    private static bool[] _marked = [];

    private static readonly List<Dust> _mods = [];
    private static readonly List<Dust> _list = [];
    private static readonly List<Dust> _hidden = [];
    private static readonly List<bool> _flags = [];

    private static void SyncMarks(Il2CppReferenceArray<Dust> dusts)
    {
        if (_marked.Length == dusts.Length) return;
        _marked = new bool[dusts.Length];
    }

    private static void ScanMarked(Il2CppReferenceArray<Dust> dusts, List<Dust> result)
    {
        var limit = Math.Min(_marked.Length, dusts.Length);
        for (var i = 0; i < limit; i++)
        {
            if (!_marked[i]) continue;

            var dust = dusts[i];
            if (dust is not null && dust.active && DustTable.Of(dust.type) is not null) result.Add(dust);
            else _marked[i] = false;
        }
    }

    private static void ResetFlags(int count, bool value)
    {
        _flags.Clear();
        for (var i = 0; i < count; i++) _flags.Add(value);
    }

    private static void BeginKillTracking()
    {
        _minFree = Dust.freeDustCount;
        _tracking = true;
    }

    private static void EndKillTracking(List<Dust> list)
    {
        _tracking = false;

        var free = Dust.freeDust;
        if (free is null) return;

        var now = Dust.freeDustCount;
        var lo = _minFree < 0 ? 0 : _minFree;
        var hi = Math.Min(now, free.Length - 1);

        for (var i = 0; i < list.Count; i++)
        {
            var dust = list[i];
            if (dust is null || dust.active) continue;

            var returned = false;
            for (var k = lo + 1; k <= hi && !returned; k++)
            {
                var slot = free[k];
                returned = slot is not null && slot.Pointer == dust.Pointer;
            }
            if (returned) continue;

            dust.active = true;
            Dust.SetNotActive(dust);
        }
    }

    private static int NewDust(On.Terraria.Dust.orig_NewDust orig, Vector2 position, int width, int height, int type,
        float speedX, float speedY, int alpha, Color newColor, float scale)
    {
        var index = orig(position, width, height, type, speedX, speedY, alpha, newColor, scale);

        if (_tracking)
        {
            var free = Dust.freeDustCount;
            if (free < _minFree) _minFree = free;
        }

        if (type < DustTable.Base) return index;

        var dusts = Main.dust;
        if (dusts is null || (uint)index >= (uint)dusts.Length) return index;

        SyncMarks(dusts);
        if (index >= _marked.Length) return index;

        var dust = dusts[index];
        var mod = dust is { active: true } ? DustTable.Of(dust.type) : null;
        if (mod is null) return index;

        _marked[index] = true;

        var frame = dust.frame;
        frame.X = 0;
        frame.Y %= SheetFrameHeight;
        dust.frame = frame;

        if (mod.HookEnabled(HookId.OnSpawn))
        {
            try
            {
                mod.OnSpawn(dust);
            }
            catch (Exception e)
            {
                ContentErrors.Callback($"[dust] {mod.Name}.OnSpawn", e);
            }
        }

        return index;
    }

    private static void UpdateDust(On.Terraria.Dust.orig_UpdateDust orig)
    {
        var dusts = Main.dust;
        if (dusts is null || dusts.Length < PoolSlots)
        {
            orig();
            return;
        }

        SyncMarks(dusts);
        _mods.Clear();
        ScanMarked(dusts, _mods);
        if (_mods.Count == 0)
        {
            orig();
            return;
        }

        _list.Clear();
        for (var i = 0; i < _mods.Count; i++)
        {
            var dust = _mods[i];
            if (DustTable.Of(dust.type) is { } mod && mod.HookEnabled(HookId.Update)) _list.Add(dust);
        }

        if (_list.Count > 0)
        {
            ResetFlags(_list.Count, true);
            BeginKillTracking();
            for (var i = 0; i < _list.Count; i++)
            {
                var dust = _list[i];
                var mod = DustTable.Of(dust.type);
                if (mod is null) continue;

                bool run;
                try
                {
                    run = mod.Update(dust);
                }
                catch (Exception e)
                {
                    ContentErrors.Callback($"[dust] {mod.Name}.Update", e);
                    Dust.SetNotActive(dust);
                    continue;
                }

                if (!dust.active) continue;

                if (!run)
                {
                    _flags[i] = false;
                    continue;
                }

                if (!IsFinite(dust)) Dust.SetNotActive(dust);
            }
            EndKillTracking(_list);
        }

        _hidden.Clear();
        var next = 0;
        for (var i = 0; i < _mods.Count; i++)
        {
            var dust = _mods[i];
            var vanilla = true;
            if (next < _list.Count && ReferenceEquals(_list[next], dust)) vanilla = _flags[next++];
            if (!dust.active) continue;
            if (DustTable.Of(dust.type) is null || vanilla) continue;
            dust.active = false;
            _hidden.Add(dust);
        }

        orig();

        for (var i = 0; i < _hidden.Count; i++) _hidden[i].active = true;

        _list.Clear();
        for (var i = 0; i < _mods.Count; i++)
        {
            var dust = _mods[i];
            if (dust.active && DustTable.Of(dust.type) is { } mod && mod.HookEnabled(HookId.MidUpdate)) _list.Add(dust);
        }

        if (_list.Count > 0)
        {
            ResetFlags(_list.Count, false);
            BeginKillTracking();
            for (var i = 0; i < _list.Count; i++)
            {
                var dust = _list[i];
                var mod = DustTable.Of(dust.type);
                if (mod is null) continue;

                try
                {
                    _flags[i] = mod.MidUpdate(dust);
                }
                catch (Exception e)
                {
                    ContentErrors.Callback($"[dust] {mod.Name}.MidUpdate", e);
                    _flags[i] = false;
                }
            }
            EndKillTracking(_list);

            for (var i = 0; i < _list.Count; i++)
            {
                var dust = _list[i];
                if (!_flags[i] || !dust.active || DustTable.Of(dust.type) is null) continue;

                var velocity = dust.velocity;
                velocity.X /= 0.99f;
                dust.velocity = velocity;
            }
        }
    }

    private static bool IsFinite(Dust dust) =>
        float.IsFinite(dust.position.X) && float.IsFinite(dust.position.Y) &&
        float.IsFinite(dust.velocity.X) && float.IsFinite(dust.velocity.Y) &&
        float.IsFinite(dust.scale);

    private static void DrawDust(On.Terraria.Main.orig_DrawDust orig, Main self)
    {
        var dusts = Main.dust;
        if (dusts is null || dusts.Length < PoolSlots)
        {
            orig(self);
            return;
        }

        SyncMarks(dusts);
        _mods.Clear();
        ScanMarked(dusts, _mods);
        if (_mods.Count == 0)
        {
            orig(self);
            return;
        }

        _hidden.Clear();
        for (var i = 0; i < _mods.Count; i++)
        {
            var dust = _mods[i];
            if (!dust.active || DustTable.Of(dust.type) is null) continue;
            dust.active = false;
            _hidden.Add(dust);
        }

        orig(self);

        for (var i = 0; i < _hidden.Count; i++) _hidden[i].active = true;

        DrawMods();
    }

    private static bool _drawLogged;

    private static void DrawMods()
    {
        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        try
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, null, null,
                new Il2CppSystem.Nullable<Matrix>(), true);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[dust] 批次 Begin 异常：" + Safe.Describe(e));
            return;
        }

        try
        {
            if (!_drawLogged && _mods.Count > 0)
            {
                _drawLogged = true;
                var first = _mods[0];
                Log.Info($"[dust] 首次绘制：{_mods.Count} 颗；screen={Main.screenPosition}；屏幕={Main.screenWidth}x{Main.screenHeight}；第一颗 world={first.position} scale={first.scale} color={first.color}");
            }

            _list.Clear();
            for (var i = 0; i < _mods.Count; i++)
            {
                var dust = _mods[i];
                if (dust.active && DustTable.Of(dust.type) is { } mod && mod.HookEnabled(HookId.PreDraw)) _list.Add(dust);
            }

            if (_list.Count > 0)
            {
                ResetFlags(_list.Count, true);
                BeginKillTracking();
                for (var i = 0; i < _list.Count; i++)
                {
                    var dust = _list[i];
                    var mod = DustTable.Of(dust.type);
                    if (mod is null) continue;

                    try
                    {
                        _flags[i] = mod.PreDraw(dust);
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Callback($"[dust] {mod.Name}.PreDraw", e);
                        _flags[i] = true;
                    }
                }
                EndKillTracking(_list);
            }

            List<Dust>? skipped = null;
            for (var i = 0; i < _list.Count; i++)
            {
                if (_flags[i]) continue;
                skipped ??= [];
                skipped.Add(_list[i]);
            }

            for (var i = 0; i < _mods.Count; i++)
            {
                var dust = _mods[i];
                var mod = DustTable.Of(dust.type);
                if (mod is null || !dust.active) continue;
                if (skipped is not null && skipped.Contains(dust)) continue;

                Texture2D? texture;
                try
                {
                    texture = mod.Texture?.Value;
                }
                catch (Exception e)
                {
                    ContentErrors.Callback($"[dust] {mod.Name}.Texture", e);
                    continue;
                }

                if (texture is null || texture.Pointer == IntPtr.Zero) continue;

                var color = dust.color;
                if (mod.HookEnabled(HookId.GetAlpha))
                {
                    try
                    {
                        if (mod.GetAlpha(dust, color) is { } mine) color = mine;
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Callback($"[dust] {mod.Name}.GetAlpha", e);
                    }
                }

                try
                {
                    var frame = new Il2CppSystem.Nullable<Rectangle>(new Rectangle(0, 0, texture.Width, texture.Height));
                    var origin = new Vector2(texture.Width / 2f, texture.Height / 2f);

                    spriteBatch.Draw(texture, dust.position - Main.screenPosition, frame, color, dust.rotation, origin,
                        dust.scale, SpriteEffects.None, 0f);
                }
                catch (Exception e)
                {
                    var stack = e.StackTrace;
                    var at = string.IsNullOrEmpty(stack) ? "" : " | " + stack.Split('\n')[0].Trim();
                    ContentErrors.Report("[dust] 绘制异常：" + Safe.Describe(e) + at);
                }
            }
        }
        finally
        {
            try { spriteBatch.End(); } catch { }
        }
    }
}
