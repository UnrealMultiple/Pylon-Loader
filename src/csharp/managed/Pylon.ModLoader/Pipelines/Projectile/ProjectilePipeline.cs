using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// 弹幕管线：把 ModProjectile / GlobalProjectile 接进"生成 → AI → 移动 → 命中 → 死亡 → 绘制 → 钩爪"这条链。
/// 跨钩子上下文只有两处（移动帧、绘制帧），都用定长结构体栈：每帧每实体都走的路不能分配。
/// </summary>
internal sealed class ProjectilePipeline : ILoaderModule
{
    public static ProjectilePipeline Instance { get; } = new();

    /// <summary>跨管线回调复用绑定实例，不能重新取模板或另建一份实体状态。</summary>
    internal ModProjectile? InstanceOf(Projectile? projectile) => _binding.InstanceOf(projectile);

    private const int FrameDepth = 2;

    private readonly ProjectileBinding _binding = new();
    private readonly ProjectileDefaults _defaults;
    private readonly MoveFrame[] _moves = new MoveFrame[FrameDepth];
    private readonly DrawFrame[] _draws = new DrawFrame[FrameDepth];
    private readonly HashSet<int> _reported = [];

    private int _moveDepth;
    private int _drawDepth;
    private long _cutting;
    private int _spawning;
    private bool _grappleRestore;
    private int _grappleShoot;
    private bool _pullChecked;
    private bool _pullWanted;

    private ProjectilePipeline() => _defaults = new ProjectileDefaults(_binding);

    public string Name => "projectile";

