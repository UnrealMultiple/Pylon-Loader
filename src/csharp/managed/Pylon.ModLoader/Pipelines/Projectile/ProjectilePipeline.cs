using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>弹幕管线：把 ModProjectile / GlobalProjectile 接进"生成 → AI → 移动 → 命中 → 死亡 → 绘制 → 钩爪"这条链；跨钩子上下文用定长结构体栈，避免每帧分配。</summary>
internal sealed class ProjectilePipeline : ILoaderModule
{
    public static ProjectilePipeline Instance { get; } = new();

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
        On.Terraria.Projectile.UpdatePosition += UpdatePosition;
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

        ProjectileDrawSplit.Install();

        Log.Info("[projectile] 已挂（生成 / AI / 移动 / 命中 / 死亡 / 绘制 / 钩爪）");
        return true;
    }

    internal void SetupTemplates(IModLog log) => _defaults.CreateTemplates(log);

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
                if (mod.Plan.Has(HookId.OnSpawn)) mod.OnSpawn(null);
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
                if (!mod.PreAI()) skip = true;
            }
            catch (Exception e)
            {
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

        List<Projectile>? hidden = null;
        var hookLimit = -1;
        if (!skip && mod is not null && self.aiStyle == ModProjectile.HookAIStyle &&
            mod.Plan.Has(HookId.NumGrappleHooks) && self.ai[0] == 0f && self.owner == Main.myPlayer)
        {
            try
            {
                var vanilla = VanillaHookLimit(self.type);
                var numHooks = vanilla;
                mod.NumGrappleHooks(Main.player[self.owner], ref numHooks);
                if (numHooks != vanilla)
                {
                    hookLimit = numHooks;
                    hidden = LatchedHooks(self.owner, self.whoAmI);
                    foreach (var hook in hidden) hook.aiStyle = 0;
                }
            }
            catch (Exception e)
            {
                Fail("NumGrappleHooks", self.type, e);
                hidden = null;
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
                if (hidden is not null)
                {
                    foreach (var hook in hidden) hook.aiStyle = ModProjectile.HookAIStyle;
                }
            }

            if (hidden is not null && self.active && self.ai[0] == 2f)
            {
                try
                {
                    TrimHooks(self.owner, hookLimit);
                }
                catch (Exception e)
                {
                    Fail("NumGrappleHooks", self.type, e);
                }
            }
        }

        try
        {
            if (!skip)
            {
                if (mod is not null && mod.Plan.Has(HookId.AI)) mod.AI();
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

            if (mod is not null && mod.Plan.Has(HookId.PostAI)) mod.PostAI();
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

            var itemUse = spawnSource is not null ? spawnSource.TryCast<EntitySource_ItemUse>() : null;
            if (itemUse?.Item is null) return;

            self.originalDamage = itemUse.Item.damage >= 0 ? itemUse.Item.damage : self.damage;
        }
        catch (Exception e)
        {
            Fail("ApplyStatsFromSource", self.type, e);
        }
    }

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
                if (mod.Plan.Has(HookId.OnSpawn)) mod.OnSpawn(spawnSource);
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

    private void UpdatePosition(On.Terraria.Projectile.orig_UpdatePosition orig, Projectile self, Vector2 wetVelocity)
    {
        var mod = _binding.InstanceOf(self);
        var globals = GlobalProjectileRegistry.AnyFor(self, HookId.ShouldUpdatePosition);
        if ((mod is null || !mod.Plan.Has(HookId.ShouldUpdatePosition)) && !globals)
        {
            orig(self, wetVelocity);
            return;
        }

        var allowed = true;
        try
        {
            foreach (var global in GlobalProjectileRegistry.For(self, HookId.ShouldUpdatePosition))
            {
                if (global.ShouldUpdatePosition(self)) continue;
                allowed = false;
                break;
            }

            if (allowed && mod is not null && mod.Plan.Has(HookId.ShouldUpdatePosition))
                allowed = mod.ShouldUpdatePosition();
        }
        catch (Exception e)
        {
            Fail("ShouldUpdatePosition", self.type, e);
        }

        if (allowed) orig(self, wetVelocity);
    }

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
                allowed = mod.TileCollideStyle(ref width, ref height, ref fallThrough, ref anchor);

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

        if (allowed && mod is not null && mod.Plan.Has(HookId.OnTileCollide) && !mod.OnTileCollide(oldVelocity))
            allowed = false;

        if (!frame.PendingKill) return;

        if (!allowed) frame.Kept = true;
        else if (proj.active) proj.Kill();
    }

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

            if (allowed && mod is not null && mod.Plan.Has(HookId.PreKill) && !mod.PreKill(timeLeft))
                allowed = false;

            if (!allowed)
            {
                self.active = false;
                return;
            }

            if (mod is not null && mod.Plan.Has(HookId.OnKill)) mod.OnKill(timeLeft);
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

            if (proceed && !allowedByGlobal && mod is not null && mod.Plan.Has(HookId.CanDamage) && !mod.CanDamage())
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
                    if (modContact) contact = mod!.MinionContactDamage();
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

            if (mod is not null && mod.Plan.Has(HookId.Colliding) && mod.Colliding(myRect, targetRect) is { } mine)
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
            if (mod is not null && mod.Plan.Has(HookId.ModifyDamageHitbox)) mod.ModifyDamageHitbox(ref box);
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

            if (mod is not null && mod.Plan.Has(HookId.CanCutTiles) && mod.CanCutTiles() is { } mine) return mine;
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
            if (modCut) mod!.CutTiles();
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

            if (mod is not null && mod.Plan.Has(HookId.GetAlpha) && mod.GetAlpha(light) is { } mine) return mine;
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
        var extrasGlobals = GlobalProjectileRegistry.For(proj, HookId.PreDrawExtras);
        var drawHooks = (mod?.Plan.Has(HookId.PreDraw) ?? false) || (mod?.Plan.Has(HookId.PostDraw) ?? false) ||
                        pre.Count > 0 || post.Count > 0;
        var extrasHooks = (mod?.Plan.Has(HookId.PreDrawExtras) ?? false) || extrasGlobals.Count > 0;
        if (mod is null && !drawHooks && !extrasHooks)
        {
            orig(self, proj, overridePlayer);
            return;
        }

        var light = LightAt(proj);
        var owner = overridePlayer ?? (proj.owner >= 0 && proj.owner < Main.player.Length ? Main.player[proj.owner] : null);
        var extras = true;
        var sprite = true;

        if (extrasHooks)
        {
            try
            {
                foreach (var global in extrasGlobals)
                    if (!global.PreDrawExtras(proj, owner!)) extras = false;

                if (extras && mod is not null && mod.Plan.Has(HookId.PreDrawExtras))
                    extras = mod.PreDrawExtras(owner!);
            }
            catch (Exception e)
            {
                Fail("PreDrawExtras", proj.type, e);
                extras = true;
            }
        }

        try
        {
            foreach (var global in pre)
            {
                if (global.PreDraw(proj, owner!, ref light)) continue;
                sprite = false;
            }

            if (mod is not null && mod.Plan.Has(HookId.PreDraw) && !mod.PreDraw(owner!, ref light)) sprite = false;
        }
        catch (Exception e)
        {
            Fail("PreDraw", proj.type, e);
            sprite = true;
        }

        var split = sprite != extras && HasExtras(proj) && ProjectileDrawSplit.Begin(OwnTexture(proj), extras, sprite);
        try
        {
            if (sprite || split)
            {
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
        }
        finally
        {
            if (split) ProjectileDrawSplit.End();

            try
            {
                if (mod is not null && mod.Plan.Has(HookId.PostDraw)) mod.PostDraw(owner!, light);
                foreach (var global in post) global.PostDraw(proj, owner!, light);
            }
            catch (Exception e)
            {
                Fail("PostDraw", proj.type, e);
            }
        }
    }

    private static bool HasExtras(Projectile proj) => proj.bobber || proj.aiStyle is 7 or 13 or 15 or 99;

    private static IntPtr OwnTexture(Projectile proj)
    {
        try
        {
            var table = Terraria.GameContent.TextureAssets.Projectile;
            if (proj.type < 0 || proj.type >= table.Length) return IntPtr.Zero;
            return table[proj.type]?.Value?.Pointer ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
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
            if (template is not null && template.Plan.Has(HookId.CanUseGrapple))
                allowed = template.CanUseGrapple(player) != false;

            foreach (var global in canUse)
            {
                if (global.CanUseGrapple(shoot, player) is not { } verdict) continue;
                allowed = verdict;
            }

            if (!allowed) return false;

            var chosen = shoot;
            if (template is not null && template.Plan.Has(HookId.UseGrapple)) template.UseGrapple(player, ref chosen);
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
            var x = (int)(self.position.X + self.width / 2f) / 16;
            var y = (int)(self.position.Y + self.height / 2f) / 16;

            bool? verdict = null;
            if (mod is not null && mod.Plan.Has(HookId.GrappleCanLatchOnTo))
                verdict = mod.GrappleCanLatchOnTo(player, x, y);

            foreach (var global in GlobalProjectileRegistry.For(self, HookId.GrappleCanLatchOnTo))
            {
                var answer = global.GrappleCanLatchOnTo(self, player, x, y);
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

    private static void ApplyGrappleRange(Projectile proj, ModProjectile mod)
    {
        if (mod.AIType > 0) return;
        if (proj.ai[0] != 0f) return;

        var owner = Main.player[proj.owner];
        var center = owner.MountedCenter;
        var dx = center.X - (proj.position.X + proj.width * 0.5f);
        var dy = center.Y - (proj.position.Y + proj.height * 0.5f);
        if (Math.Sqrt(dx * dx + dy * dy) <= mod.GrappleRange()) return;

        var ai = proj.ai;
        ai[0] = 1f;
        proj.ai = ai;
    }

    private static int VanillaHookLimit(int type) => type switch
    {
        165 => 8,
        256 or 372 => 2,
        652 or 73 or 74 => 1,
        >= 646 and <= 649 => 4,
        _ => 3,
    };

    private List<Projectile> LatchedHooks(int owner, int except)
    {
        var found = new List<Projectile>();
        var all = Main.projectile;
        for (var i = 0; i < all.Length; i++)
        {
            if (i == except) continue;
            var hook = all[i];
            if (hook is null || !hook.active || hook.owner != owner) continue;
            if (hook.aiStyle != ModProjectile.HookAIStyle || hook.ai[0] != 2f) continue;
            found.Add(hook);
        }

        return found;
    }

    private static void TrimHooks(int owner, int limit)
    {
        var all = Main.projectile;
        var hooks = 0;
        var oldest = -1;
        var oldestTime = int.MaxValue;
        for (var i = 0; i < all.Length; i++)
        {
            var hook = all[i];
            if (hook is null || !hook.active || hook.owner != owner) continue;
            if (hook.aiStyle != ModProjectile.HookAIStyle || hook.ai[0] != 2f) continue;
            hooks++;
            if (hook.timeLeft < oldestTime)
            {
                oldest = i;
                oldestTime = hook.timeLeft;
            }
        }

        if (hooks > limit && oldest >= 0) all[oldest]?.Kill();
    }

    private static void ApplyGrappleRetreat(Projectile proj, ModProjectile mod)
    {
        if (!proj.active || proj.ai[0] != 1f) return;

        var baseSpeed = VanillaRetreatSpeed(mod.AIType > 0 ? mod.AIType : proj.type);
        if (baseSpeed <= 0) return;

        var speed = baseSpeed;
        mod.GrappleRetreatSpeed(Main.player[proj.owner], ref speed);

        var ratio = speed / baseSpeed;
        if (ratio == 1f) return;

        proj.velocity = new Vector2(proj.velocity.X * ratio, proj.velocity.Y * ratio);
    }

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
                    mod.GrappleTargetPoint(self, ref centerX, ref centerY);
            }

            var speed = VanillaPullSpeed(first.type);
            var firstMod = _binding.InstanceOf(first);
            if (firstMod is not null && firstMod.Plan.Has(HookId.GrapplePullSpeed))
                firstMod.GrapplePullSpeed(self, ref speed);

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
