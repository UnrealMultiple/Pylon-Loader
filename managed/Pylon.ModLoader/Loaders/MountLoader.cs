// MountLoader：坐骑是"每玩家一个 Mount 实例"，派发靠 `mount._type` 反查 ModMount，不需要实体注册表。
// 全链：占号（注册期）→ 安装（Ready setup：Mount.mounts 与 MountID.Sets 12 张表扩长、建 MountData、SetStaticDefaults）
// → 骑/下（SetMount 记上下文、mod 的 SetMount 在尘土里跑；Dismount 先 mod 后原版）
// → 每帧/跳/能力（转给 mod；手机版跳高/跳速无 Player 参数，用 mount 地址反查骑它的玩家）
// → 绘制（每层一次：算出原版默认值 → mod 可改 → 没改交回原版，改了塞进 DrawDataCache 并跳过原版）。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Pylon.ModLoader
{
    /// <summary>ModMount 的钩子接线与 MountData 安装。</summary>
    internal static class MountLoader
    {
        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _installed;

        /// <summary>原版坐骑数量（MountID.Count 在 mod 占号前的值，注册期捕获一次）。</summary>
        private static int _vanillaCount;

        internal static int BaseCount()
        {
            if (_vanillaCount == 0) _vanillaCount = MountID.Count;
            return _vanillaCount;
        }

        internal static void Install(Harmony harmony, ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            _harmony = harmony;
            _logger = logger;

            GameHooks.Install(harmony, logger, typeof(MountLoader), Targets());
            _logger?.Info("[mount] MountLoader 已挂（骑/下/跳/AI/绘制）");
        }

        // 一次全装所有 hook（注册期就要装好），运行期按 `_type` 过滤。
        private static GameHooks.Hook[] Targets() => new[]
        {
            // 骑上：记上下文；mod 的 SetMount 落在尘土里
            new GameHooks.Hook { Type = typeof(Mount), Method = "SetMount", Args = new[] { typeof(int), typeof(Player), typeof(bool) },
                Prefix = true, Handler = nameof(SetMountPrefix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "SetMount", Args = new[] { typeof(int), typeof(Player), typeof(bool) },
                Handler = nameof(SetMountPostfix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "DoSpawnDust", Args = new[] { typeof(Player), typeof(bool) },
                Prefix = true, Handler = nameof(SpawnDustPrefix) },
            // 下来
            new GameHooks.Hook { Type = typeof(Mount), Method = "Dismount", Args = new[] { typeof(Player), typeof(bool) },
                Prefix = true, Handler = nameof(DismountPrefix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "Dismount", Args = new[] { typeof(Player), typeof(bool) },
                Handler = nameof(DismountPostfix) },
            // 每帧 / 跳跃 / 能力
            new GameHooks.Hook { Type = typeof(Mount), Method = "UpdateEffects", Args = new[] { typeof(Player) },
                Prefix = true, Handler = nameof(UpdateEffectsPrefix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "UpdateFrame", Args = new[] { typeof(Player), typeof(int), typeof(Vector2) },
                Prefix = true, Handler = nameof(UpdateFramePrefix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "JumpHeight", Args = new[] { typeof(float) }, Handler = nameof(JumpHeightPostfix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "JumpSpeed", Args = new[] { typeof(float) }, Handler = nameof(JumpSpeedPostfix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "UseAbility", Args = new[] { typeof(Player), typeof(Vector2), typeof(bool) },
                Handler = nameof(UseAbilityPostfix) },
            new GameHooks.Hook { Type = typeof(Mount), Method = "AimAbility", Args = new[] { typeof(Player), typeof(Vector2) },
                Prefix = true, Handler = nameof(AimAbilityPrefix) },
            // 绘制
            new GameHooks.Hook { Type = typeof(Mount), Method = "Draw",
                Args = new[] { typeof(PlayerDrawSet), typeof(int), typeof(Player), typeof(Vector2), typeof(Color),
                               typeof(SpriteEffects), typeof(float) },
                Prefix = true, Handler = nameof(DrawPrefix) },
            // Pylon 专用：游戏内容初始化/换语言会重建 Mount.mounts，mod 槽位变 null → 每帧 NullReferenceException。
            new GameHooks.Hook { Type = typeof(Mount), Method = "Initialize", Args = Type.EmptyTypes, Handler = nameof(InitializePostfix) },
        };

        /// <summary>诊断：补丁目标是否都能找到（PC 上也能跑，.diag/targetprobe 用）。</summary>
        internal static List<string> MissingTargets()
        {
            var missing = new List<string>();
            foreach (var target in Targets())
            {
                if (GameHooks.Find(target.Type, target.Method, target.Args) == null) missing.Add(target.Label);
                if (typeof(MountLoader).GetMethod(target.Handler, BindingFlags.Static | BindingFlags.NonPublic) == null)
                    missing.Add("补丁方法 " + target.Handler);
            }
            return missing;
        }

        /// <summary>Mount.Initialize 重建了 mounts 表：把 mod 的 MountData 填回去（Pylon 专用，见 Reapply）。</summary>
        private static void InitializePostfix() => Reapply();

        // ================= 扩表 + MountData（Ready 的 setup 阶段） =================

        // 层名 → MountData 字段：Back→backTexture、FrontGlow→frontTextureGlow，后缀顺序同 MountTextureType；interop 里可直接赋值，不需反射。
        private static readonly (string Suffix, Action<Mount.MountData, Asset<Texture2D>> Set)[] Layers =
        {
            ("_Back", (data, asset) => data.backTexture = asset),
            ("_BackGlow", (data, asset) => data.backTextureGlow = asset),
            ("_BackExtra", (data, asset) => data.backTextureExtra = asset),
            ("_BackExtraGlow", (data, asset) => data.backTextureExtraGlow = asset),
            ("_Front", (data, asset) => data.frontTexture = asset),
            ("_FrontGlow", (data, asset) => data.frontTextureGlow = asset),
            ("_FrontExtra", (data, asset) => data.frontTextureExtra = asset),
            ("_FrontExtraGlow", (data, asset) => data.frontTextureExtraGlow = asset),
        };

        /// <summary>已建好 MountData 的坐骑（Reapply 靠它重填表）。</summary>
        private static readonly List<ModMount> Pending = new();

        /// <summary>内容就绪（表已定型）后调一次：建 MountData → 扩表写槽 → SetStaticDefaults。</summary>
        internal static void Setup(ModLogger logger)
        {
            if (logger != null) _logger = logger;

            var built = BuildData();
            Reapply();
            if (!built) return;

            // Safe.Run：所有 MountData 都进表之后再跑 SetStaticDefaults。
            foreach (var mount in Pending) Safe.Run(mount.Name + ".SetStaticDefaults", mount.SetStaticDefaults);

            // buff 标记要在 SetStaticDefaults 之后——作者在那里写 BuffID.Sets.MountType[Type]。
            ApplyBuffFlags();

            _logger?.Info($"[mount] 坐骑安装：{Pending.Count} 匹（类型 {Pending[0].Type}..{MountID.Count - 1}）");
        }

        /// <summary>每匹坐骑建一个 MountData：各层贴图 + 帧默认值；只建一次。</summary>
        private static bool BuildData()
        {
            if (Pending.Count > 0) return false;

            foreach (var mount in ModContent.GetContent<ModMount>())
            {
                var data = new Mount.MountData();
                foreach (var (suffix, set) in Layers)
                {
                    var asset = LoadLayer(mount, suffix);
                    if (asset != null) set(data, asset);
                }
                InitFrameDefaults(data);
                mount.MountData = data;
                Pending.Add(mount);
            }
            return Pending.Count > 0;
        }

        /// <summary>&lt;Texture&gt;&lt;层&gt;.png 存在才算这一层；没有就留空（那一层不画）。</summary>
        private static Asset<Texture2D> LoadLayer(ModMount mount, string suffix)
        {
            var path = mount.TexturePath + suffix + ".png";
            try
            {
                return ModContent.HasAsset(mount.Mod, path) ? mount.Mod.LoadAsset(path) : null;
            }
            catch (Exception e)
            {
                Safe.Report(mount.Name + " (" + suffix + ")", e);
                return null;
            }
        }

        /// <summary>把 MountData 写回 Mount.mounts；**必须可重复调**：Mount.Initialize 会按 MountID.Count 重建数组，
        /// mod 槽位变 null → 每帧 NullReferenceException（真机卡死）。</summary>
        internal static void Reapply()
        {
            if (Pending.Count == 0) return;
            var total = BaseCount() + Pending.Count;
            try
            {
                // Mount.mounts：表没扩会崩（mounts[type] 越界）。
                // ⚠ 全程在**托管数组**上填好再一次性隐式转换赋值（内部原生拷贝）；
                // 在新 il2cpp 数组上逐元素写会 SIGSEGV（0x7cf774，真机踩过两次）。
                var table = Mount.mounts;
                var grown = new Mount.MountData[total];
                if (table != null) for (var i = 0; i < table.Length && i < total; i++) grown[i] = table[i];

                // MountID.Sets 的 bool 数组同样要变长（同样用托管数组赋值）。
                GrowSets(total);
                MountID.Count = total;

                foreach (var mount in Pending) grown[mount.Type] = mount.MountData;

                Mount.mounts = grown;   // 托管数组 → 原生数组（隐式转换，一次性拷贝）

                ApplyBuffFlags();

                // 重填是静默的（Mount.Initialize 后可能调多次），首次安装由 Setup 打日志。
            }
            catch (Exception e) { _logger?.Warn($"[mount] 安装失败：{e.Message}"); }
        }

        /// <summary>MountID.Sets 的 12 张 bool 表跟着变长（原版写死按 MountID.Count 索引）。</summary>
        private static void GrowSets(int total)
        {
            MountID.Sets.CanUseHooks = Grow(MountID.Sets.CanUseHooks, total);
            MountID.Sets.CanDash = Grow(MountID.Sets.CanDash, total);
            MountID.Sets.DoesNotOverrideBodyFrames = Grow(MountID.Sets.DoesNotOverrideBodyFrames, total);
            MountID.Sets.DoesNotOverrideLegFrames = Grow(MountID.Sets.DoesNotOverrideLegFrames, total);
            MountID.Sets.DoesNotOverrideBackpackDraw = Grow(MountID.Sets.DoesNotOverrideBackpackDraw, total);
            MountID.Sets.DoesNotOverrideWings = Grow(MountID.Sets.DoesNotOverrideWings, total);
            MountID.Sets.IsRollerSkates = Grow(MountID.Sets.IsRollerSkates, total);
            MountID.Sets.Cart = Grow(MountID.Sets.Cart, total);
            MountID.Sets.IsTransformationMount = Grow(MountID.Sets.IsTransformationMount, total);
            MountID.Sets.PlayerIsHidden = Grow(MountID.Sets.PlayerIsHidden, total);
            MountID.Sets.DontHoldItems = Grow(MountID.Sets.DontHoldItems, total);
            MountID.Sets.DontDismountWhenCCed = Grow(MountID.Sets.DontDismountWhenCCed, total);
        }

        private static bool[] Grow(Il2CppStructArray<bool> old, int total)
        {
            var grown = new bool[total];
            if (old != null) for (var i = 0; i < old.Length && i < total; i++) grown[i] = old[i];
            return grown;
        }

        /// <summary>BuffID.Sets.MountType 指向坐骑的 buff 不显示时间、不存档；重建表后标记会丢，重填时也做一遍。</summary>
        private static void ApplyBuffFlags()
        {
            var mountOf = BuffID.Sets.MountType;
            var noTime = Main.buffNoTimeDisplay;
            var noSave = Main.buffNoSave;
            if (mountOf == null || noTime == null || noSave == null) return;

            foreach (var buff in ModContent.GetContent<ModBuff>())
            {
                var type = buff.Type;
                if (type < 0 || type >= mountOf.Length || mountOf[type] < 0) continue;
                if (type >= noTime.Length || type >= noSave.Length) continue;
                noTime[type] = true;
                noSave[type] = true;
            }
        }

        /// <summary>MountData 的尺寸/帧数默认值：作者没写时也要能安全画（Draw 要除 totalFrames、get_PlayerOffset 要索引 playerYOffsets）。</summary>
        private static void InitFrameDefaults(Mount.MountData data)
        {
            var texture = data.backTexture ?? data.frontTexture;
            var width = 0;
            var height = 0;
            if (texture != null)
            {
                try { PylonTexture.Size(texture.Value, out width, out height); }
                catch { }
            }

            if (data.textureWidth <= 0) data.textureWidth = width;
            if (data.textureHeight <= 0) data.textureHeight = height;
            if (data.totalFrames <= 0) data.totalFrames = 1;

            // playerYOffsets 是原版每匹坐骑都会建好的 int[totalFrames]；新 MountData 里是 null，
            // Mount.get_PlayerOffset 直接索引它 → 每帧 NullReferenceException（真机：骑上去玩家/UI 全没了）。
            var frames = data.totalFrames;
            if (data.playerYOffsets == null || data.playerYOffsets.Length < frames)
            {
                var offsets = new int[frames];
                var old = data.playerYOffsets;
                if (old != null) for (var i = 0; i < old.Length && i < frames; i++) offsets[i] = old[i];
                data.playerYOffsets = offsets;
            }

            // 七组动画：作者不写就用"单帧、停在 0"（interop 字段是属性，不能 ref 传，只能逐组写）。
            if (data.standingFrameCount <= 0) { data.standingFrameStart = 0; data.standingFrameCount = 1; }
            if (data.runningFrameCount <= 0) { data.runningFrameStart = 0; data.runningFrameCount = 1; }
            if (data.inAirFrameCount <= 0) { data.inAirFrameStart = 0; data.inAirFrameCount = 1; }
            if (data.flyingFrameCount <= 0) { data.flyingFrameStart = 0; data.flyingFrameCount = 1; }
            if (data.swimFrameCount <= 0) { data.swimFrameStart = 0; data.swimFrameCount = 1; }
            if (data.dashingFrameCount <= 0) { data.dashingFrameStart = 0; data.dashingFrameCount = 1; }
            if (data.idleFrameCount <= 0) { data.idleFrameStart = 0; data.idleFrameCount = 1; }
        }

        // ================= 小工具 =================

        /// <summary>按类型反查 ModMount。</summary>
        private static ModMount Of(int type) => ModContent.Find(ContentKind.Mount, type) as ModMount;

        /// <summary>只有 mod 坐骑才分派（_type >= 原版数）。</summary>
        private static ModMount Of(Mount mount) =>
            mount == null || mount._type < BaseCount() ? null : Of(mount._type);

        private static long Address(Il2CppObjectBase obj) => obj == null ? 0 : (long)obj.Pointer;

        /// <summary>手机版跳高/跳速无 Player 参数：用 mount 地址反查骑它的玩家。</summary>
        private static Player OwnerOf(Mount mount)
        {
            var address = Address(mount);
            if (address == 0) return null;

            var local = Main.player[Main.myPlayer];
            if (local != null && Address(local.mount) == address) return local;

            // 按数组长度扫（多出的槽位没人骑，与写死上限等价）。
            var players = Main.player;
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player != null && player.active && Address(player.mount) == address) return player;
            }
            return null;
        }

        // ================= 骑上 / 下来 =================

        /// <summary>一次 SetMount 的上下文：哪匹 mod 坐骑、它的 SetMount 轮到没有。</summary>
        private sealed class MountCall
        {
            public ModMount Mod;
            public bool Called;
        }

        private static MountCall _setting;
        private static bool _dismountSkipDust;

        /// <summary>玩家 → 坐骑数据（whoAmI 做键，SetMount/Dismount 时清）。</summary>
        private static readonly Dictionary<int, object> Specific = new();

        internal static object SpecificData(Player player) =>
            player != null && Specific.TryGetValue(player.whoAmI, out var value) ? value : null;

        internal static void SetSpecificData(Player player, object value)
        {
            if (player == null) return;
            if (value == null) Specific.Remove(player.whoAmI);
            else Specific[player.whoAmI] = value;
        }

        /// <summary>SetMount 开始：换 mod 坐骑了才建上下文，并清掉这个玩家的旧数据。</summary>
        private static bool SetMountPrefix(Mount __instance, int m, Player mountedPlayer, bool ignoreEffect)
        {
            // 上一次 SetMount 没走到 Postfix（原版抛异常）就把上下文丢掉。
            _setting = null;
            try
            {
                var mod = Of(m);
                if (mod == null || __instance._type == m) return true;
                Specific.Remove(mountedPlayer.whoAmI);
                _setting = new MountCall { Mod = mod };
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("Mount.SetMount", e);
                return true;
            }
        }

        /// <summary>SetMount 结束：mod 的 SetMount 本该在尘土里跑；原版没撒尘土就在这里补一次（skipDust=false）。</summary>
        private static void SetMountPostfix(Mount __instance, int m, Player mountedPlayer)
        {
            var setting = _setting;
            _setting = null;
            if (setting == null || setting.Called || __instance._type != m) return;
            try
            {
                var skip = false;
                setting.Mod.SetMount(mountedPlayer, ref skip);
            }
            catch (Exception e) { Safe.Report(setting.Mod.Name + ".SetMount", e); }
        }

        /// <summary>坐骑的尘土：骑上时在这里跑 mod 的 SetMount（FinalizeMountData 之后、原版尘土之前）；
        /// 下来时 mod 说跳过尘土也在这里拦。不能按 `_type` 过滤——SetMount 期间 `_type` 可能还没换成 m。</summary>
        private static bool SpawnDustPrefix(Mount __instance, Player mountedPlayer, bool isDismounting)
        {
            try
            {
                var setting = _setting;
                if (!isDismounting && setting != null && !setting.Called)
                {
                    setting.Called = true;
                    var skip = false;
                    try { setting.Mod.SetMount(mountedPlayer, ref skip); }
                    catch (Exception e) { Safe.Report(setting.Mod.Name + ".SetMount", e); }
                    if (skip) return false;   // mod 要求跳过原版尘土
                }
                if (isDismounting && _dismountSkipDust) return false;
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("Mount.DoSpawnDust", e);
                return true;
            }
        }

        /// <summary>下来：先跑 mod 的 Dismount（拿 skipDust），再交回原版。</summary>
        private static bool DismountPrefix(Mount __instance, Player mountedPlayer, bool ignoreEffect)
        {
            try
            {
                var mod = Of(__instance);
                var skip = false;
                if (mod != null)
                {
                    try { mod.Dismount(mountedPlayer, ref skip); }
                    catch (Exception e) { Safe.Report(mod.Name + ".Dismount", e); }
                }
                _dismountSkipDust = skip;
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("Mount.Dismount", e);
                return true;
            }
        }

        /// <summary>下来之后清掉这个玩家的坐骑数据。</summary>
        private static void DismountPostfix(Mount __instance, Player mountedPlayer)
        {
            _dismountSkipDust = false;
            if (mountedPlayer != null) Specific.Remove(mountedPlayer.whoAmI);
        }

        // ================= 每帧 / 跳跃 / 能力 =================

        /// <summary>每帧，在游戏自己的效果之前。</summary>
        private static bool UpdateEffectsPrefix(Mount __instance, Player mountedPlayer)
        {
            try
            {
                var mod = Of(__instance);
                if (mod != null) mod.UpdateEffects(mountedPlayer);
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("Mount.UpdateEffects", e);
                return true;
            }
        }

        /// <summary>false 跳过游戏动画（只有明确 false 才跳过，抛异常照跑原版）。</summary>
        private static bool UpdateFramePrefix(Mount __instance, Player mountedPlayer, int state, Vector2 velocity)
        {
            try
            {
                var mod = Of(__instance);
                if (mod != null && !mod.UpdateFrame(mountedPlayer, state, velocity)) return false;
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("Mount.UpdateFrame", e);
                return true;
            }
        }

        /// <summary>游戏算完再交给 mod 改；手机版没有 Player 参数 → 反查。</summary>
        private static void JumpHeightPostfix(Mount __instance, float xVelocity, ref int __result)
        {
            try
            {
                var mod = Of(__instance);
                if (mod == null) return;
                var player = OwnerOf(__instance);
                if (player != null) mod.JumpHeight(player, ref __result, xVelocity);
            }
            catch (Exception e) { Safe.Report("Mount.JumpHeight", e); }
        }

        /// <summary>同 JumpHeight。</summary>
        private static void JumpSpeedPostfix(Mount __instance, float xVelocity, ref float __result)
        {
            try
            {
                var mod = Of(__instance);
                if (mod == null) return;
                var player = OwnerOf(__instance);
                if (player != null) mod.JumpSpeed(player, ref __result, xVelocity);
            }
            catch (Exception e) { Safe.Report("Mount.JumpSpeed", e); }
        }

        /// <summary>原版先跑，mod 之后。</summary>
        private static void UseAbilityPostfix(Mount __instance, Player mountedPlayer, Vector2 mousePosition, bool toggleOn)
        {
            try
            {
                var mod = Of(__instance);
                if (mod != null) mod.UseAbility(mountedPlayer, mousePosition, toggleOn);
            }
            catch (Exception e) { Safe.Report("Mount.UseAbility", e); }
        }

        /// <summary>mod 先跑，返回值仍是原版的。</summary>
        private static bool AimAbilityPrefix(Mount __instance, Player mountedPlayer, Vector2 mousePosition)
        {
            try
            {
                var mod = Of(__instance);
                if (mod != null) mod.AimAbility(mountedPlayer, mousePosition);
            }
            catch (Exception e) { Safe.Report("Mount.AimAbility", e); }
            return true;
        }

        // ================= 绘制（每层一次） =================

        private static bool DrawPrefix(Mount __instance, PlayerDrawSet playerDrawData, int drawType, Player drawPlayer,
                                       Vector2 Position, Color drawColor, SpriteEffects playerEffect, float shadow)
        {
            try
            {
                var mod = Of(__instance);
                if (mod == null) return true;
                // false = 已经画过（用 mod 的值，或这层不画）→ 跳过原版。
                return !DrawMod(mod, __instance, playerDrawData, drawType, drawPlayer, Position, drawColor, playerEffect, shadow);
            }
            catch (Exception e)
            {
                Safe.Report("Mount.Draw", e);
                return true;
            }
        }

        /// <summary>drawType → 这层的两张贴图（0 Back 1 BackExtra 2 Front 3 FrontExtra）。</summary>
        private static (Asset<Texture2D> Texture, Asset<Texture2D> Glow) LayerOf(Mount.MountData data, int drawType)
        {
            switch (drawType)
            {
                case 0: return (data.backTexture, data.backTextureGlow);                  // Mount.DrawBack
                case 1: return (data.backTextureExtra, data.backTextureExtraGlow);        // Mount.DrawBackExtra
                case 2: return (data.frontTexture, data.frontTextureGlow);                // Mount.DrawFront
                case 3: return (data.frontTextureExtra, data.frontTextureExtraGlow);      // Mount.DrawFrontExtra
                default: return (null, null);
            }
        }

        /// <summary>原版对一匹"没有特例"的坐骑会怎么画，就交给 mod 改。</summary>
        private static bool DrawMod(ModMount mod, Mount mount, PlayerDrawSet set, int drawType, Player player,
                                    Vector2 position, Color drawColor, SpriteEffects effect, float shadow)
        {
            var data = mount._data;
            if (data == null) return false;

            var (textureAsset, glowAsset) = LayerOf(data, drawType);
            var texture = textureAsset != null ? textureAsset.Value : null;
            if (texture == null) return false;   // 这一层没有贴图：交回原版（它也不会画）
            var glow = glowAsset != null ? glowAsset.Value : null;

            var screen = Main.screenPosition;
            var xOffset = mount.XOffset * (player.direction <= 0 ? -1 : 1);
            var yOffset = mount.YOffset + mount.PlayerOffset;
            var frameHeight = (int)((float)data.textureHeight / data.totalFrames);

            var glowColor = new Color(drawColor.R * 0.25f + 191.25f, drawColor.G * 0.25f + 191.25f,
                                      drawColor.B * 0.25f + 191.25f, drawColor.A * 0.25f + 191.25f);

            var spriteEffects = effect;
            if (mount.Cart && Math.Sign(player.velocity.X) == -player.direction)
                spriteEffects = (SpriteEffects)((int)effect ^ 1);

            // 原版这批值交给 mod 改。
            var drawTexture = texture;
            var drawGlow = glow;
            var drawPosition = new Vector2(
                (int)(position.X - screen.X + player.width / 2f + xOffset),
                (int)(position.Y - screen.Y + player.height / 2f + yOffset));
            var frame = new Rectangle(0, frameHeight * mount._frame, data.textureWidth, frameHeight - 2);
            var color = new Color(drawColor.R, drawColor.G, drawColor.B, drawColor.A);
            var glowDrawColor = glowColor;
            var rotation = 0f;
            var drawOrigin = mount.Origin;
            var drawScale = 1f;

            var before = Snapshot(drawTexture, drawGlow, drawPosition, frame, color, glowDrawColor,
                                  rotation, spriteEffects, drawOrigin, drawScale);

            if (!mod.Draw(set, drawType, player, ref drawTexture, ref drawGlow, ref drawPosition, ref frame,
                          ref color, ref glowDrawColor, ref rotation, ref spriteEffects, ref drawOrigin, ref drawScale, shadow))
                return true;   // mod 说这层不画

            if (Snapshot(drawTexture, drawGlow, drawPosition, frame, color, glowDrawColor,
                         rotation, spriteEffects, drawOrigin, drawScale) == before)
                return false;  // 什么都没改：交回原版画

            // 改了值：用 mod 的值自己塞绘制数据（跳过原版）。
            if (drawTexture != null)
                AddDrawData(set, drawTexture, drawPosition, frame, color, rotation, drawOrigin, drawScale, spriteEffects);
            if (drawGlow != null)
            {
                var alpha = color.A / 255f;
                AddDrawData(set, drawGlow, drawPosition, frame,
                    new Color(glowDrawColor.R * alpha, glowDrawColor.G * alpha, glowDrawColor.B * alpha, glowDrawColor.A * alpha),
                    rotation, drawOrigin, drawScale, spriteEffects);
            }
            return true;
        }

        /// <summary>Mount.currentShader + NewDrawData + AddDrawData。</summary>
        private static void AddDrawData(PlayerDrawSet set, Texture2D texture, Vector2 position, Rectangle source,
                                        Color color, float rotation, Vector2 origin, float scale, SpriteEffects effect)
        {
            var data = ModMount.NewDrawData(texture, position, source, color, rotation, origin, scale, effect);
            data.shader = Mount.currentShader;
            ModMount.AddDrawData(set, data);
        }

        /// <summary>把交给 mod 的那批值拍成一条可比对的串。</summary>
        private static string Snapshot(Texture2D texture, Texture2D glow, Vector2 position, Rectangle frame,
                                       Color color, Color glowColor, float rotation, SpriteEffects effect,
                                       Vector2 origin, float scale) =>
            string.Join(",", Address(texture), Address(glow), position.X, position.Y, frame.X, frame.Y, frame.Width, frame.Height,
                color.R, color.G, color.B, color.A, glowColor.R, glowColor.G, glowColor.B, glowColor.A,
                rotation, (int)effect, origin.X, origin.Y, scale);
    }
}