    public IReadOnlyList<TableRequest> Tables =>
        [new(ProjectileTables.Set, () => ModContent.NextType(ContentKind.Projectile), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    private ref MoveFrame Move => ref _moves[_moveDepth - 1];

    private ref DrawFrame Draw => ref _draws[_drawDepth - 1];

    public bool Install()
    {
        _defaults.Install();

        On.Terraria.Projectile.AI += Ai;
        On.Terraria.Projectile.ApplyStatsFromSource += StatsFromSource;
        On.Terraria.Projectile.HandleMovement += Movement;
        On.Terraria.Projectile.GetCollisionParams += CollisionParams;
        On.Terraria.Projectile.Kill += Kill;
        On.Terraria.Projectile.Damage += Damage;
        ProjectileHitHooks.Install();
        On.Terraria.Projectile.Damage_GetHitbox += DamageHitbox;
        On.Terraria.Projectile.Colliding += Colliding;
        On.Terraria.Projectile.CanCutTiles += CanCutTiles;
        On.Terraria.Projectile.CutTiles += CutTiles;
        On.Terraria.Projectile.GetAlpha += Alpha;
        On.Terraria.Projectile.AI_007_GrapplingHooks_CanTileBeLatchedOnTo += Latch;
        On.Terraria.Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier += Spawned;
        On.Terraria.Projectile.NewProjectile_IEntitySource_Vector2_Vector2_int_int_float_int_float_float_float_NewProjectileModifier += SpawnedVector;

        On.Terraria.Player.FireGrapple += FireGrapple;
        On.Terraria.Player.GetGrapplingForces += GrapplingForces;
        On.Terraria.Main.DrawProjDirect += DrawDirect;

        Log.Info("[projectile] 已挂（生成 / AI / 移动 / 命中 / 死亡 / 绘制 / 钩爪）");
        return true;
    }

    /// <summary>安装期：给每个自定义弹幕造模板并跑静态默认值三连。</summary>
    internal void SetupTemplates(IModLog log) => _defaults.CreateTemplates(log);

    /// <summary>覆写了接不了的钩子时给一条一次性警告，别让作者以为是自己的问题。</summary>
    internal void WarnUnwired(Mod mod)
    {
        foreach (var projectile in ModContent.GetContent<ModProjectile>())
        {
            if (projectile.Mod != mod) continue;
            if (!Hooks.Overrides(projectile.GetType(), typeof(ModProjectile), "NumGrappleHooks")) continue;

            Safe.Once("projectile.NumGrappleHooks." + projectile.GetType().FullName,
                $"[projectile] {projectile.Name} 覆写了 NumGrappleHooks，但钩爪上限写死在游戏逻辑里，按原版上限处理", true);
        }
    }

#region AI

    private void Ai(On.Terraria.Projectile.orig_AI orig, Projectile self)
    {
        var mod = _binding.InstanceOf(self);
        var globals = GlobalProjectileRegistry.AnyFor(self, HookId.PreAI, HookId.AI, HookId.PostAI);
        if (mod is null && !globals)
        {
            orig(self);
            return;
        }

        var savedType = self.type;
        var swapped = false;
        var skip = false;

        try
        {
            if (mod is not null && !mod.Spawned)
            {
                mod.Spawned = true;
                if (mod.Plan.Has(HookId.OnSpawn)) mod.OnSpawn(self);
            }
        }
        catch (Exception e)
        {
            Fail("OnSpawn", self.type, e);
        }

        if (mod is not null && mod.Plan.Has(HookId.PreAI))
        {
            try
            {
                if (!mod.PreAI(self)) skip = true;
            }
            catch (Exception e)
            {
                // 异常不能撤销作者已经作出的跳过决定；默认继续原版 AI。
                Fail("PreAI", self.type, e);
            }
        }

        if (globals)
        {
            foreach (var global in GlobalProjectileRegistry.For(self, HookId.PreAI))
            {
                try
                {
                    if (!global.PreAI(self)) skip = true;
                }
                catch (Exception e)
                {
                    Fail("PreAI", self.type, e);
                }
            }
        }

        if (!skip)
        {
            try
            {
                if (mod is not null && mod.AIType > 0)
                {
                    swapped = true;
                    self.type = mod.AIType;
                }

                if (self.aiStyle == ModProjectile.HookAIStyle && mod is not null && mod.Plan.Has(HookId.GrappleRange))
                    ApplyGrappleRange(self, mod);
            }
            catch (Exception e)
            {
                Fail("GrappleRange", self.type, e);
            }

            try
            {
                orig(self);
            }
            finally
            {
                if (swapped) self.type = savedType;
            }
        }

        try
        {
            if (!skip)
            {
                if (mod is not null && mod.Plan.Has(HookId.AI)) mod.AI(self);
                if (globals)
                {
                    foreach (var global in GlobalProjectileRegistry.For(self, HookId.AI)) global.AI(self);
                }
            }

            if (self.aiStyle == ModProjectile.HookAIStyle && mod is not null && mod.Plan.Has(HookId.GrappleRetreatSpeed))
                ApplyGrappleRetreat(self, mod);

            if (globals)
            {
                foreach (var global in GlobalProjectileRegistry.For(self, HookId.PostAI)) global.PostAI(self);
            }

            if (mod is not null && mod.Plan.Has(HookId.PostAI)) mod.PostAI(self);
        }
        catch (Exception e)
        {
            Fail("AI", self.type, e);
        }
    }

    private void StatsFromSource(On.Terraria.Projectile.orig_ApplyStatsFromSource orig, Projectile self,
                                 IEntitySource spawnSource)
    {
        orig(self, spawnSource);

        try
        {
            if (self.originalDamage != 0) return;
            if (ModContent.Find(ContentKind.Projectile, self.type) is null) return;

            // IL2CPP 接口在托管侧是代理类，不能 as，要用 TryCast 落到目标代理类。
            var itemUse = spawnSource is not null ? spawnSource.TryCast<EntitySource_ItemUse>() : null;
            if (itemUse?.Item is null) return;

            self.originalDamage = itemUse.Item.damage >= 0 ? itemUse.Item.damage : self.damage;
        }
        catch (Exception e)
        {
            Fail("ApplyStatsFromSource", self.type, e);
        }
    }

    /// <summary>浮点版 <c>NewProjectile(x, y, speedX, speedY, ...)</c>。</summary>
    private int Spawned(On.Terraria.Projectile.orig_NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier orig, IEntitySource spawnSource,
                        float x, float y, float speedX, float speedY, int type, int damage, float knockBack, int owner,
                        float ai0, float ai1, float ai2, NewProjectileModifier modifier)
    {
        int result;
        var outer = _spawning++;
        try
        {
            result = orig(spawnSource, x, y, speedX, speedY, type, damage, knockBack, owner, ai0, ai1, ai2, modifier);
        }
        finally
        {
            _spawning--;
        }

        return outer == 0 ? DispatchSpawned(result, spawnSource, type) : result;
    }

    /// <summary>向量版 <c>NewProjectile(position, velocity, ...)</c>：两个重载都要挂。</summary>
    private int SpawnedVector(On.Terraria.Projectile.orig_NewProjectile_IEntitySource_Vector2_Vector2_int_int_float_int_float_float_float_NewProjectileModifier orig, IEntitySource spawnSource,
                              Vector2 position, Vector2 velocity, int type, int damage, float knockBack, int owner,
                              float ai0, float ai1, float ai2, NewProjectileModifier modifier)
    {
        int result;
        var outer = _spawning++;
        try
        {
            result = orig(spawnSource, position, velocity, type, damage, knockBack, owner, ai0, ai1, ai2, modifier);
        }
        finally
        {
            _spawning--;
        }

        return outer == 0 ? DispatchSpawned(result, spawnSource, type) : result;
    }

    /// <summary>
    /// 两个重载共用的生成分派：作者 <c>OnSpawn</c> + <c>GlobalProjectile.OnSpawn</c>；
    /// 嵌套调用只由最外层分派一次。生成时就调，不等首帧 AI。
    /// </summary>
    private int DispatchSpawned(int result, IEntitySource? spawnSource, int type)
    {
        if (result < 0) return result;

        var all = Main.projectile;
        if (all is null || result >= all.Length) return result;

        var projectile = all[result];
        if (projectile is null || !projectile.active) return result;

        try
        {
            if (_binding.InstanceOf(projectile) is { } mod && !mod.Spawned)
            {
                mod.Spawned = true;
                if (mod.Plan.Has(HookId.OnSpawn)) mod.OnSpawn(projectile);
            }
        }
        catch (Exception e)
        {
            Fail("OnSpawn", type, e);
        }

        if (!GlobalProjectileRegistry.AnyWith(HookId.OnSpawn)) return result;

        try
        {
            foreach (var global in GlobalProjectileRegistry.For(projectile, HookId.OnSpawn))
                global.OnSpawn(projectile, spawnSource!);
        }
        catch (Exception e)
        {
            Fail("GlobalProjectile.OnSpawn", type, e);
        }

        return result;
    }

#endregion

#region 移动与撞块

    private void Movement(On.Terraria.Projectile.orig_HandleMovement orig, Projectile self, Vector2 wetVelocity)
    {
        var mod = _binding.InstanceOf(self);
        var collideGlobals = GlobalProjectileRegistry.AnyFor(self, HookId.OnTileCollide);
        var styleGlobals = GlobalProjectileRegistry.AnyFor(self, HookId.TileCollideStyle);

        var hasCollision = (mod is not null && mod.Plan.Has(HookId.OnTileCollide)) || collideGlobals;
        var hasStyle = (mod is not null && mod.Plan.Has(HookId.TileCollideStyle)) || styleGlobals;
        if ((mod is null && !collideGlobals && !styleGlobals) || !self.tileCollide || (!hasCollision && !hasStyle))
        {
            orig(self, wetVelocity);
            return;
        }

        var pushed = PushMove(self, hasCollision, hasStyle, mod);
        try
        {
            orig(self, wetVelocity);
            ResolveMove(self, mod);
        }
        finally
        {
            if (pushed) PopMove(self);
        }
    }

    /// <summary>把 TileCollideStyle 的改写写进帧，并让游戏这一帧按改后的值算碰撞。</summary>
    private bool PushMove(Projectile proj, bool hasCollision, bool hasStyle, ModProjectile? mod)
    {
        if (_moveDepth >= FrameDepth) return false;

        ref var frame = ref _moves[_moveDepth];
        frame = new MoveFrame
        {
            Address = ProjectileBinding.Address(proj),
            HasCollision = hasCollision,
            Start = proj.position,
            OldVelocity = proj.velocity,
        };
        _moveDepth++;

        if (!hasStyle) return true;

        try
        {
            proj.GetCollisionParams(out var anchor, out var width, out var height);
            frame.Anchor = anchor;
            frame.Width = width;
            frame.Height = height;

            var fallThrough = VanillaFallThrough(proj);
            var allowed = true;

            if (mod is not null && mod.Plan.Has(HookId.TileCollideStyle))
                allowed = mod.TileCollideStyle(proj, ref width, ref height, ref fallThrough, ref anchor);

            foreach (var global in GlobalProjectileRegistry.For(proj, HookId.TileCollideStyle))
            {
                if (global.TileCollideStyle(proj, ref width, ref height, ref fallThrough, ref anchor)) continue;
                allowed = false;
                break;
            }

            frame.Anchor = anchor;
            frame.Width = width;
            frame.Height = height;
            frame.StyleApplied = true;
            frame.SavedDecides = proj.decidesManualFallThrough;
            frame.SavedFall = proj.shouldFallThrough;
            proj.decidesManualFallThrough = true;
            proj.shouldFallThrough = fallThrough;

            if (allowed) return true;
            frame.Bypass = true;
            proj.tileCollide = false;
        }
        catch (Exception e)
        {
            Fail("TileCollideStyle", proj.type, e);
        }

        return true;
    }

    private void ResolveMove(Projectile proj, ModProjectile? mod)
    {
        if (_moveDepth == 0) return;

        ref var frame = ref Move;
        if (frame.Address != ProjectileBinding.Address(proj)) return;

        try
        {
            if (!frame.Reported && frame.HasCollision && proj.active && proj.tileCollide && Collided(proj, in frame))
                ReportCollision(proj, mod, ref frame);

            if (frame.Kept && proj.active)
                proj.position = new Vector2(frame.Start.X + proj.velocity.X, frame.Start.Y + proj.velocity.Y);
        }
        catch (Exception e)
        {
            Fail("OnTileCollide", proj.type, e);
        }
    }

    private void PopMove(Projectile proj)
    {
        if (_moveDepth == 0) return;

        ref var frame = ref Move;
        if (frame.StyleApplied)
        {
            proj.decidesManualFallThrough = frame.SavedDecides;
            proj.shouldFallThrough = frame.SavedFall;
        }

        if (frame.Bypass) proj.tileCollide = true;
        _moveDepth--;
    }

    private static bool Collided(Projectile proj, in MoveFrame frame) =>
        proj.velocity.X != frame.OldVelocity.X || proj.velocity.Y != frame.OldVelocity.Y;

    /// <summary>撞块：OnTileCollide 收撞击前的速度；返回 false 表示这一发不该死。</summary>
    private void ReportCollision(Projectile proj, ModProjectile? mod, ref MoveFrame frame)
    {
        frame.Reported = true;

        var oldVelocity = frame.OldVelocity;
        var allowed = true;

        foreach (var global in GlobalProjectileRegistry.For(proj, HookId.OnTileCollide))
        {
            if (global.OnTileCollide(proj, oldVelocity)) continue;
            allowed = false;
            break;
        }

        if (allowed && mod is not null && mod.Plan.Has(HookId.OnTileCollide) && !mod.OnTileCollide(proj, oldVelocity))
            allowed = false;

        if (!frame.PendingKill) return;

        if (!allowed) frame.Kept = true;
        else if (proj.active) proj.Kill();
    }

    /// <summary>GetCollisionParams 每帧被调多次：只把 TileCollideStyle 的决定喂回去，不再问作者。</summary>
    private void CollisionParams(On.Terraria.Projectile.orig_GetCollisionParams orig, Projectile self,
                                 out Vector2 resizeAnchor, out int colWidth, out int colHeight)
    {
        orig(self, out resizeAnchor, out colWidth, out colHeight);

        if (_moveDepth == 0) return;

        ref var frame = ref Move;
        if (!frame.StyleApplied || frame.Address != ProjectileBinding.Address(self)) return;

        resizeAnchor = frame.Anchor;
        colWidth = frame.Width;
        colHeight = frame.Height;
    }

    private static readonly int[] NoFallThrough =
    [
        9, 12, 13, 15, 24, 663, 665, 667, 677, 678, 679, 688, 689, 690, 691, 692, 693, 1037, 1049, 1105,
    ];

    /// <summary>原版 HandleMovement 里的 fallThrough 判定；作者改写它之前必须知道原值。</summary>
    private static bool VanillaFallThrough(Projectile proj)
    {
        if (proj.decidesManualFallThrough) return proj.shouldFallThrough;

        var type = proj.type;
        var style = proj.aiStyle;
        var fall = true;

        if (InRange(Main.projPet, type) && Main.projPet[type])
        {
            var owner = Main.player[proj.owner];
            fall = owner.position.Y + owner.height - 12 > proj.position.Y + proj.height;
        }

        if (type is 500 or 653 or 1018)
        {
            var owner = Main.player[proj.owner];
            fall = owner.position.Y + owner.height > proj.position.Y + proj.height + 4;
        }

        fall = ((fall && type != 378) || style == 62 || style == 66 || type == 317 || type == 373) &&
               type != 281 && type != 253 && style != 53 || style == 197;

        for (var i = 0; i < NoFallThrough.Length; i++)
        {
            if (NoFallThrough[i] != type) continue;
            fall = false;
            break;
        }

        if (style == 10 && InRange(ProjectileID.Sets.FallingBlockDoesNotFallThroughPlatforms, type) &&
            ProjectileID.Sets.FallingBlockDoesNotFallThroughPlatforms[type])
            fall = false;

        if (style == 99 && proj.ai[0] is -2f) fall = false;
        if (type == 759) fall = true;

        if (type == 1020)
        {
            var rotation = proj.rotation % (Math.PI * 2);
            if (rotation >= Math.PI) rotation -= Math.PI * 2;
            if (rotation < -Math.PI) rotation += Math.PI * 2;
            fall = rotation >= Math.PI / 2 || rotation < -Math.PI / 2;
        }

        return fall;
    }

#endregion

#region 死亡

    private void Kill(On.Terraria.Projectile.orig_Kill orig, Projectile self)
    {
        if (!self.active)
        {
            orig(self);
            return;
        }

        // 移动途中撞块致死先挂起：等 HandleMovement 结束、OnTileCollide 表态后再决定。
        if (_moveDepth > 0)
        {
            ref var moving = ref Move;
            if (moving.Address == ProjectileBinding.Address(self) && !moving.Reported &&
                moving.HasCollision && self.tileCollide)
            {
                moving.PendingKill = true;
                return;
            }
        }

        var mod = _binding.InstanceOf(self);
        var preKill = GlobalProjectileRegistry.For(self, HookId.PreKill);
        var onKill = GlobalProjectileRegistry.For(self, HookId.OnKill);
        if (mod is null && preKill.Count == 0 && onKill.Count == 0)
        {
            orig(self);
            return;
        }

        var timeLeft = self.timeLeft;
        try
        {
            var allowed = true;
            foreach (var global in preKill)
            {
                if (global.PreKill(self, timeLeft)) continue;
                allowed = false;
                break;
            }

            if (allowed && mod is not null && mod.Plan.Has(HookId.PreKill) && !mod.PreKill(self, timeLeft))
                allowed = false;

            if (!allowed)
            {
                self.active = false;
                return;
            }

            if (mod is not null && mod.Plan.Has(HookId.OnKill)) mod.OnKill(self, timeLeft);
            foreach (var global in onKill) global.OnKill(self, timeLeft);
        }
        catch (Exception e)
        {
            Fail("Kill", self.type, e);
        }

        orig(self);
    }

#endregion

#region 命中

    private void Damage(On.Terraria.Projectile.orig_Damage orig, Projectile self)
    {
        var mod = _binding.InstanceOf(self);
        var anyGlobals = GlobalProjectileRegistry.AnyFor(self, HookId.CanDamage, HookId.MinionContactDamage, HookId.Colliding);
        if (mod is null && !anyGlobals)
        {
            orig(self);
            return;
        }

        var proceed = true;
        var allowedByGlobal = false;
        try
        {
            foreach (var global in GlobalProjectileRegistry.For(self, HookId.CanDamage))
            {
                var verdict = global.CanDamage(self);
                if (verdict == false)
                {
                    proceed = false;
                    break;
                }

                if (verdict == true) allowedByGlobal = true;
            }

            if (proceed && !allowedByGlobal && mod is not null && mod.Plan.Has(HookId.CanDamage) && !mod.CanDamage(self))
                proceed = false;
        }
        catch (Exception e)
        {
            Fail("CanDamage", self.type, e);
        }

        if (!proceed) return;

        var contactGlobals = GlobalProjectileRegistry.For(self, HookId.MinionContactDamage);
        var collisionGlobals = GlobalProjectileRegistry.For(self, HookId.Colliding);
        var modContact = mod is not null && mod.Plan.Has(HookId.MinionContactDamage);

        var type = self.type;
        var contact = false;
        var customCollision = false;
        var wasPet = false;
        var wasComplex = false;

        if (contactGlobals.Count > 0 || collisionGlobals.Count > 0 || modContact)
        {
            try
            {
                wasPet = InRange(Main.projPet, type) && Main.projPet[type];
                if (wasPet)
                {
                    if (modContact) contact = mod!.MinionContactDamage(self);
                    if (!contact)
                    {
                        foreach (var global in contactGlobals)
                        {
                            if (!global.MinionContactDamage(self)) continue;
                            contact = true;
                            break;
                        }
                    }
                }

                if (collisionGlobals.Count > 0 && InRange(ProjectileID.Sets.IsAComplexCollision, type))
                {
                    wasComplex = ProjectileID.Sets.IsAComplexCollision[type];
                    customCollision = !wasComplex;
                }
            }
            catch (Exception e)
            {
                Fail("MinionContactDamage", type, e);
            }
        }

        // 宠物贴身判定与自定义碰撞都读"类型表"而不是这一发：改—调—还原必须夹在同一个 try/finally 里。
        var petChanged = contact && InRange(Main.projPet, type);
        var complexChanged = customCollision && InRange(ProjectileID.Sets.IsAComplexCollision, type);

        if (petChanged) Main.projPet[type] = false;
        if (complexChanged) ProjectileID.Sets.IsAComplexCollision[type] = true;
        if (!petChanged && !complexChanged)
        {
            orig(self);
            return;
        }

        try
        {
            orig(self);
        }
        finally
        {
            if (petChanged) Main.projPet[type] = wasPet;
            if (complexChanged) ProjectileID.Sets.IsAComplexCollision[type] = wasComplex;
        }
    }

    private bool Colliding(On.Terraria.Projectile.orig_Colliding orig, Projectile self,
                           Rectangle myRect, Rectangle targetRect)
    {
        var mod = _binding.InstanceOf(self);
        if (mod is null && !GlobalProjectileRegistry.AnyFor(self, HookId.Colliding)) return orig(self, myRect, targetRect);

        try
        {
            foreach (var global in GlobalProjectileRegistry.For(self, HookId.Colliding))
            {
                if (global.Colliding(self, myRect, targetRect) is not { } verdict) continue;
                return verdict;
            }

            if (mod is not null && mod.Plan.Has(HookId.Colliding) && mod.Colliding(self, myRect, targetRect) is { } mine)
                return mine;
        }
        catch (Exception e)
        {
            Fail("Colliding", self.type, e);
        }

        return orig(self, myRect, targetRect);
    }

    private Rectangle DamageHitbox(On.Terraria.Projectile.orig_Damage_GetHitbox orig, Projectile self)
    {
        var box = orig(self);

        var mod = _binding.InstanceOf(self);
        var globals = GlobalProjectileRegistry.For(self, HookId.ModifyDamageHitbox);
        if (mod is null && globals.Count == 0) return box;

        try
        {
            if (mod is not null && mod.Plan.Has(HookId.ModifyDamageHitbox)) mod.ModifyDamageHitbox(self, ref box);
            foreach (var global in globals) global.ModifyDamageHitbox(self, ref box);
        }
        catch (Exception e)
        {
            Fail("ModifyDamageHitbox", self.type, e);
        }

        return box;
    }

#endregion

#region 割草

    private bool CanCutTiles(On.Terraria.Projectile.orig_CanCutTiles orig, Projectile self)
    {
        // 原版 CutTiles 内部会再问一次，不能让它绕回去再问作者（否则重入）。
        if (_cutting != 0 && _cutting == ProjectileBinding.Address(self)) return true;

        var mod = _binding.InstanceOf(self);
        if (mod is null && !GlobalProjectileRegistry.AnyFor(self, HookId.CanCutTiles)) return orig(self);

        try
        {
            foreach (var global in GlobalProjectileRegistry.For(self, HookId.CanCutTiles))
            {
                if (global.CanCutTiles(self) is not { } verdict) continue;
                return verdict;
            }

            if (mod is not null && mod.Plan.Has(HookId.CanCutTiles) && mod.CanCutTiles(self) is { } mine) return mine;
        }
        catch (Exception e)
        {
            Fail("CanCutTiles", self.type, e);
        }

        return orig(self);
    }

    private void CutTiles(On.Terraria.Projectile.orig_CutTiles orig, Projectile self)
    {
        var mod = _binding.InstanceOf(self);
        var globals = GlobalProjectileRegistry.For(self, HookId.CutTiles);
        var modCut = mod is not null && mod.Plan.Has(HookId.CutTiles);
        if (!modCut && globals.Count == 0)
        {
            orig(self);
            return;
        }

        try
        {
            if (!self.CanCutTiles()) return;

            foreach (var global in globals) global.CutTiles(self);
            if (modCut) mod!.CutTiles(self);
        }
        catch (Exception e)
        {
            Fail("CutTiles", self.type, e);
            return;
        }

        _cutting = ProjectileBinding.Address(self);
        try
        {
            orig(self);
        }
        finally
        {
            _cutting = 0;
        }
    }

#endregion

#region 绘制

    private Color Alpha(On.Terraria.Projectile.orig_GetAlpha orig, Projectile self, Color newColor)
    {
        var mod = _binding.InstanceOf(self);
        if (mod is null && !GlobalProjectileRegistry.AnyFor(self, HookId.GetAlpha)) return orig(self, newColor);

        try
        {
            var light = newColor;
            if (_drawDepth > 0)
            {
                ref var frame = ref Draw;
                if (frame.Address == ProjectileBinding.Address(self)) light = frame.Light;
            }

            foreach (var global in GlobalProjectileRegistry.For(self, HookId.GetAlpha))
            {
                if (global.GetAlpha(self, light) is not { } verdict) continue;
                return verdict;
            }

            if (mod is not null && mod.Plan.Has(HookId.GetAlpha) && mod.GetAlpha(self, light) is { } mine) return mine;
        }
        catch (Exception e)
        {
            Fail("GetAlpha", self.type, e);
        }

        return orig(self, newColor);
    }

    private void DrawDirect(On.Terraria.Main.orig_DrawProjDirect orig, Main self, Projectile proj, Player overridePlayer)
    {
        var mod = _binding.InstanceOf(proj);
        var pre = GlobalProjectileRegistry.For(proj, HookId.PreDraw);
        var post = GlobalProjectileRegistry.For(proj, HookId.PostDraw);
        if (mod is null && pre.Count == 0 && post.Count == 0)
        {
            orig(self, proj, overridePlayer);
            return;
        }

        var light = LightAt(proj);
        var sprite = true;

        try
        {
            foreach (var global in pre)
            {
                if (global.PreDraw(proj, ref light)) continue;
                sprite = false;
            }

            if (mod is not null)
            {
                // 手机版把附加物画在 DrawProjDirect 内部：PreDrawExtras 只保留副作用，返回值不生效。
                if (mod.Plan.Has(HookId.PreDrawExtras)) mod.PreDrawExtras(proj);
                if (mod.Plan.Has(HookId.PreDraw) && !mod.PreDraw(proj, light)) sprite = false;
            }
        }
        catch (Exception e)
        {
            Fail("PreDraw", proj.type, e);
            sprite = true;
        }

        try
        {
            if (!sprite) return;

            PushDraw(proj, light);
            try
            {
                orig(self, proj, overridePlayer);
            }
            finally
            {
                if (_drawDepth > 0) _drawDepth--;
            }
        }
        finally
        {
            try
            {
                if (mod is not null && mod.Plan.Has(HookId.PostDraw)) mod.PostDraw(proj, light);
                foreach (var global in post) global.PostDraw(proj, light);
            }
            catch (Exception e)
            {
                Fail("PostDraw", proj.type, e);
            }
        }
    }

    private void PushDraw(Projectile proj, Color light)
    {
        if (_drawDepth >= FrameDepth) return;

        ref var frame = ref _draws[_drawDepth];
        frame = new DrawFrame { Address = ProjectileBinding.Address(proj), Light = light };
        _drawDepth++;
    }

    private static Color LightAt(Projectile proj)
    {
        try
        {
            var x = proj.position.X + proj.width * 0.5f;
            var y = proj.position.Y + proj.height * 0.5f;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return Color.White;

            return Lighting.GetColor((int)(x / 16f), (int)(y / 16f));
        }
        catch
        {
            return Color.White;
        }
    }

#endregion

#region 钩爪

    private void FireGrapple(On.Terraria.Player.orig_FireGrapple orig, Player self, Item grappleItem)
    {
        if (PrepareGrapple(self, grappleItem)) orig(self, grappleItem);
        RestoreGrapple(grappleItem);
    }

    private bool PrepareGrapple(Player player, Item? grappleItem)
    {
        if (grappleItem is null) return true;

        var shoot = grappleItem.shoot;
        var template = ModContent.Find(ContentKind.Projectile, shoot) as ModProjectile;
        var canUse = GlobalProjectileRegistry.TemplatesFor(HookId.CanUseGrapple);
        if (template is null && canUse.Count == 0) return true;

        try
        {
            var allowed = true;
            if (template is not null && template.Plan.Has(HookId.CanUseGrapple)) allowed = template.CanUseGrapple(player, shoot);

            foreach (var global in canUse)
            {
                if (global.CanUseGrapple(shoot, player) is not { } verdict) continue;
                allowed = verdict;
            }

            if (!allowed) return false;

            var chosen = shoot;
            if (template is not null && template.Plan.Has(HookId.UseGrapple)) chosen = template.UseGrapple(player, shoot);
            foreach (var global in GlobalProjectileRegistry.TemplatesFor(HookId.UseGrapple))
                global.UseGrapple(player, ref chosen);

            if (chosen <= 0 || chosen == shoot) return true;

            grappleItem.shoot = chosen;
            _grappleRestore = true;
            _grappleShoot = shoot;
        }
        catch (Exception e)
        {
            Fail("CanUseGrapple", shoot, e);
        }

        return true;
    }

    private void RestoreGrapple(Item? grappleItem)
    {
        if (!_grappleRestore) return;

        _grappleRestore = false;
        if (grappleItem is not null) grappleItem.shoot = _grappleShoot;
    }

    private bool Latch(On.Terraria.Projectile.orig_AI_007_GrapplingHooks_CanTileBeLatchedOnTo orig,
                       Projectile self, Tile theTile)
    {
        var mod = _binding.InstanceOf(self);
        if (mod is null && !GlobalProjectileRegistry.AnyFor(self, HookId.GrappleCanLatchOnTo)) return orig(self, theTile);

        try
        {
            var player = Main.player[self.owner];

            bool? verdict = null;
            if (mod is not null && mod.Plan.Has(HookId.GrappleCanLatchOnTo))
                verdict = mod.GrappleCanLatchOnTo(self, player, theTile);

            foreach (var global in GlobalProjectileRegistry.For(self, HookId.GrappleCanLatchOnTo))
            {
                var answer = global.GrappleCanLatchOnTo(self, player, theTile);
                if (answer == false) return false;
                if (answer == true) verdict = true;
            }

            if (verdict.HasValue) return verdict.Value;
        }
        catch (Exception e)
        {
            Fail("GrappleCanLatchOnTo", self.type, e);
        }

        return orig(self, theTile);
    }

    /// <summary>射程：超了就往回收（ai[0]=1，与 AI_007 自己的回收状态一致）。</summary>
    private static void ApplyGrappleRange(Projectile proj, ModProjectile mod)
    {
        if (mod.AIType > 0) return;
        if (proj.ai[0] != 0f) return;

        var owner = Main.player[proj.owner];
        var center = owner.MountedCenter;
        var dx = center.X - (proj.position.X + proj.width * 0.5f);
        var dy = center.Y - (proj.position.Y + proj.height * 0.5f);
        if (Math.Sqrt(dx * dx + dy * dy) <= mod.GrappleRange(proj)) return;

        var ai = proj.ai;
        ai[0] = 1f;
        proj.ai = ai;
    }

    private static void ApplyGrappleRetreat(Projectile proj, ModProjectile mod)
    {
        if (!proj.active || proj.ai[0] != 1f) return;

        var baseSpeed = VanillaRetreatSpeed(mod.AIType > 0 ? mod.AIType : proj.type);
        if (baseSpeed <= 0) return;

        var speed = baseSpeed;
        mod.GrappleRetreatSpeed(proj, Main.player[proj.owner], ref speed);

        var ratio = speed / baseSpeed;
        if (ratio == 1f) return;

        proj.velocity = new Vector2(proj.velocity.X * ratio, proj.velocity.Y * ratio);
    }

    /// <summary>挂住后把玩家拉过去：目标点是所有已挂住的钩爪中心，第一个钩爪的收绳速度决定快慢。</summary>
    private void GrapplingForces(On.Terraria.Player.orig_GetGrapplingForces orig, Player self, Vector2 fromPosition,
                                 out Il2CppSystem.Nullable<int> preferredDirection,
                                 out float velocityX, out float velocityY)
    {
        orig(self, fromPosition, out preferredDirection, out velocityX, out velocityY);

        if (!WantsGrapplePull()) return;

        try
        {
            var grappling = self.grappling;
            if (grappling is null || grappling.Length == 0) return;

            var count = Math.Min(self.grapCount, grappling.Length);
            if (count <= 0) return;

            Projectile? first = null;
            var centerX = 0f;
            var centerY = 0f;
            var latched = 0;

            for (var i = 0; i < count; i++)
            {
                var hook = Main.projectile[grappling[i]];
                if (hook is null || !hook.active || hook.ai[0] != 2f) continue;

                // 原版自带拉力的钩爪：别插手。
                if (hook.type is 446 or 652 or 865) return;

                centerX += hook.position.X + hook.width * 0.5f;
                centerY += hook.position.Y + hook.height * 0.5f;
                latched++;
                first ??= hook;
            }

            if (latched == 0 || first is null) return;

            centerX /= latched;
            centerY /= latched;

            for (var i = 0; i < count; i++)
            {
                var hook = Main.projectile[grappling[i]];
                if (hook is null || !hook.active || hook.ai[0] != 2f) continue;

                var mod = _binding.InstanceOf(hook);
                if (mod is not null && mod.Plan.Has(HookId.GrappleTargetPoint))
                    mod.GrappleTargetPoint(hook, self, ref centerX, ref centerY);
            }

            var speed = VanillaPullSpeed(first.type);
            var firstMod = _binding.InstanceOf(first);
            if (firstMod is not null && firstMod.Plan.Has(HookId.GrapplePullSpeed))
                firstMod.GrapplePullSpeed(first, self, ref speed);

            var dx = centerX - fromPosition.X;
            var dy = centerY - fromPosition.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var scale = length <= speed || length <= 0 ? 1d : speed / length;

            velocityX = (float)(dx * scale);
            velocityY = (float)(dy * scale);
        }
        catch (Exception e)
        {
            Fail("GrapplePullSpeed", 0, e);
        }
    }

    private bool WantsGrapplePull()
    {
        if (_pullChecked) return _pullWanted;

        _pullChecked = true;
        foreach (var type in ModContent.GetContent<ModProjectile>())
        {
            if (!type.Plan.Has(HookId.GrappleTargetPoint) && !type.Plan.Has(HookId.GrapplePullSpeed)) continue;
            _pullWanted = true;
            break;
        }

        return _pullWanted;
    }

    private static float VanillaRetreatSpeed(int type)
    {
        if (type is >= 230 and <= 235) return 11 + (type - 230) * 0.75f;
        if (type is >= 486 and <= 489) return 18;
        if (type is >= 646 and <= 649) return 24;

        return type switch
        {
            32 => 15,
            73 => 22,
            74 => 22,
            315 => 20,
            322 => 22,
            753 => 15,
            446 => 20,
            652 => 24,
            332 => 17,
            _ => 11,
        };
    }

    private static float VanillaPullSpeed(int type)
    {
        if (type is >= 646 and <= 649) return 16;

        return type switch
        {
            73 => 12.5f,
            74 => 12.5f,
            315 => 14,
            487 => 12,
            _ => 11,
        };
    }

#endregion

#region 工具

    private static bool InRange(Il2CppArrayBase<bool>? array, int index) =>
        array is not null && index >= 0 && index < array.Length;

    /// <summary>同一 (钩子, 类型) 只记一条：AI/撞块这类每帧路径出错会刷屏。</summary>
    private void Fail(string hook, int type, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(hook, type))) return;
        ContentErrors.Report($"[projectile] {hook} 异常（type={type}）：{Safe.Describe(exception)}");
    }

    private struct MoveFrame
    {
        public long Address;
        public bool HasCollision;
        public bool Reported;
        public bool PendingKill;
        public bool Kept;
        public bool Bypass;
        public bool StyleApplied;
        public bool SavedDecides;
        public bool SavedFall;
        public Vector2 Start;
        public Vector2 OldVelocity;
        public Vector2 Anchor;
        public int Width;
        public int Height;
    }

    private struct DrawFrame
    {
        public long Address;
        public Color Light;
    }
#endregion

}

/// <summary>弹幕编号相关的原生表：弹幕、Lang、贴图、Main 里的缓存数组。</summary>
internal static class ProjectileTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.ID.ProjectileID.Sets),
        typeof(Terraria.Main),
        typeof(Terraria.Lang),
        typeof(Terraria.Projectile),
        typeof(Terraria.GameContent.TextureAssets),
    ];

    internal static readonly TableSet Set = new("弹幕", () => Terraria.ID.ProjectileID.Count, Types);

}
