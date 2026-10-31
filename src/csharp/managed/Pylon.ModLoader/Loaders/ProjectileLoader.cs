// 层：Mod 框架 / Loaders —— ProjectileLoader（ModProjectile/GlobalProjectile 的每帧钩子接线）。
//
//   · AI 链：OnSpawn（首帧）→ PreAI → 游戏 AI（AIType 临时换 type）→ AI → PostAI；命中/移动撞块/死亡/绘制/钩爪。
//   · 每个 hook 内部按"是不是 mod 弹幕 / 有没有 Global"早退（vanilla 只多一次字典查找）。
//   · NumGrappleHooks 还没接（要临时改其它钩爪的 aiStyle，风险高）；覆写它的 Mod 收到一次性警告。
//   · 补丁方法体只调静态方法（IL2CPP 约束）。
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace Pylon.ModLoader
{
    /// <summary>ModProjectile / GlobalProjectile 的每帧钩子接线。</summary>
    internal sealed class ProjectileLoader : ILoaderModule
    {
        public ProjectileLoader(ModLogger logger) => _logger = logger;

        public string Name => "projectile";

        public int Order => 200;

        private static ModLogger _logger;

        /// <summary>注册期装一次（幂等）：第一个 mod 注册弹幕时由管线调。</summary>
        public bool Install()
        {
            On.Terraria.Projectile.AI += AiHook;
            On.Terraria.Projectile.ApplyStatsFromSource += OriginalDamageHook;
            On.Terraria.Projectile.HandleMovement += MovementHook;
            On.Terraria.Projectile.GetCollisionParams += CollisionParamsHook;
            On.Terraria.Projectile.Kill += KillHook;
            On.Terraria.Projectile.Damage += DamageHook;
            On.Terraria.Projectile.Damage_GetHitbox += DamageHitboxHook;
            On.Terraria.Projectile.Colliding += CollidingHook;
            On.Terraria.Projectile.CanCutTiles += CanCutTilesHook;
            On.Terraria.Projectile.CutTiles += CutTilesHook;
            On.Terraria.Projectile.GetAlpha += GetAlphaHook;
            On.Terraria.Projectile.AI_007_GrapplingHooks_CanTileBeLatchedOnTo += LatchHook;
            On.Terraria.Player.FireGrapple += FireGrappleHook;
            On.Terraria.Main.DrawProjDirect += DrawProjDirectHook;

            // OnSpawn（浮点版 NewProjectile 之后）：没有 Global 覆写时钩子内部自己早退。
            On.Terraria.Projectile.NewProjectile_1 += NewProjectileHook;

            _logger?.Info("[proj] ProjectileLoader 已挂（AI/移动/命中/死亡/绘制/钩爪）");
            _logger?.Info("[proj] GlobalProjectile.OnSpawn 已挂");

            // 签名索引里没有 Terraria.Player.GetGrapplingForces 的 On.* 事件：实现保留，暂不订阅。
            _logger?.Warn("[proj] 找不到 On.Terraria.Player.GetGrapplingForces，GrappleTargetPoint/GrapplePullSpeed 暂不可用");
            return true;
        }

        // ================= 小工具 =================

        private static ModProjectile Of(Projectile proj) => ProjectileEntities.InstanceOf(proj);

        private static long Address(Projectile proj) => ProjectileEntities.Address(proj);

        private static bool InRange(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<bool> array, int index) =>
            array != null && index >= 0 && index < array.Length;

        /// <summary>读一个 ai 槽（ai 是定长结构体属性，直接改字段不会回写）。</summary>
        private static float AI(Projectile p, int index) => p.ai[index];

        /// <summary>写一个 ai 槽：整体取出来改完再写回（C# struct 回写语义）。</summary>
        private static void SetAI(Projectile p, int index, float value)
        {
            var ai = p.ai;
            ai[index] = value;
            p.ai = ai;
        }

        private static Vector2 Center(Projectile p) =>
            new Vector2(p.position.X + p.width * 0.5f, p.position.Y + p.height * 0.5f);

        /// <summary>弹幕中心的格子光照（Lighting.GetColor 取）。</summary>
        private static Color LightAt(Projectile p)
        {
            try
            {
                var c = Center(p);
                if (float.IsNaN(c.X) || float.IsNaN(c.Y) || float.IsInfinity(c.X) || float.IsInfinity(c.Y)) return Color.White;
                return Lighting.GetColor((int)(c.X / 16f), (int)(c.Y / 16f));
            }
            catch { return Color.White; }
        }

        // ================= AI =================

        private sealed class AiFrame
        {
            public ModProjectile Mod;
            public bool Globals;
            public bool Swapped;
            public int SavedType;
            public bool SkipModAi;
            public bool Grapple;
            public AiFrame Outer;
        }

        private static AiFrame _ai;
        private static readonly System.Collections.Generic.HashSet<int> _aiErrLogged = new();

        /// <summary>游戏 AI：旧 prefix 决定跑不跑，旧 postfix 之后照跑（PreAI 否决时也要 PostAI）。</summary>
        private static void AiHook(On.Terraria.Projectile.orig_AI orig, Projectile self)
        {
            if (AiPrefix(self)) orig(self);
            AiPostfix(self);
        }

        private static bool AiPrefix(Projectile __instance)
        {
            var p = __instance;
            var mod = Of(p);
            var globals = GlobalProjectileRegistry.AnyFor(p, "PreAI", "AI", "PostAI");
            if (mod == null && !globals) return true;

            var frame = new AiFrame { Mod = mod, Globals = globals, SavedType = p.type, Outer = _ai };
            _ai = frame;
            try
            {
                if (mod != null && !mod.Spawned)
                {
                    mod.Spawned = true;
                    if (mod.Plan.Has("OnSpawn")) mod.OnSpawn(p);
                }

                var go = true;
                if (mod != null && mod.Plan.Has("PreAI")) go = mod.PreAI(p);
                if (go && globals)
                {
                    foreach (var g in GlobalProjectileRegistry.For(p, "PreAI"))
                        if (!g.PreAI(p)) { go = false; break; }
                }
                if (!go) { frame.SkipModAi = true; return false; }

                // AIType：只在游戏 AI 期间把 type 换成它。
                if (mod != null && mod.AIType > 0)
                {
                    frame.Swapped = true;
                    p.type = mod.AIType;
                }
                else
                {
                    frame.Swapped = false;
                    frame.SavedType = p.type;
                }

                // 钩爪（aiStyle 7）：射程判断在游戏 AI 之前（超过射程就往回收）。
                frame.Grapple = p.aiStyle == 7;
                if (frame.Grapple && mod != null && mod.Plan.Has("GrappleRange")) GrappleRange(p, mod);

                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] PreAI 异常：{e.Message}");
                return true;
            }
        }

        private static void AiPostfix(Projectile __instance)
        {
            var frame = _ai;
            if (frame == null) return;
            var p = __instance;
            try
            {
                if (frame.Swapped) p.type = frame.SavedType;

                if (!frame.SkipModAi)
                {
                    if (frame.Mod != null && frame.Mod.Plan.Has("AI")) frame.Mod.AI(p);
                    if (frame.Globals)
                        foreach (var g in GlobalProjectileRegistry.For(p, "AI")) g.AI(p);
                }

                if (frame.Grapple && frame.Mod != null && frame.Mod.Plan.Has("GrappleRetreatSpeed"))
                    GrappleRetreat(p, frame.Mod);

                if (frame.Globals)
                    foreach (var g in GlobalProjectileRegistry.For(p, "PostAI")) g.PostAI(p);
                if (frame.Mod != null && frame.Mod.Plan.Has("PostAI")) frame.Mod.PostAI(p);
            }
            catch (Exception e)
            {
                if (_aiErrLogged.Add(p.type)) _logger?.Warn($"[proj] AI 异常 type={p.type}：{e}");
                else _logger?.Warn($"[proj] AI 异常：{e.Message}");
                if (frame.Swapped) p.type = frame.SavedType;
            }
            finally
            {
                _ai = frame.Outer;
            }
        }

        // ================= originalDamage =================

        private static void OriginalDamageHook(On.Terraria.Projectile.orig_ApplyStatsFromSource orig,
                                               Projectile self, IEntitySource spawnSource)
        {
            orig(self, spawnSource);
            OriginalDamagePostfix(self, spawnSource);
        }

        private static void OriginalDamagePostfix(Projectile __instance, IEntitySource spawnSource)
        {
            try
            {
                var p = __instance;
                if (Of(p) == null || p.originalDamage != 0) return;

                // Il2CppInterop：IL2CPP 接口在托管侧是代理类，不能 as，要用 TryCast 落到目标代理类。
                var item = spawnSource != null ? spawnSource.TryCast<EntitySource_ItemUse>() : null;
                if (item == null) return;
                p.originalDamage = item.Item != null && item.Item.damage >= 0 ? item.Item.damage : p.damage;
            }
            catch (Exception e) { _logger?.Warn($"[proj] originalDamage 异常：{e.Message}"); }
        }

        // ================= 移动 / 撞块 =================

        private sealed class MoveFrame
        {
            public long Address;
            public bool HasCollision;
            public bool Bypass;
            public bool Reported;
            public bool PendingKill;
            public bool Kept;
            public Vector2 Start;
            public Vector2 OldVelocity;
            public Vector2 Anchor;
            public int Width;
            public int Height;
            public bool SavedDecides;
            public bool SavedFall;
            public bool SavedTileCollide;
            public MoveFrame Outer;
        }

        private static MoveFrame _move;

        private static void MovementHook(On.Terraria.Projectile.orig_HandleMovement orig, Projectile self, Vector2 wetVelocity)
        {
            if (MovementPrefix(self, wetVelocity)) orig(self, wetVelocity);
            MovementPostfix(self, wetVelocity);
        }

        private static bool MovementPrefix(Projectile __instance, Vector2 wetVelocity)
        {
            var p = __instance;
            var mod = Of(p);
            var collisionGlobals = GlobalProjectileRegistry.AnyFor(p, "OnTileCollide");
            var styleGlobals = GlobalProjectileRegistry.AnyFor(p, "TileCollideStyle");
            if (mod == null && !collisionGlobals && !styleGlobals) return true;

            var hasCollision = (mod != null && mod.Plan.Has("OnTileCollide")) || collisionGlobals;
            var hasStyle = (mod != null && mod.Plan.Has("TileCollideStyle")) || styleGlobals;
            if (!p.tileCollide || (!hasCollision && !hasStyle)) return true;

            var frame = new MoveFrame
            {
                Address = Address(p),
                HasCollision = hasCollision,
                Start = p.position,
                OldVelocity = p.velocity,
                Outer = _move,
            };
            _move = frame;

            try
            {
                p.GetCollisionParams(out var anchor, out var width, out var height);
                frame.Anchor = anchor;
                frame.Width = width;
                frame.Height = height;

                if (hasStyle)
                {
                    var fallThrough = VanillaFallThrough(p);
                    var allowed = true;
                    if (mod != null && mod.Plan.Has("TileCollideStyle"))
                        allowed = mod.TileCollideStyle(p, ref width, ref height, ref fallThrough, ref anchor);
                    foreach (var g in GlobalProjectileRegistry.For(p, "TileCollideStyle"))
                        if (!g.TileCollideStyle(p, ref width, ref height, ref fallThrough, ref anchor)) { allowed = false; break; }

                    frame.Anchor = anchor;
                    frame.Width = width;
                    frame.Height = height;
                    frame.Bypass = !allowed;

                    frame.SavedDecides = p.decidesManualFallThrough;
                    frame.SavedFall = p.shouldFallThrough;
                    p.decidesManualFallThrough = true;
                    p.shouldFallThrough = fallThrough;
                    if (frame.Bypass) p.tileCollide = false;
                }
                frame.SavedTileCollide = p.tileCollide;
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] TileCollideStyle 异常：{e.Message}");
                return true;
            }
        }

        private static void MovementPostfix(Projectile __instance, Vector2 wetVelocity)
        {
            var frame = _move;
            if (frame == null) return;
            var p = __instance;
            try
            {
                if (frame.Address != Address(p)) return;

                if (!frame.Reported && frame.HasCollision && p.active && p.tileCollide && Collided(p, frame))
                    ReportCollision(p, frame);

                if (frame.Kept && p.active) p.position = new Vector2(frame.Start.X + p.velocity.X, frame.Start.Y + p.velocity.Y);
            }
            catch (Exception e) { _logger?.Warn($"[proj] 撞块处理异常：{e.Message}"); }
            finally
            {
                if (frame.SavedDecides || frame.SavedFall || frame.Bypass)
                {
                    p.decidesManualFallThrough = frame.SavedDecides;
                    p.shouldFallThrough = frame.SavedFall;
                }
                if (frame.Bypass) p.tileCollide = true;
                _move = frame.Outer;
            }
        }

        private static void CollisionParamsHook(On.Terraria.Projectile.orig_GetCollisionParams orig, Projectile self,
                                                ref Vector2 resizeAnchor, ref int colWidth, ref int colHeight)
        {
            orig(self, ref resizeAnchor, ref colWidth, ref colHeight);
            CollisionParamsPostfix(self, ref resizeAnchor, ref colWidth, ref colHeight);
        }

        /// <summary>GetCollisionParams 每帧被调多次：只把 TileCollideStyle 的决定喂回去，不再调 mod。</summary>
        private static void CollisionParamsPostfix(Projectile __instance, ref Vector2 resizeAnchor, ref int colWidth, ref int colHeight)
        {
            var frame = _move;
            if (frame == null || frame.Address != Address(__instance)) return;
            if (frame.Width <= 0 && frame.Height <= 0) return;
            resizeAnchor = frame.Anchor;
            colWidth = frame.Width;
            colHeight = frame.Height;
        }

        private static bool Collided(Projectile p, MoveFrame frame)
        {
            var v = p.velocity;
            return v.X != frame.OldVelocity.X || v.Y != frame.OldVelocity.Y;
        }

        /// <summary>撞块：OnTileCollide 收原始速度；返回 false 保住弹幕（位置按 start+velocity 复原）。</summary>
        private static void ReportCollision(Projectile p, MoveFrame frame)
        {
            frame.Reported = true;
            if (!frame.HasCollision) return;

            var oldVelocity = frame.OldVelocity;
            var allowed = true;
            foreach (var g in GlobalProjectileRegistry.For(p, "OnTileCollide"))
                if (!g.OnTileCollide(p, oldVelocity)) { allowed = false; break; }
            if (allowed)
            {
                var mod = Of(p);
                if (mod != null && mod.Plan.Has("OnTileCollide") && !mod.OnTileCollide(p, oldVelocity)) allowed = false;
            }

            if (!frame.PendingKill) return;
            if (!allowed) frame.Kept = true;
            else if (p.active) p.Kill();
        }

        // ---- 原版 fallThrough（逐条照抄原版）----

        private static readonly int[] NoFallThrough =
        {
            9, 12, 13, 15, 24, 663, 665, 667, 677, 678, 679, 688, 689, 690, 691, 692, 693, 1037, 1049, 1105,
        };

        private static bool VanillaFallThrough(Projectile p)
        {
            if (p.decidesManualFallThrough) return p.shouldFallThrough;

            var type = p.type;
            var style = p.aiStyle;
            var fall = true;
            if (InRange(Main.projPet, type) && Main.projPet[type])
            {
                var owner = Main.player[p.owner];
                fall = owner.position.Y + owner.height - 12 > p.position.Y + p.height;
            }
            if (type == 500 || type == 653 || type == 1018)
            {
                var owner = Main.player[p.owner];
                fall = owner.position.Y + owner.height > p.position.Y + p.height + 4;
            }
            fall = ((fall && type != 378) || style == 62 || style == 66 || type == 317 || type == 373) &&
                   type != 281 && type != 253 && style != 53 || style == 197;
            for (var i = 0; i < NoFallThrough.Length; i++) if (NoFallThrough[i] == type) { fall = false; break; }
            if (style == 10 && InRange(ProjectileID.Sets.FallingBlockDoesNotFallThroughPlatforms, type) &&
                ProjectileID.Sets.FallingBlockDoesNotFallThroughPlatforms[type]) fall = false;
            if (style == 99 && AI(p, 0) == -2) fall = false;
            if (type == 759) fall = true;
            if (type == 1020)
            {
                var rotation = p.rotation % (Math.PI * 2);
                if (rotation >= Math.PI) rotation -= Math.PI * 2;
                if (rotation < -Math.PI) rotation += Math.PI * 2;
                fall = rotation >= Math.PI / 2 || rotation < -Math.PI / 2;
            }
            return fall;
        }

        // ================= Kill / PreKill / OnKill =================

        private static void KillHook(On.Terraria.Projectile.orig_Kill orig, Projectile self)
        {
            if (KillPrefix(self)) orig(self);
        }

        private static bool KillPrefix(Projectile __instance)
        {
            var p = __instance;
            try
            {
                if (!p.active) return true;

                // 移动过程里的撞块死亡先挂起：等 HandleMovement 结束、OnTileCollide 表态后再死。
                var moving = _move;
                if (moving != null && moving.Address == Address(p) && !moving.Reported &&
                    moving.HasCollision && p.tileCollide)
                {
                    moving.PendingKill = true;
                    return false;
                }

                var mod = Of(p);
                var preKill = GlobalProjectileRegistry.For(p, "PreKill");
                var onKill = GlobalProjectileRegistry.For(p, "OnKill");
                if (mod == null && preKill.Count == 0 && onKill.Count == 0) return true;

                var timeLeft = p.timeLeft;
                var allowed = true;
                foreach (var g in preKill)
                    if (!g.PreKill(p, timeLeft)) { allowed = false; break; }
                if (allowed && mod != null && mod.Plan.Has("PreKill") && !mod.PreKill(p, timeLeft)) allowed = false;
                if (!allowed)
                {
                    p.active = false;
                    return false;
                }

                if (mod != null && mod.Plan.Has("OnKill")) mod.OnKill(p, timeLeft);
                foreach (var g in onKill) g.OnKill(p, timeLeft);
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] Kill 异常：{e.Message}");
                return true;
            }
        }

        // ================= 命中 =================

        private sealed class DamageFrame
        {
            public long Address;
            public int Type;
            public bool WasPet;
            public bool PetChanged;
            public bool WasComplex;
            public bool ComplexChanged;
        }

        private static DamageFrame _damage;

        private static void DamageHook(On.Terraria.Projectile.orig_Damage orig, Projectile self)
        {
            if (DamagePrefix(self)) orig(self);
            DamagePostfix(self);
        }

        private static bool DamagePrefix(Projectile __instance)
        {
            var p = __instance;
            try
            {
                var mod = Of(p);
                var anyGlobals = GlobalProjectileRegistry.AnyFor(p, "CanDamage", "MinionContactDamage", "Colliding");
                if (mod == null && !anyGlobals) return true;

                bool? allowedByGlobals = null;
                foreach (var g in GlobalProjectileRegistry.For(p, "CanDamage"))
                {
                    var result = g.CanDamage(p);
                    if (result == false) return false;
                    if (result == true) allowedByGlobals = true;
                }
                if (allowedByGlobals != true && mod != null && mod.Plan.Has("CanDamage") && !mod.CanDamage(p)) return false;

                var contactGlobals = GlobalProjectileRegistry.For(p, "MinionContactDamage");
                var collisionGlobals = GlobalProjectileRegistry.For(p, "Colliding");
                var modContact = mod != null && mod.Plan.Has("MinionContactDamage");
                if (contactGlobals.Count == 0 && collisionGlobals.Count == 0 && !modContact) return true;

                var type = p.type;
                var isPet = InRange(Main.projPet, type) && Main.projPet[type];
                var contact = false;
                if (isPet)
                {
                    if (modContact) contact = mod.MinionContactDamage(p);
                    if (!contact)
                        foreach (var g in contactGlobals)
                            if (g.MinionContactDamage(p)) { contact = true; break; }
                }

                var customCollision = false;
                var wasComplex = false;
                if (collisionGlobals.Count > 0 && InRange(ProjectileID.Sets.IsAComplexCollision, type))
                {
                    wasComplex = ProjectileID.Sets.IsAComplexCollision[type];
                    customCollision = !wasComplex;
                }

                if (!contact && !customCollision) return true;

                var frame = new DamageFrame { Address = Address(p), Type = type };
                if (contact)
                {
                    frame.WasPet = isPet;
                    frame.PetChanged = true;
                    Main.projPet[type] = false;
                }
                if (customCollision)
                {
                    frame.WasComplex = wasComplex;
                    frame.ComplexChanged = true;
                    ProjectileID.Sets.IsAComplexCollision[type] = true;
                }
                _damage = frame;
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] CanDamage 异常：{e.Message}");
                return true;
            }
        }

        private static void DamagePostfix(Projectile __instance)
        {
            var frame = _damage;
            if (frame == null) return;
            try
            {
                if (frame.Address != Address(__instance)) return;
                if (frame.PetChanged && InRange(Main.projPet, frame.Type)) Main.projPet[frame.Type] = frame.WasPet;
                if (frame.ComplexChanged && InRange(ProjectileID.Sets.IsAComplexCollision, frame.Type))
                    ProjectileID.Sets.IsAComplexCollision[frame.Type] = frame.WasComplex;
            }
            catch (Exception e) { _logger?.Warn($"[proj] Damage 还原异常：{e.Message}"); }
            finally { _damage = null; }
        }

        private static bool CollidingHook(On.Terraria.Projectile.orig_Colliding orig, Projectile self,
                                          Rectangle myRect, Rectangle targetRect)
        {
            var p = self;
            try
            {
                bool? result = null;
                foreach (var g in GlobalProjectileRegistry.For(p, "Colliding"))
                {
                    var r = g.Colliding(p, myRect, targetRect);
                    if (r.HasValue) { result = r.Value; break; }
                }
                if (!result.HasValue)
                {
                    var mod = Of(p);
                    if (mod != null && mod.Plan.Has("Colliding")) result = mod.Colliding(p, myRect, targetRect);
                }
                if (!result.HasValue) return orig(p, myRect, targetRect);
                return result.Value;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] Colliding 异常：{e.Message}");
                return orig(p, myRect, targetRect);
            }
        }

        /// <summary>造成伤害的区域：mod 先改，再由 Globals 改。</summary>
        private static Rectangle DamageHitboxHook(On.Terraria.Projectile.orig_Damage_GetHitbox orig, Projectile self)
        {
            var result = orig(self);
            try
            {
                var p = self;
                var mod = Of(p);
                var globals = GlobalProjectileRegistry.For(p, "ModifyDamageHitbox");
                if (mod == null && globals.Count == 0) return result;

                var box = result;
                if (mod != null && mod.Plan.Has("ModifyDamageHitbox")) mod.ModifyDamageHitbox(p, ref box);
                foreach (var g in globals) g.ModifyDamageHitbox(p, ref box);
                return box;
            }
            catch (Exception e) { _logger?.Warn($"[proj] ModifyDamageHitbox 异常：{e.Message}"); return result; }
        }

        // ================= 割草 =================

        private static long _cutting;   // 正在跑原版 CutTiles 的弹幕地址（防重入）

        private static bool CanCutTilesHook(On.Terraria.Projectile.orig_CanCutTiles orig, Projectile self)
        {
            var p = self;
            try
            {
                if (_cutting != 0 && _cutting == Address(p)) return true;

                bool? result = null;
                foreach (var g in GlobalProjectileRegistry.For(p, "CanCutTiles"))
                {
                    var r = g.CanCutTiles(p);
                    if (r.HasValue) { result = r.Value; break; }
                }
                if (!result.HasValue)
                {
                    var mod = Of(p);
                    if (mod != null && mod.Plan.Has("CanCutTiles")) result = mod.CanCutTiles(p);
                }
                if (!result.HasValue) return orig(p);
                return result.Value;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] CanCutTiles 异常：{e.Message}");
                return orig(p);
            }
        }

        private static void CutTilesHook(On.Terraria.Projectile.orig_CutTiles orig, Projectile self)
        {
            if (CutTilesPrefix(self)) orig(self);
            CutTilesPostfix(self);
        }

        private static bool CutTilesPrefix(Projectile __instance)
        {
            try
            {
                var p = __instance;
                var mod = Of(p);
                var globals = GlobalProjectileRegistry.For(p, "CutTiles");
                var modCut = mod != null && mod.Plan.Has("CutTiles");
                if (!modCut && globals.Count == 0) return true;

                if (!p.CanCutTiles()) return false;
                foreach (var g in globals) g.CutTiles(p);
                if (modCut) mod.CutTiles(p);

                _cutting = Address(p);
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] CutTiles 异常：{e.Message}");
                return true;
            }
        }

        private static void CutTilesPostfix(Projectile __instance) => _cutting = 0;

        // ================= GetAlpha / 绘制 =================

        private sealed class DrawFrame
        {
            public long Address;
            public Color Light;
            public DrawFrame Outer;
        }

        private static DrawFrame _draw;

        private static Color GetAlphaHook(On.Terraria.Projectile.orig_GetAlpha orig, Projectile self, Color newColor)
        {
            var p = self;
            try
            {
                var mod = Of(p);
                if (mod == null && !GlobalProjectileRegistry.AnyFor(p, "GetAlpha")) return orig(p, newColor);

                var color = newColor;
                var draw = _draw;
                if (draw != null && draw.Address == Address(p)) color = draw.Light;

                Color? result = null;
                foreach (var g in GlobalProjectileRegistry.For(p, "GetAlpha"))
                {
                    var r = g.GetAlpha(p, color);
                    if (r.HasValue) { result = r.Value; break; }
                }
                if (!result.HasValue && mod != null && mod.Plan.Has("GetAlpha")) result = mod.GetAlpha(p, color);
                if (!result.HasValue) return orig(p, newColor);

                return result.Value;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] GetAlpha 异常：{e.Message}");
                return orig(p, newColor);
            }
        }

        private static void DrawProjDirectHook(On.Terraria.Main.orig_DrawProjDirect orig, Main self,
                                               Projectile proj, Player overridePlayer)
        {
            if (DrawPrefix(self, proj, overridePlayer)) orig(self, proj, overridePlayer);
            DrawPostfix(proj);
        }

        private static bool DrawPrefix(Main __instance, Projectile proj, Player overridePlayer)
        {
            var p = proj;
            try
            {
                var mod = Of(p);
                var pre = GlobalProjectileRegistry.For(p, "PreDraw");
                var post = GlobalProjectileRegistry.For(p, "PostDraw");
                if (mod == null && pre.Count == 0 && post.Count == 0) return true;

                var light = LightAt(p);
                var sprite = true;
                var extras = true;

                foreach (var g in pre)
                    if (!g.PreDraw(p, ref light)) sprite = false;

                if (mod != null)
                {
                    // 手机版附加物没有单独入口：PreDrawExtras 只保留调用语义，返回 false 无法只去掉附加物。
                    if (mod.Plan.Has("PreDrawExtras")) extras = mod.PreDrawExtras(p);
                    if (mod.Plan.Has("PreDraw") && !mod.PreDraw(p, light)) sprite = false;
                }

                // 精灵不画就整只不画（附加物一起走，见上面注释）。
                if (!sprite) return false;

                var frame = new DrawFrame
                {
                    Address = Address(p),
                    Light = light,
                    Outer = _draw,
                };
                _draw = frame;
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] PreDraw 异常：{e.Message}");
                return true;
            }
        }

        private static void DrawPostfix(Projectile proj)
        {
            var frame = _draw;
            if (frame == null) return;
            var p = proj;
            try
            {
                if (frame.Address != Address(p)) return;

                var mod = Of(p);
                if (mod != null && mod.Plan.Has("PostDraw")) mod.PostDraw(p, frame.Light);
                foreach (var g in GlobalProjectileRegistry.For(p, "PostDraw")) g.PostDraw(p, frame.Light);
            }
            catch (Exception e) { _logger?.Warn($"[proj] PostDraw 异常：{e.Message}"); }
            finally { _draw = frame.Outer; }
        }

        // ================= 钩爪 =================

        private static bool _grappleRestore;
        private static int _grappleSavedShoot;

        private static void FireGrappleHook(On.Terraria.Player.orig_FireGrapple orig, Player self, Item grappleItem)
        {
            if (FireGrapplePrefix(self, grappleItem)) orig(self, grappleItem);
            FireGrapplePostfix(self, grappleItem);
        }

        private static bool FireGrapplePrefix(Player __instance, Item grappleItem)
        {
            try
            {
                var player = __instance;
                if (grappleItem == null) return true;
                var shoot = grappleItem.shoot;
                var template = ModContent.Find(ContentKind.Projectile, shoot) as ModProjectile;
                var grappleGlobals = GlobalProjectileRegistry.TemplatesFor("CanUseGrapple");

                if (template == null && grappleGlobals.Count == 0) return true;

                var allowed = true;
                if (template != null && template.Plan.Has("CanUseGrapple")) allowed = template.CanUseGrapple(player, shoot);
                foreach (var g in grappleGlobals)
                {
                    var r = g.CanUseGrapple(shoot, player);
                    if (r.HasValue) allowed = r.Value;
                }
                if (!allowed) return false;

                var chosen = shoot;
                if (template != null && template.Plan.Has("UseGrapple")) chosen = template.UseGrapple(player, shoot);
                foreach (var g in GlobalProjectileRegistry.TemplatesFor("UseGrapple")) g.UseGrapple(player, ref chosen);
                if (chosen <= 0 || chosen == shoot) return true;

                grappleItem.shoot = chosen;
                _grappleRestore = true;
                _grappleSavedShoot = shoot;
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] CanUseGrapple/UseGrapple 异常：{e.Message}");
                return true;
            }
        }

        private static void FireGrapplePostfix(Player __instance, Item grappleItem)
        {
            if (!_grappleRestore) return;
            _grappleRestore = false;
            try { if (grappleItem != null) grappleItem.shoot = _grappleSavedShoot; } catch { }
        }

        /// <summary>AI_007 的落点判断：mod/Global 说 false 就挂不上，说 true 就盖过原版。</summary>
        private static bool LatchHook(On.Terraria.Projectile.orig_AI_007_GrapplingHooks_CanTileBeLatchedOnTo orig,
                                      Projectile self, Tile theTile)
        {
            var p = self;
            try
            {
                var mod = Of(p);
                if (mod == null && !GlobalProjectileRegistry.AnyFor(p, "GrappleCanLatchOnTo")) return orig(p, theTile);

                var player = Main.player[p.owner];
                bool? result = null;
                if (mod != null && mod.Plan.Has("GrappleCanLatchOnTo"))
                    result = mod.GrappleCanLatchOnTo(p, player, theTile);
                foreach (var g in GlobalProjectileRegistry.For(p, "GrappleCanLatchOnTo"))
                {
                    var r = g.GrappleCanLatchOnTo(p, player, theTile);
                    if (r == false) return false;
                    if (r == true) result = true;
                }
                if (!result.HasValue) return orig(p, theTile);
                return result.Value;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[proj] GrappleCanLatchOnTo 异常：{e.Message}");
                return orig(p, theTile);
            }
        }

        /// <summary>射程：超过就往回收（把 ai[0] 置 1，与 AI_007 自己的回收状态一致）。</summary>
        private static void GrappleRange(Projectile p, ModProjectile mod)
        {
            if (mod.AIType > 0) return;      // 借了原版钩爪的 AI：用原版的射程
            if (AI(p, 0) != 0) return;

            var owner = Main.player[p.owner];
            var range = mod.GrappleRange(p);
            var center = owner.MountedCenter;
            var c = Center(p);
            var dx = center.X - c.X;
            var dy = center.Y - c.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > range) SetAI(p, 0, 1);
        }

        /// <summary>收绳速度：按原版基础速度的比例缩放本帧速度。</summary>
        private static void GrappleRetreat(Projectile p, ModProjectile mod)
        {
            if (!p.active || AI(p, 0) != 1) return;

            var type = mod.AIType > 0 ? mod.AIType : p.type;
            var baseSpeed = VanillaRetreatSpeed(type);
            if (baseSpeed <= 0) return;

            var speed = baseSpeed;
            mod.GrappleRetreatSpeed(p, Main.player[p.owner], ref speed);
            var ratio = speed / baseSpeed;
            if (ratio == 1f) return;

            var v = p.velocity;
            p.velocity = new Vector2(v.X * ratio, v.Y * ratio);
        }

        private static float VanillaRetreatSpeed(int type)
        {
            if (type >= 230 && type <= 235) return 11 + (type - 230) * 0.75f;
            if (type >= 486 && type <= 489) return 18;
            if (type >= 646 && type <= 649) return 24;
            switch (type)
            {
                case 32: return 15;
                case 73: return 22;
                case 74: return 22;
                case 315: return 20;
                case 322: return 22;
                case 753: return 15;
                case 446: return 20;
                case 652: return 24;
                case 332: return 17;
                default: return 11;
            }
        }

        private static float VanillaPullSpeed(int type)
        {
            if (type >= 646 && type <= 649) return 16;
            switch (type)
            {
                case 73: return 12.5f;
                case 74: return 12.5f;
                case 315: return 14;
                case 487: return 12;
                default: return 11;
            }
        }

        /// <summary>钩爪把玩家拉过去的力：GrappleTargetPoint（合成一个点）+ GrapplePullSpeed（第一个钩爪的）。</summary>
        /// 注：签名索引里没有 Terraria.Player.GetGrapplingForces 的 On.* 事件，这条先保留实现、暂不订阅。
        private static void GrappleForcesPostfix(Player __instance, Vector2 fromPosition,
                                                 ref float preferedPlayerVelocityX, ref float preferedPlayerVelocityY)
        {
            try
            {
                var player = __instance;
                var count = player.grapCount;
                if (count <= 0) return;

                var grappling = player.grappling;
                if (grappling == null || grappling.Length == 0) return;

                var first = Main.projectile[grappling[0]];
                var firstMod = Of(first);
                var wants = firstMod != null && firstMod.Plan.Has("GrapplePullSpeed") && firstMod.Plan.Has("GrappleTargetPoint");

                var hooks = new List<Projectile>(count);
                var mods = new List<ModProjectile>(count);
                for (var k = 0; k < count && k < grappling.Length; k++)
                {
                    var h = Main.projectile[grappling[k]];
                    if (h == null || !h.active || AI(h, 0) != 2) continue;
                    var t = h.type;
                    if (t == 446 || t == 652 || t == 865) return;   // 原版带自己拉力的钩爪

                    var m = Of(h);
                    if (m != null && m.Plan.Has("GrappleTargetPoint")) wants = true;
                    hooks.Add(h);
                    mods.Add(m);
                }
                if (!wants || hooks.Count == 0) return;

                var x = 0f;
                var y = 0f;
                for (var k = 0; k < hooks.Count; k++)
                {
                    var h = hooks[k];
                    x += h.position.X + h.width * 0.5f;
                    y += h.position.Y + h.height * 0.5f;
                    var m = mods[k];
                    if (m == null || !m.Plan.Has("GrappleTargetPoint")) continue;
                    var gx = x;
                    var gy = y;
                    m.GrappleTargetPoint(h, player, ref gx, ref gy);
                    x = gx;
                    y = gy;
                }

                var dx = x / hooks.Count - fromPosition.X;
                var dy = y / hooks.Count - fromPosition.Y;
                var speed = VanillaPullSpeed(first.type);
                if (firstMod != null && firstMod.Plan.Has("GrapplePullSpeed"))
                    firstMod.GrapplePullSpeed(first, player, ref speed);

                var length = Math.Sqrt(dx * dx + dy * dy);
                var scale = length <= speed || length <= 0 ? 1f : speed / (float)length;
                preferedPlayerVelocityX = dx * scale;
                preferedPlayerVelocityY = dy * scale;
            }
            catch (Exception e) { _logger?.Warn($"[proj] GrapplePullSpeed/GrappleTargetPoint 异常：{e.Message}"); }
        }

        /// <summary>注册期检查：覆写了还没接的钩子时给一条一次性警告（别静默）。</summary>
        internal static void WarnUnwired(Mod mod)
        {
            foreach (var projectile in ModContent.GetContent<ModProjectile>())
            {
                if (projectile.Mod != mod) continue;
                if (Hooks.Overrides(projectile.GetType(), typeof(ModProjectile), "NumGrappleHooks"))
                    Safe.Once("proj.NumGrappleHooks." + projectile.GetType().FullName,
                        $"[proj] {projectile.Name} 覆写了 NumGrappleHooks，但这条钩子还没接（按原版上限处理）", true);
            }
        }

        // ================= GlobalProjectile.OnSpawn =================

        private static int NewProjectileHook(On.Terraria.Projectile.orig_NewProjectile_1 orig,
                                             IEntitySource spawnSource, float X, float Y, float SpeedX, float SpeedY,
                                             int Type, int Damage, float KnockBack, int Owner,
                                             float ai0, float ai1, float ai2, NewProjectileModifier modifer)
        {
            var result = orig(spawnSource, X, Y, SpeedX, SpeedY, Type, Damage, KnockBack, Owner, ai0, ai1, ai2, modifer);
            NewProjectilePostfix(spawnSource, ref result);
            return result;
        }

        private static void NewProjectilePostfix(IEntitySource spawnSource, ref int __result)
        {
            try
            {
                var i = __result;
                if (i < 0) return;
                var all = Main.projectile;
                if (all == null || i >= all.Length) return;
                var p = all[i];
                if (p == null || !p.active) return;
                foreach (var g in GlobalProjectileRegistry.For(p, "OnSpawn")) g.OnSpawn(p, spawnSource);
            }
            catch (Exception e) { _logger?.Warn($"[proj] GlobalProjectile.OnSpawn 异常：{e.Message}"); }
        }
    }
}

