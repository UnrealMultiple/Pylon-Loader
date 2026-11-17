using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.NaviteAPI.Host;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>城镇攻击八件套与全局攻防加成（BuffTownNPC）：入口钩够不到 AI_007 攻击段，全部走中体桥（指纹校验 + MidBodyIntercept）改参放行；桥只装给声明钩子的 mod 城镇 NPC，BuffTownNPC 全局时全体安装。</summary>
internal static unsafe class NpcTownAttackHooks
{
    private const int ThrowAttack = 0;
    private const int ShootAttack = 1;
    private const int MagicAttack = 2;
    private const int SwingAttack = 3;
    private const int ThrowRandom = 4;
    private const int ShootRandom = 5;
    private const int MagicRandom = 6;
    private const int AuraLight = 7;
    private const int SwingStats = 8;
    private const int SwingRect = 9;
    private const int GunDraw = 10;
    private const int SwingDraw = 11;

    private sealed class Site(System.Type klass, string method, int argc, int offset,
                              uint[] expected, int kind)
    {
        public System.Type Class => klass;
        public string Method => method;
        public int Argc => argc;
        public int Offset => offset;
        public uint[] Expected => expected;
        public int Kind => kind;
        public bool Installed;
    }

    private static readonly Site[] Sites =
    [
        new(typeof(NPC), "AI_007_TownEntities", 0, 0x69e4,
            [0x1e220100, 0x1e200980, 0xaa1303e0, 0xaa1f03e1], ThrowAttack),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xac6c,
            [0x1e220100, 0x1e200980, 0xaa1303e0, 0xaa1f03e1], ShootAttack),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xb4dc,
            [0x1e220100, 0x1e200980, 0xaa1303e0, 0xaa1f03e1], MagicAttack),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xbb0c,
            [0x1e220100, 0x1e200980, 0xaa1303e0, 0xaa1f03e1], SwingAttack),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0x6d90,
            [0x9415f207, 0x4ea01c02, 0x4ea11c23, 0x4ea91d20], ThrowRandom),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xaf34,
            [0x9415e19e, 0x4ea01c02, 0x4ea11c23, 0x4eaa1d40], ShootRandom),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xb864,
            [0x9415df52, 0x4ea01c02, 0x4ea11c23, 0x4ea91d20], MagicRandom),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xcb50,
            [0x97ff1ce8, 0xf94057e8, 0xf9400114, 0xb4000934], AuraLight),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xbbe8,
            [0x9404faff, 0xb40084a0, 0xf00111e8, 0xf944ed08], SwingStats),
        new(typeof(NPC), "AI_007_TownEntities", 0, 0xbc48,
            [0xb9421a68, 0x3100051f, 0x54000081, 0xb9413be8], SwingRect),
        new(typeof(Main), "DrawNPCExtras", 8, 0xf94,
            [0x4b1303e8, 0x710002bf, 0x1e220100, 0x1a95a6a8], GunDraw),
        new(typeof(Main), "DrawNPCExtras", 8, 0x1ccc,
            [0xf9400360, 0xb940e008, 0x35000068, 0x97d9ee7b], SwingDraw),
    ];

    private static bool _attackGroupNeeded;
    private static bool _drawGroupNeeded;
    private static bool _buffTownNeeded;
    private static bool _allAttack;
    private static uint[] _wanted = [];

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Install()
    {
        _npcClass = MethodCode.KlassOf(typeof(NPC));
        var typeField = _npcClass != IntPtr.Zero ? IL2CPP.il2cpp_class_get_field_from_name(_npcClass, "type") : IntPtr.Zero;
        _npcTypeOffset = typeField != IntPtr.Zero ? checked((int)IL2CPP.il2cpp_field_get_offset(typeField)) : -1;
        if (_npcTypeOffset < 0)
            Log.Warn("[townattack] NPC.type 字段偏移没解析到；全部桥保持惰性（不分派）");

        RebuildWanted();
    }

    private static bool _subscribed;

    private static void SubscribeAttack()
    {
        if (_subscribed || !(_attackGroupNeeded || _buffTownNeeded)) return;
        On.Terraria.NPC.AI_007_TownEntities += TownEntities;
        _subscribed = true;
    }

    private static void InstallSites()
    {
        var installed = 0;
        foreach (var site in Sites)
        {
            if (site.Installed) continue;
            if (!Needed(site.Kind)) continue;

            try
            {
                var klass = MethodCode.KlassOf(site.Class);
                if (!MethodCode.TryResolve(klass, site.Method, site.Argc, out var body, out _))
                {
                    Log.Warn($"[townattack] {site.Method}+0x{site.Offset:x}：方法没解析到，跳过");
                    continue;
                }
                if (TryInstallStub(site, body))
                {
                    site.Installed = true;
                    installed++;
                }
                else
                {
                    Log.Warn($"[townattack] {site.Kind} 桥安装失败（指纹不匹配或桩被拒），点跳过");
                }
            }
            catch (Exception e)
            {
                Log.Warn($"[townattack] {site.Kind} 安装异常：" + Safe.Describe(e));
            }
        }

        if (installed > 0)
            Log.Info($"[townattack] 已挂（{installed} 桥：攻击={_attackGroupNeeded || _buffTownNeeded} 绘制={_drawGroupNeeded} 全局加成={_buffTownNeeded}）");
        else if ((_attackGroupNeeded || _drawGroupNeeded || _buffTownNeeded) && !_warnedEmpty)
        {
            _warnedEmpty = true;
            Log.Warn("[townattack] 需要桥但一个都没装上（构建指纹不匹配？）");
        }
    }

    private static bool _warnedEmpty;

    internal static void RebuildWanted()
    {
        var attack = false;
        var draw = false;
        var maxType = VanillaCounts.Npc;
        var wanted = new uint[maxType + 64];

        foreach (var npc in ModContent.GetContent<ModNpc>())
        {
            if (!npc.Registered || npc.Type <= 0) continue;
            uint mask = 0;
            if (npc.Hooks.Has(HookId.TownNPCAttackStrength) || npc.Hooks.Has(HookId.TownNPCAttackCooldown) ||
                npc.Hooks.Has(HookId.TownNPCAttackProj) || npc.Hooks.Has(HookId.TownNPCAttackProjSpeed) ||
                npc.Hooks.Has(HookId.TownNPCAttackShoot) || npc.Hooks.Has(HookId.TownNPCAttackMagic) ||
                npc.Hooks.Has(HookId.TownNPCAttackSwing))
                mask |= 1;
            if (npc.Hooks.Has(HookId.DrawTownAttackGun) || npc.Hooks.Has(HookId.DrawTownAttackSwing))
                mask |= 2;
            if (mask == 0) continue;

            if (npc.Type >= wanted.Length) Array.Resize(ref wanted, npc.Type + 64);
            wanted[npc.Type] = mask;
            attack |= (mask & 1) != 0;
            draw |= (mask & 2) != 0;
            if (npc.Type + 1 > maxType) maxType = npc.Type + 1;
        }

        _wanted = wanted;
        _attackGroupNeeded = attack;
        _drawGroupNeeded = draw;
        _buffTownNeeded = GlobalNpcRegistry.AnyWith(HookId.BuffTownNPC);
        _allAttack = _buffTownNeeded;

        SubscribeAttack();
        InstallSites();
    }

    private static bool Needed(int kind) => kind switch
    {
        GunDraw or SwingDraw => _drawGroupNeeded,
        _ => _attackGroupNeeded || _buffTownNeeded,
    };

    private static bool TryInstallStub(Site site, IntPtr body)
    {
        var siteAddress = unchecked((ulong)body.ToInt64() + (ulong)site.Offset);
        if (!CodeImage.TryQuery(siteAddress, out var image, out var status) ||
            image.ImageBase == 0 || string.IsNullOrEmpty(image.BuildId))
        {
            Log.Warn($"[townattack] {site.Kind}: image identity unavailable ({status})");
            return false;
        }

        var fingerprint = new byte[16];
        for (var i = 0; i < 4; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(fingerprint.AsSpan(i * 4, 4), site.Expected[i]);
        var live = new byte[16];
        fixed (byte* destination = live)
        {
            if (Arm64Native.TryReadMemory(siteAddress, destination, 16, out _) != 0) return false;
        }
        for (var i = 0; i < fingerprint.Length; i++)
        {
            if (live[i] != fingerprint[i])
            {
                Log.Warn($"[townattack] {site.Kind}: fingerprint mismatch at 0x{siteAddress + (uint)i:x}");
                return false;
            }
        }

        var profile = new CodeSiteProfile(
            image.BuildId,
            "townattack-stub",
            siteAddress,
            (uint)(siteAddress - image.ImageBase),
            0,
            16,
            "NpcTownAttackHooks",
            site.Kind.ToString(),
            Array.AsReadOnly(site.Expected),
            Array.AsReadOnly(fingerprint));
        InterceptHandler handler = frame => Intercept(site.Kind, frame);
        if (!MidBodyIntercept.TryInstall(profile, image.BuildId, handler, true,
                out var interception, out var diagnostics, Log))
        {
            Log.Warn($"[townattack] {site.Kind}: MidBodyIntercept refused: " + string.Join("; ", diagnostics));
            return false;
        }
        Log.Info($"[townattack] {site.Kind}: stub site=0x{siteAddress:x} strategy={interception!.RelocationStrategy}");
        return true;
    }

    private sealed class Scope
    {
        public IntPtr Npc;
        public int Mode = -1;
        public AttackArgs Args;
    }

    [ThreadStatic] private static Scope? _scopeStorage;
    [ThreadStatic] private static Scope? _scope;

    private struct AttackArgs
    {
        public int Damage;
        public float Knockback;
        public int Cooldown, Extra, Projectile, Delay;
        public float Speed, Gravity, Random, Aura;
        public int Width, Height, InBetween;
    }

    private static void TownEntities(On.Terraria.NPC.orig_AI_007_TownEntities orig, NPC self)
    {
        var scoped = Wanted(self.type, 1);
        var outer = _scope;
        if (scoped)
        {
            _scopeStorage ??= new Scope();
            _scopeStorage.Npc = self.Pointer;
            _scopeStorage.Mode = -1;
            _scopeStorage.Args = default;
            _scope = _scopeStorage;
        }
        try
        {
            orig(self);
        }
        finally
        {
            _scope = outer;
        }

        if (_buffTownNeeded)
        {
            float damage = 1f;
            int defense = 0;
            foreach (var global in GlobalNpcRegistry.For(self, HookId.BuffTownNPC))
            {
                try
                {
                    global.BuffTownNPC(self, ref damage, ref defense);
                }
                catch (Exception e)
                {
                    Report(global, HookId.BuffTownNPC, e);
                }
            }
            self.defense += defense;
            if ((uint)self.whoAmI < 200u) _townDamage[self.whoAmI] = damage;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Wanted(int type, uint flag) =>
        (WantedFlags(type) & flag) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint WantedFlags(int type)
    {
        var table = _wanted;
        var flags = (uint)type < (uint)table.Length ? table[type] : 0;
        return flags | (_allAttack ? 1u : 0u);
    }

    private static bool Intercept(int kind, IntPtr frame)
    {
        try
        {
            if (System.Threading.Interlocked.Exchange(ref _hits[kind], 1) == 0)
                Log.Info($"[townattack] 桥 {kind} 首次命中");
            switch (kind)
            {
                case ThrowAttack: Attack(frame, 0); break;
                case ShootAttack: Attack(frame, 1); break;
                case MagicAttack: Attack(frame, 2); break;
                case SwingAttack: Attack(frame, 3); break;
                case ThrowRandom or ShootRandom or MagicRandom: RandomSpread(frame); break;
                case AuraLight: Aura(frame); break;
                case SwingStats: SwingSize(frame); break;
                case SwingRect: SwingHitbox(frame); break;
                case GunDraw: Draw(frame, 4); break;
                case SwingDraw: Draw(frame, 5); break;
            }
        }
        catch (Exception e)
        {
            Safe.Once("townattack.stub", "[townattack] 桥分派异常：" + Safe.Describe(e), true);
        }
        return false;
    }

    private static readonly int[] _hits = new int[Sites.Length];

    private static int Gpr(IntPtr frame, int index) => (int)InterceptFrame.GetGpr(frame, index);
    private static void SetGpr(IntPtr frame, int index, int value) =>
        InterceptFrame.SetGpr(frame, index, (uint)value);

    private static float Scalar(IntPtr frame, int index) =>
        BitConverter.UInt32BitsToSingle((uint)InterceptFrame.GetVectorUInt64(frame, index, 0));

    private static void SetScalar(IntPtr frame, int index, float value) =>
        *(float*)((byte*)frame + InterceptFrame.VectorOffset + index * 16) = value;

    private static float StackFloat(IntPtr frame, int offset) =>
        *(float*)((byte*)frame + InterceptFrame.FrameBytes + offset);

    private static void SetStackFloat(IntPtr frame, int offset, float value) =>
        *(float*)((byte*)frame + InterceptFrame.FrameBytes + offset) = value;

    private static int StackInt(IntPtr frame, int offset) =>
        *(int*)((byte*)frame + InterceptFrame.FrameBytes + offset);

    private static void SetStackInt(IntPtr frame, int offset, int value) =>
        *(int*)((byte*)frame + InterceptFrame.FrameBytes + offset) = value;

    private static IntPtr _npcClass;
    private static int _npcTypeOffset = -1;

    private static IntPtr NpcOf(IntPtr frame, int gpr)
    {
        var pointer = (IntPtr)InterceptFrame.GetGpr(frame, gpr);
        return pointer;
    }

    private static bool Wants(IntPtr frame, int gpr, uint flag)
    {
        var pointer = NpcOf(frame, gpr);
        if (pointer == IntPtr.Zero || _npcTypeOffset < 0) return false;
        var type = *(int*)((byte*)pointer + _npcTypeOffset);
        return Wanted(type, flag);
    }

    private static void Attack(IntPtr frame, int mode)
    {
        var scope = _scope;
        if (scope is null || scope.Npc != NpcOf(frame, 19) || !Wants(frame, 19, 1)) return;

        var args = new AttackArgs { Damage = Gpr(frame, 8), Aura = 1f };
        if (mode == 0)
        {
            args.Knockback = Scalar(frame, 8);
            args.Speed = Scalar(frame, 9);
            args.Cooldown = Gpr(frame, 21);
            args.Extra = Gpr(frame, 23);
            args.Projectile = Gpr(frame, 20);
            args.Delay = (int)Scalar(frame, 11);
            args.Gravity = StackFloat(frame, 0x30);
        }
        else if (mode == 1)
        {
            args.Knockback = Scalar(frame, 8);
            args.Speed = StackFloat(frame, 0x2c);
            args.Cooldown = Gpr(frame, 22);
            args.Extra = Gpr(frame, 23);
            args.Projectile = StackInt(frame, 0xa4);
            args.Delay = Gpr(frame, 20);
            args.Gravity = -Scalar(frame, 11);
            args.Random = Scalar(frame, 9);
            args.InBetween = Gpr(frame, 24) != 0 ? 1 : 0;
        }
        else if (mode == 2)
        {
            args.Knockback = StackFloat(frame, 0xa4);
            args.Speed = Scalar(frame, 9);
            args.Cooldown = StackInt(frame, 0x9c);
            args.Extra = StackInt(frame, 0x8c);
            args.Projectile = Gpr(frame, 27);
            args.Delay = Gpr(frame, 23);
            args.Gravity = Scalar(frame, 13);
            args.Random = Scalar(frame, 8);
        }
        else
        {
            args.Knockback = Scalar(frame, 11);
            args.Cooldown = StackInt(frame, 0xa4);
            args.Extra = StackInt(frame, 0x9c);
            args.Width = args.Height = Gpr(frame, 27);
        }

        var npc = new NPC(NpcOf(frame, 19));
        try
        {
            AttackEvent(npc, mode, ref args);
        }
        catch (Exception e)
        {
            Safe.Report("[townattack] 攻击参数分派", e);
        }

        args.Extra = Math.Max(1, args.Extra);
        args.Width = Math.Max(1, args.Width);
        args.Height = Math.Max(1, args.Height);
        if (!float.IsFinite(args.Random)) args.Random = 0;
        if (!float.IsFinite(args.Aura)) args.Aura = 1;
        scope.Mode = mode;
        scope.Args = args;

        SetGpr(frame, 8, args.Damage);
        if (mode == 0)
        {
            SetScalar(frame, 8, args.Knockback);
            SetScalar(frame, 9, args.Speed);
            SetScalar(frame, 11, args.Delay);
            SetGpr(frame, 21, args.Cooldown);
            SetGpr(frame, 23, args.Extra);
            SetGpr(frame, 20, args.Projectile);
            SetStackFloat(frame, 0x30, args.Gravity);
        }
        else if (mode == 1)
        {
            SetScalar(frame, 8, args.Knockback);
            SetStackFloat(frame, 0x2c, args.Speed);
            SetGpr(frame, 22, args.Cooldown);
            SetGpr(frame, 23, args.Extra);
            SetGpr(frame, 20, args.Delay);
            SetStackInt(frame, 0xa4, args.Projectile);
            SetScalar(frame, 11, -args.Gravity);
            SetScalar(frame, 9, args.Random);
            SetGpr(frame, 24, args.InBetween != 0 ? 1 : 0);
        }
        else if (mode == 2)
        {
            SetStackFloat(frame, 0xa4, args.Knockback);
            SetScalar(frame, 9, args.Speed);
            SetStackInt(frame, 0x9c, args.Cooldown);
            SetStackInt(frame, 0x8c, args.Extra);
            SetGpr(frame, 27, args.Projectile);
            SetGpr(frame, 23, args.Delay);
            SetScalar(frame, 13, args.Gravity);
            SetScalar(frame, 8, args.Random);
        }
        else
        {
            SetScalar(frame, 11, args.Knockback);
            SetStackInt(frame, 0xa4, args.Cooldown);
            SetStackInt(frame, 0x9c, args.Extra);
            SetGpr(frame, 27, args.Width);
        }
    }

    private static void AttackEvent(NPC npc, int mode, ref AttackArgs args)
    {
        args.Damage = (int)(args.Damage * TownDamage(npc));
        Strength(npc, ref args.Damage, ref args.Knockback);
        Cooldown(npc, ref args.Cooldown, ref args.Extra);
        if (mode == 3)
        {
            Swing(npc, ref args.Width, ref args.Height);
        }
        else
        {
            Proj(npc, ref args.Projectile, ref args.Delay);
            ProjSpeed(npc, ref args.Speed, ref args.Gravity, ref args.Random);
            if (mode == 1)
            {
                bool inBetween = args.InBetween != 0;
                Shoot(npc, ref inBetween);
                args.InBetween = inBetween ? 1 : 0;
            }
            if (mode == 2) Magic(npc, ref args.Aura);
        }
    }

    private static void RandomSpread(IntPtr frame)
    {
        var scope = _scope;
        if (scope is null) return;
        var random = scope.Args.Random;
        SetScalar(frame, 0, -random);
        SetScalar(frame, 1, random);
    }

    private static void Aura(IntPtr frame)
    {
        var scope = _scope;
        if (scope is null || scope.Mode != 2) return;
        var scale = scope.Args.Aura;
        SetScalar(frame, 2, Scalar(frame, 2) * scale);
        SetScalar(frame, 3, Scalar(frame, 3) * scale);
        SetScalar(frame, 4, Scalar(frame, 4) * scale);
    }

    private static void SwingSize(IntPtr frame)
    {
        var scope = _scope;
        if (scope is null || scope.Mode != 3) return;
        if (NpcOf(frame, 0) != scope.Npc) return;
        SetGpr(frame, 4, scope.Args.Width);
        SetGpr(frame, 5, scope.Args.Height);
    }

    private static void SwingHitbox(IntPtr frame)
    {
        var scope = _scope;
        if (scope is null || scope.Mode != 3) return;
        SetStackInt(frame, 0x13c, StackInt(frame, 0x13c) - (scope.Args.Height - scope.Args.Width));
        SetStackInt(frame, 0x144, scope.Args.Height);
    }

    private static void Draw(IntPtr frame, int mode)
    {
        if (!Wants(frame, 20, 2)) return;

        var stack = (byte*)frame + InterceptFrame.FrameBytes;
        var texOffset = mode == 4 ? 0x1e8 : 0x1c8;
        var rectOffset = mode == 4 ? 0x1d8 : 0x1b8;

        var texturePointer = *(IntPtr*)(stack + texOffset);
        var frameRect = new Rectangle(*(int*)(stack + rectOffset), *(int*)(stack + rectOffset + 4),
            *(int*)(stack + rectOffset + 8), *(int*)(stack + rectOffset + 12));
        var scale = Scalar(frame, mode == 4 ? 13 : 10);
        var size = Gpr(frame, mode == 4 ? 19 : 23);
        var offsetX = Scalar(frame, 8);
        var offsetY = Scalar(frame, 9);

        var npc = new NPC(NpcOf(frame, 20));
        var item = texturePointer != IntPtr.Zero ? new Texture2D(texturePointer) : null;
        if (item is not null)
        {
            try
            {
                if (mode == 4) DrawGun(npc, ref item, ref frameRect, ref scale, ref size);
                else DrawSwing(npc, ref item, ref frameRect, ref size, ref scale, ref offsetX, ref offsetY);
            }
            catch (Exception e)
            {
                Safe.Report("[townattack] 绘制分派", e);
            }
        }

        *(IntPtr*)(stack + texOffset) = item is null ? texturePointer : item.Pointer;
        *(int*)(stack + rectOffset) = frameRect.X;
        *(int*)(stack + rectOffset + 4) = frameRect.Y;
        *(int*)(stack + rectOffset + 8) = frameRect.Width;
        *(int*)(stack + rectOffset + 12) = frameRect.Height;
        SetScalar(frame, mode == 4 ? 13 : 10, scale);
        SetGpr(frame, mode == 4 ? 19 : 23, size);
        if (mode == 5)
        {
            SetScalar(frame, 8, offsetX);
            SetScalar(frame, 9, offsetY);
        }
    }

    private static void Strength(NPC npc, ref int damage, ref float knockback)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.TownNPCAttackStrength))
        {
            try { mod.TownNPCAttackStrength(ref damage, ref knockback); }
            catch (Exception e) { Report(mod, HookId.TownNPCAttackStrength, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.TownNPCAttackStrength))
        {
            try { global.TownNPCAttackStrength(npc, ref damage, ref knockback); }
            catch (Exception e) { Report(global, HookId.TownNPCAttackStrength, e); }
        }
    }

    private static void Cooldown(NPC npc, ref int cooldown, ref int extra)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.TownNPCAttackCooldown))
        {
            try { mod.TownNPCAttackCooldown(ref cooldown, ref extra); }
            catch (Exception e) { Report(mod, HookId.TownNPCAttackCooldown, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.TownNPCAttackCooldown))
        {
            try { global.TownNPCAttackCooldown(npc, ref cooldown, ref extra); }
            catch (Exception e) { Report(global, HookId.TownNPCAttackCooldown, e); }
        }
    }

    private static void Proj(NPC npc, ref int projType, ref int delay)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.TownNPCAttackProj))
        {
            try { mod.TownNPCAttackProj(ref projType, ref delay); }
            catch (Exception e) { Report(mod, HookId.TownNPCAttackProj, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.TownNPCAttackProj))
        {
            try { global.TownNPCAttackProj(npc, ref projType, ref delay); }
            catch (Exception e) { Report(global, HookId.TownNPCAttackProj, e); }
        }
    }

    private static void ProjSpeed(NPC npc, ref float multiplier, ref float gravity, ref float random)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.TownNPCAttackProjSpeed))
        {
            try { mod.TownNPCAttackProjSpeed(ref multiplier, ref gravity, ref random); }
            catch (Exception e) { Report(mod, HookId.TownNPCAttackProjSpeed, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.TownNPCAttackProjSpeed))
        {
            try { global.TownNPCAttackProjSpeed(npc, ref multiplier, ref gravity, ref random); }
            catch (Exception e) { Report(global, HookId.TownNPCAttackProjSpeed, e); }
        }
    }

    private static void Shoot(NPC npc, ref bool inBetween)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.TownNPCAttackShoot))
        {
            try { mod.TownNPCAttackShoot(ref inBetween); }
            catch (Exception e) { Report(mod, HookId.TownNPCAttackShoot, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.TownNPCAttackShoot))
        {
            try { global.TownNPCAttackShoot(npc, ref inBetween); }
            catch (Exception e) { Report(global, HookId.TownNPCAttackShoot, e); }
        }
    }

    private static void Magic(NPC npc, ref float aura)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.TownNPCAttackMagic))
        {
            try { mod.TownNPCAttackMagic(ref aura); }
            catch (Exception e) { Report(mod, HookId.TownNPCAttackMagic, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.TownNPCAttackMagic))
        {
            try { global.TownNPCAttackMagic(npc, ref aura); }
            catch (Exception e) { Report(global, HookId.TownNPCAttackMagic, e); }
        }
    }

    private static void Swing(NPC npc, ref int width, ref int height)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.TownNPCAttackSwing))
        {
            try { mod.TownNPCAttackSwing(ref width, ref height); }
            catch (Exception e) { Report(mod, HookId.TownNPCAttackSwing, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.TownNPCAttackSwing))
        {
            try { global.TownNPCAttackSwing(npc, ref width, ref height); }
            catch (Exception e) { Report(global, HookId.TownNPCAttackSwing, e); }
        }
    }

    private static void DrawGun(NPC npc, ref Texture2D item, ref Rectangle itemFrame, ref float scale, ref int holdout)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.DrawTownAttackGun))
        {
            try { mod.DrawTownAttackGun(ref item, ref itemFrame, ref scale, ref holdout); }
            catch (Exception e) { Report(mod, HookId.DrawTownAttackGun, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.DrawTownAttackGun))
        {
            try { global.DrawTownAttackGun(npc, ref item, ref itemFrame, ref scale, ref holdout); }
            catch (Exception e) { Report(global, HookId.DrawTownAttackGun, e); }
        }
    }

    private static void DrawSwing(NPC npc, ref Texture2D item, ref Rectangle itemFrame, ref int size,
                                  ref float scale, ref float offsetX, ref float offsetY)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.DrawTownAttackSwing))
        {
            try
            {
                var offset = new Vector2(offsetX, offsetY);
                mod.DrawTownAttackSwing(ref item, ref itemFrame, ref size, ref scale, ref offset);
                offsetX = offset.X;
                offsetY = offset.Y;
            }
            catch (Exception e) { Report(mod, HookId.DrawTownAttackSwing, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.DrawTownAttackSwing))
        {
            try
            {
                var offset = new Vector2(offsetX, offsetY);
                global.DrawTownAttackSwing(npc, ref item, ref itemFrame, ref size, ref scale, ref offset);
                offsetX = offset.X;
                offsetY = offset.Y;
            }
            catch (Exception e) { Report(global, HookId.DrawTownAttackSwing, e); }
        }
    }

    private static readonly float[] _townDamage = new float[200];

    internal static float TownDamage(NPC npc) =>
        (uint)npc.whoAmI < 200u && _townDamage[npc.whoAmI] > 0f ? _townDamage[npc.whoAmI] : 1f;

    private static readonly HashSet<string> _reported = [];

    private static void Report(ModNpc mod, HookId hook, Exception exception)
    {
        if (!_reported.Add(mod.Type + "/" + hook)) return;
        ContentErrors.Report($"[townattack] {mod.Name}.{hook} 异常：{Safe.Describe(exception)}");
    }

    private static void Report(GlobalNpc global, HookId hook, Exception exception)
    {
        if (!_reported.Add(global.Name + "/" + hook)) return;
        ContentErrors.Report($"[townattack] {global.Name}.{hook} 异常：{Safe.Describe(exception)}");
    }
}
