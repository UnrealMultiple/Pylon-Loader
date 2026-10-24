// 层：Mod 框架 / Loaders —— NpcLoader（ModNpc/GlobalNpc 钩子接线：AI/事件/绘制/对话，一个钩子一个补丁）。
//
//   · AI 链：Global/Mod 的 PreAI→AI→PostAI（AIType 期间临时换 type）；每个钩子内部先看类型/Global 表早退。
//   · 不 patch SpriteBatch（真机踩过：Unity 绘制入口被重写会串 SpriteEffects，出现"倒影"重绘）。
//   · 一个 Global 抛错只丢它自己那一次调用；补丁方法体只调静态方法（IL2CPP 约束）。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Graphics.Light;

namespace Pylon.ModLoader
{
    /// <summary>ModNpc / GlobalNpc 的钩子接线。</summary>
    internal static class NpcLoader
    {
        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _installed;

        /// <summary>大师难度（这版是 float，Master = 2）。</summary>
        private const float MasterDifficulty = 2f;

        // ================= 装配 =================

        /// <summary>注册期装一次（幂等）；每个 Mod.Load 都刷新一次 Global 门控（Global 在注册期登记）。</summary>
        internal static void Install(Harmony harmony, ModLogger logger)
        {
            if (!_installed)
            {
                _installed = true;
                _harmony = harmony;
                _logger = logger;
                GameHooks.Install(harmony, logger, typeof(NpcLoader), Targets);
                _logger?.Info("[npc] NpcLoader 已挂（AI/事件/绘制/对话）");
            }

            // 过滤器：没有 Global 覆写这套钩子时，原版 NPC 一个钩子都不用进。
            _anyGlobalAny = GlobalNpcRegistry.AnyWith(GlobalHooks);
            _anyGlobalAI = GlobalNpcRegistry.AnyWith("PreAI", "AI", "PostAI");
        }

        private static GameHooks.Hook[] _targets;

        private static GameHooks.Hook[] Targets => _targets ??= new[]
        {
            // 绘制入口（Pylon 专有）：原版上限会跳过 type >= NPCID.Count，IL2CPP 上改不了，于是自己补画。
            new GameHooks.Hook { Type = typeof(Main), Method = "DrawNPCs", Args = new[] { typeof(bool) },
                Prefix = true, Handler = nameof(DrawNpcsPrefix) },

            // AI（prefix 跑 PreAI 并决定否决，postfix 跑 Mod/Global 的 AI/PostAI）
            new GameHooks.Hook { Type = typeof(NPC), Method = "AI", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(AiPrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "AI", Args = Type.EmptyTypes, Handler = nameof(AiPostfix) },

            // OnSpawn（NewNPC 的 postfix）
            new GameHooks.Hook { Type = typeof(NPC), Method = "NewNPC",
                Args = new[] { typeof(IEntitySource), typeof(int), typeof(int), typeof(int), typeof(int),
                               typeof(float), typeof(float), typeof(float), typeof(float), typeof(int) },
                Handler = nameof(NewNpcPostfix) },

            // ResetEffects（UpdateNPC_BuffSetFlags 的 prefix；同时记 DrawBehind）
            new GameHooks.Hook { Type = typeof(NPC), Method = "UpdateNPC_BuffSetFlags", Args = new[] { typeof(bool) },
                Prefix = true, Handler = nameof(ResetEffectsPrefix) },

            new GameHooks.Hook { Type = typeof(NPC), Method = "checkDead", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(CheckDeadPrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "CheckActive", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(CheckActivePrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "FindFrame", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(FindFramePrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "FindFrame", Args = Type.EmptyTypes, Handler = nameof(FindFramePostfix) },

            // 死亡（NPCLoot 的 prefix + postfix）
            new GameHooks.Hook { Type = typeof(NPC), Method = "NPCLoot", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(LootPrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "NPCLoot", Args = Type.EmptyTypes, Handler = nameof(LootPostfix) },

            new GameHooks.Hook { Type = typeof(NPC), Method = "HitEffect", Args = new[] { typeof(int), typeof(double) }, Handler = nameof(HitEffectPostfix) },

            // 难度/人数缩放（这版是 private，已按 NonPublic 找）
            new GameHooks.Hook { Type = typeof(NPC), Method = "ScaleStats_ByPlayerCount", Args = new[] { typeof(int) }, Handler = nameof(ScalingPostfix) },

            // 对话
            new GameHooks.Hook { Type = typeof(NPC), Method = "GetChat", Args = Type.EmptyTypes, Handler = nameof(GetChatPostfix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "get_CanTalk", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(CanChatPrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "get_CanBeTalkedTo", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(CanChatPrefix) },

            // 绘制（两个 DrawNPCDirect 重载共用一套 prefix/postfix：参数名一致，LightMap/lightRegion 不声明）。
            new GameHooks.Hook { Type = typeof(Main), Method = "DrawNPCDirect",
                Args = new[] { typeof(SpriteBatch), typeof(NPC), typeof(bool), typeof(Vector2) },
                Prefix = true, Handler = nameof(DrawPrefix) },
            new GameHooks.Hook { Type = typeof(Main), Method = "DrawNPCDirect",
                Args = new[] { typeof(SpriteBatch), typeof(NPC), typeof(bool), typeof(Vector2) },
                Handler = nameof(DrawPostfix) },
            new GameHooks.Hook { Type = typeof(Main), Method = "DrawNPCDirect",
                Args = new[] { typeof(SpriteBatch), typeof(NPC), typeof(bool), typeof(Vector2), typeof(LightMap),
                               typeof(Rectangle).MakeByRefType() },
                Prefix = true, Handler = nameof(DrawPrefix) },
            new GameHooks.Hook { Type = typeof(Main), Method = "DrawNPCDirect",
                Args = new[] { typeof(SpriteBatch), typeof(NPC), typeof(bool), typeof(Vector2), typeof(LightMap),
                               typeof(Rectangle).MakeByRefType() },
                Handler = nameof(DrawPostfix) },

            // DrawEffects 改了画色时，把改过的颜色喂给原版绘制
            new GameHooks.Hook { Type = typeof(NPC), Method = "GetNPCColorTintedByBuffs", Args = new[] { typeof(Color) },
                Prefix = true, Handler = nameof(TintedByBuffsPrefix) },

            new GameHooks.Hook { Type = typeof(NPC), Method = "GetAlpha", Args = new[] { typeof(Color) }, Prefix = true, Handler = nameof(GetAlphaPrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "GetBossHeadTextureIndex", Args = Type.EmptyTypes, Handler = nameof(BossHeadSlotPostfix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "GetBossHeadRotation", Args = Type.EmptyTypes, Handler = nameof(BossHeadRotationPostfix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "GetBossHeadSpriteEffects", Args = Type.EmptyTypes, Handler = nameof(BossHeadEffectsPostfix) },
            new GameHooks.Hook { Type = typeof(Main), Method = "CacheNPCDraws", Args = Type.EmptyTypes, Handler = nameof(CacheDrawsPostfix) },
        };

        /// <summary>诊断：补丁目标是否都能找到（PC 上也能跑，不装补丁）。</summary>
        internal static List<string> MissingTargets()
        {
            var missing = new List<string>();
            foreach (var target in Targets)
            {
                if (GameHooks.Find(target.Type, target.Method, target.Args) == null) missing.Add(target.Label);
                if (typeof(NpcLoader).GetMethod(target.Handler, BindingFlags.Static | BindingFlags.NonPublic) == null)
                    missing.Add("补丁方法 " + target.Handler);
            }
            return missing;
        }

        // ================= 小工具 =================

        /// <summary>所有会派发给 GlobalNPC 的钩子（一个 Global 都没覆写时原版 NPC 全跳过）。</summary>
        private static readonly string[] GlobalHooks =
        {
            "PreAI", "AI", "PostAI", "OnSpawn", "ResetEffects", "CheckDead", "CheckActive", "FindFrame",
            "PreKill", "OnKill", "HitEffect", "GetChat", "CanChat", "ApplyDifficultyAndPlayerScaling",
            "PreDraw", "PostDraw", "DrawEffects", "GetAlpha", "BossHeadSlot", "BossHeadRotation",
            "BossHeadSpriteEffects", "SetDefaults",
        };

        private static bool _anyGlobalAny;   // 有 Global 覆写了上面任一钩子
        private static bool _anyGlobalAI;    // 有 Global 覆写了 PreAI/AI/PostAI（AI 的全局一路）

        private static ModNpc Of(NPC npc) => NpcEntities.InstanceOf(npc);

        private static long Address(NPC npc) => NpcEntities.Address(npc);

        private static bool Has(ModNpc mod, string hook) => mod != null && mod.HasHook(hook);

        /// <summary>错误日志的标签（"类名.方法"）；只在 catch 里调用。</summary>
        private static string Label(object owner, string hook) =>
            (owner != null ? owner.GetType().Name : "?") + "." + hook;

        internal static int FramesOf(int type)
        {
            try
            {
                var table = Main.npcFrameCount;
                if (table != null && type >= 0 && type < table.Length && table[type] > 0) return table[type];
            }
            catch { }
            return 1;
        }

        /// <summary>贴图的整宽与单帧高（按类型缓存）。</summary>
        private struct FrameSize
        {
            public int Width;
            public int Height;
        }

        private static readonly Dictionary<int, FrameSize> FrameSizes = new();

        private static FrameSize SizeOf(int type)
        {
            if (FrameSizes.TryGetValue(type, out var size)) return size;
            size = default;
            try
            {
                var assets = TextureAssets.Npc;
                if (assets != null && type >= 0 && type < assets.Length)
                {
                    var asset = assets[type];
                    var texture = asset != null ? asset.Value : null;
                    if (texture == null) return default;   // 贴图还没写进表：不缓存，等下次
                    var frames = FramesOf(type);
                    Pylon.ModLoader.Textures.PylonTexture.Size(texture, out var width, out var height);
                    size.Width = width;
                    size.Height = height / frames;
                }
            }
            catch { return default; }
            FrameSizes[type] = size;
            return size;
        }

        /// <summary>贴图整宽与单帧高（finishModNpc 补宽高用）；贴图不在时返回 0。</summary>
        internal static void TextureSize(int type, out int width, out int height)
        {
            var size = SizeOf(type);
            width = size.Width;
            height = size.Height;
        }

        /// <summary>单帧高。</summary>
        private static int FrameHeight(int type) => SizeOf(type).Height;

        /// <summary>接管原版 FindFrame 前先填好 frame 的 X/Width/Height；作者只写 frame.Y，否则 0×0 源矩形（NPC 看不见）。</summary>
        private static int PrepareFrame(NPC npc)
        {
            var size = SizeOf(npc.type);
            var frame = npc.frame;
            frame.X = 0;
            if (frame.Width <= 0 && size.Width > 0) frame.Width = size.Width;
            if (frame.Height <= 0 && size.Height > 0) frame.Height = size.Height;
            npc.frame = frame;
            return size.Height;
        }

        private static Vector2 Center(NPC npc) =>
            new Vector2(npc.position.X + npc.width * 0.5f, npc.position.Y + npc.height * 0.5f);

        /// <summary>NPC 中心那格的光照色（Lighting.GetColor 取）。</summary>
        private static Color LightAt(NPC npc)
        {
            try
            {
                var c = Center(npc);
                if (float.IsNaN(c.X) || float.IsNaN(c.Y) || float.IsInfinity(c.X) || float.IsInfinity(c.Y)) return Color.White;
                return Lighting.GetColor((int)(c.X / 16f), (int)(c.Y / 16f));
            }
            catch { return Color.White; }
        }

        // ================= 绘制入口（Pylon 专有） =================

        /// <summary>原版上限跳过 type > NPCID.Count-1，IL2CPP 没 IL 可改，于是对每个 active 的 mod NPC 直接调一次 DrawNPCDirect（两趟按 behindTiles 各画各的层）。</summary>
        private static void DrawNpcsPrefix(Main __instance, bool behindTiles)
        {
            try
            {
                if (Main.dedServ || ModContent.Count(ContentKind.Npc) == 0) return;
                var npcs = Main.npc;
                var batch = Main.spriteBatch;
                if (npcs == null || batch == null) return;
                var screen = Main.screenPosition;

                for (var i = 0; i < npcs.Length; i++)
                {
                    var npc = npcs[i];
                    if (npc == null || !npc.active) continue;
                    if (npc.type < VanillaCounts.Npc) continue;      // 原版循环自己会画
                    if (npc.behindTiles != behindTiles) continue;   // 两趟各画各的层

                    try { __instance.DrawNPCDirect(batch, npc, behindTiles, screen); }
                    catch (Exception e) { Safe.Once("npc.draw." + npc.type, $"[npc] DrawNPCDirect 异常：{e.Message}", true); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] DrawNPCs 补画异常：{e.Message}"); }
        }

        // ================= AI =================

        private sealed class AiFrame
        {
            public long Address;
            public ModNpc Mod;
            public bool Globals;      // 全局一路（有 Global 覆写 AI 三件）
            public bool Swapped;      // 本帧换过 type（AIType）
            public int SavedType;
            public bool SkipAi;       // PreAI 否决：游戏 AI 与 AI 都不跑
            public AiFrame Outer;
        }

        private static AiFrame _ai;

        private static bool AiPrefix(NPC __instance)
        {
            var npc = __instance;
            var mod = Of(npc);
            // 原版 NPC：没有 Global 的 AI 钩子就整段不进（marks 过滤 + AI 本地/全局互斥的等价物）。
            if (mod == null && !_anyGlobalAI) return true;

            var frame = new AiFrame
            {
                Address = Address(npc),
                Mod = mod,
                Globals = _anyGlobalAI,
                SavedType = npc.type,
                Outer = _ai,
            };
            _ai = frame;
            try
            {
                var go = true;
                if (frame.Globals)
                {
                    foreach (var g in GlobalNpcRegistry.For(npc, "PreAI"))
                    {
                        try { if (!g.PreAI(npc)) go = false; }
                        catch (Exception e) { Safe.Report(Label(g, "PreAI"), e); }
                    }
                }
                if (go && Has(mod, "PreAI"))
                {
                    try { go = mod.PreAI(npc); }
                    catch (Exception e) { Safe.Report(Label(mod, "PreAI"), e); }
                }
                if (!go) { frame.SkipAi = true; return false; }

                // AIType：只在游戏 AI 期间把 type 换成它，AI/PostAI 之前还原。
                if (mod != null && mod.AIType > 0)
                {
                    frame.Swapped = true;
                    npc.type = mod.AIType;
                }
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] PreAI 异常：{e.Message}");
                return true;
            }
        }

        private static void AiPostfix(NPC __instance)
        {
            var frame = _ai;
            if (frame == null || frame.Address != Address(__instance)) return;
            var npc = __instance;
            try
            {
                if (frame.Swapped) npc.type = frame.SavedType;

                if (!frame.SkipAi)
                {
                    if (Has(frame.Mod, "AI"))
                    {
                        try { frame.Mod.AI(npc); }
                        catch (Exception e) { Safe.Report(Label(frame.Mod, "AI"), e); }
                    }
                    if (frame.Globals)
                    {
                        foreach (var g in GlobalNpcRegistry.For(npc, "AI"))
                        {
                            try { g.AI(npc); }
                            catch (Exception e) { Safe.Report(Label(g, "AI"), e); }
                        }
                    }
                }

                if (Has(frame.Mod, "PostAI"))
                {
                    try { frame.Mod.PostAI(npc); }
                    catch (Exception e) { Safe.Report(Label(frame.Mod, "PostAI"), e); }
                }
                if (frame.Globals)
                {
                    foreach (var g in GlobalNpcRegistry.For(npc, "PostAI"))
                    {
                        try { g.PostAI(npc); }
                        catch (Exception e) { Safe.Report(Label(g, "PostAI"), e); }
                    }
                }
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] AI 异常：{e.Message}");
                if (frame.Swapped) npc.type = frame.SavedType;
            }
            finally { _ai = frame.Outer; }
        }

        // ================= OnSpawn =================

        private static void NewNpcPostfix(IEntitySource source, ref int __result)
        {
            try
            {
                var index = __result;
                var npcs = Main.npc;
                // 最后一格是游戏的"空槽"（index < 200 同义）
                if (npcs == null || index < 0 || index >= npcs.Length - 1) return;
                var npc = npcs[index];
                if (npc == null || !npc.active) return;

                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                if (Has(mod, "OnSpawn"))
                {
                    try { mod.OnSpawn(npc, source); }
                    catch (Exception e) { Safe.Report(Label(mod, "OnSpawn"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "OnSpawn"))
                {
                    try { g.OnSpawn(npc, source); }
                    catch (Exception e) { Safe.Report(Label(g, "OnSpawn"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] OnSpawn 异常：{e.Message}"); }
        }

        // ================= ResetEffects / DrawBehind 跟踪 =================

        private static void ResetEffectsPrefix(NPC __instance, bool lowerBuffTime)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                // 覆写了 DrawBehind 的 mod NPC 记一笔，CacheNPCDraws 时再画。
                if (Has(mod, "DrawBehind")) Behind.Add(npc.whoAmI);

                if (Has(mod, "ResetEffects"))
                {
                    try { mod.ResetEffects(npc); }
                    catch (Exception e) { Safe.Report(Label(mod, "ResetEffects"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "ResetEffects"))
                {
                    try { g.ResetEffects(npc); }
                    catch (Exception e) { Safe.Report(Label(g, "ResetEffects"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] ResetEffects 异常：{e.Message}"); }
        }

        // ================= 死亡 =================

        private static bool CheckDeadPrefix(NPC __instance)
        {
            var npc = __instance;
            try
            {
                if (!npc.active || npc.life > 0) return true;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return true;

                // mod 先，Globals 用 AllCall（每个都跑到，任一 false 就否决）。
                if (Has(mod, "CheckDead"))
                {
                    try { if (!mod.CheckDead(npc)) return false; }
                    catch (Exception e) { Safe.Report(Label(mod, "CheckDead"), e); }
                }
                var go = true;
                foreach (var g in GlobalNpcRegistry.For(npc, "CheckDead"))
                {
                    try { if (!g.CheckDead(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(g, "CheckDead"), e); }
                }
                return go;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] CheckDead 异常：{e.Message}");
                return true;
            }
        }

        private sealed class LootFrame
        {
            public long Address;
            public ModNpc Mod;
            public bool ModOnKill;
            public bool Globals;
            public LootFrame Outer;
        }

        private static LootFrame _loot;

        private static bool LootPrefix(NPC __instance)
        {
            var npc = __instance;
            try
            {
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return true;

                var modPreKill = Has(mod, "PreKill");
                var modOnKill = Has(mod, "OnKill");
                var preKill = GlobalNpcRegistry.For(npc, "PreKill");
                var onKill = GlobalNpcRegistry.For(npc, "OnKill");
                if (!modPreKill && !modOnKill && preKill.Count == 0 && onKill.Count == 0) return true;

                // 联机客户端：掉落只由服务端算。
                if (Main.netMode == 1) return true;

                var go = true;
                foreach (var g in preKill)
                {
                    try { if (!g.PreKill(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(g, "PreKill"), e); }
                }
                if (!go) return false;
                if (modPreKill)
                {
                    try { if (!mod.PreKill(npc)) return false; }
                    catch (Exception e) { Safe.Report(Label(mod, "PreKill"), e); }
                }

                _loot = new LootFrame
                {
                    Address = Address(npc),
                    Mod = mod,
                    ModOnKill = modOnKill,
                    Globals = onKill.Count > 0,
                    Outer = _loot,
                };
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] PreKill 异常：{e.Message}");
                return true;
            }
        }

        private static void LootPostfix(NPC __instance)
        {
            var frame = _loot;
            // prefix 否决/异常时没压过帧：地址对不上就直接走（别把外层的帧弹掉）
            if (frame == null || frame.Address != Address(__instance)) return;
            var npc = __instance;
            try
            {
                if (frame.ModOnKill)
                {
                    try { frame.Mod.OnKill(npc); }
                    catch (Exception e) { Safe.Report(Label(frame.Mod, "OnKill"), e); }
                }
                if (frame.Globals)
                {
                    foreach (var g in GlobalNpcRegistry.For(npc, "OnKill"))
                    {
                        try { g.OnKill(npc); }
                        catch (Exception e) { Safe.Report(Label(g, "OnKill"), e); }
                    }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] OnKill 异常：{e.Message}"); }
            finally { _loot = frame.Outer; }
        }

        // ================= HitEffect / 难度缩放 =================

        private static void HitEffectPostfix(NPC __instance, int hitDirection, double dmg)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                if (Has(mod, "HitEffect"))
                {
                    try { mod.HitEffect(npc, hitDirection, dmg); }
                    catch (Exception e) { Safe.Report(Label(mod, "HitEffect"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "HitEffect"))
                {
                    try { g.HitEffect(npc, hitDirection, dmg); }
                    catch (Exception e) { Safe.Report(Label(g, "HitEffect"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] HitEffect 异常：{e.Message}"); }
        }

        private static void ScalingPostfix(NPC __instance, int numPlayers)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                var modScaling = Has(mod, "ApplyDifficultyAndPlayerScaling");
                var globals = GlobalNpcRegistry.For(npc, "ApplyDifficultyAndPlayerScaling");
                if (!modScaling && globals.Count == 0) return;

                NPC.GetStatScalingFactors(numPlayers, out var balance, out _);
                // 这版游戏的 NPC.difficulty 是 float（0 经典 / 1 专家 / 2 大师），不是枚举。
                var bossAdjustment = npc.difficulty >= MasterDifficulty ? 0.85f : 1f;

                if (modScaling)
                {
                    try { mod.ApplyDifficultyAndPlayerScaling(npc, numPlayers, balance, bossAdjustment); }
                    catch (Exception e) { Safe.Report(Label(mod, "ApplyDifficultyAndPlayerScaling"), e); }
                }
                foreach (var g in globals)
                {
                    try { g.ApplyDifficultyAndPlayerScaling(npc, numPlayers, balance, bossAdjustment); }
                    catch (Exception e) { Safe.Report(Label(g, "ApplyDifficultyAndPlayerScaling"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] ApplyDifficultyAndPlayerScaling 异常：{e.Message}"); }
        }

        // ================= CheckActive / FindFrame =================

        private static bool CheckActivePrefix(NPC __instance)
        {
            var npc = __instance;
            try
            {
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return true;

                if (Has(mod, "CheckActive"))
                {
                    try { if (!mod.CheckActive(npc)) return false; }
                    catch (Exception e) { Safe.Report(Label(mod, "CheckActive"), e); }
                }
                var go = true;
                foreach (var g in GlobalNpcRegistry.For(npc, "CheckActive"))
                {
                    try { if (!g.CheckActive(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(g, "CheckActive"), e); }
                }
                return go;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] CheckActive 异常：{e.Message}");
                return true;
            }
        }

        // AnimationType：临时把 type/贴图/帧数换到借来的原版槽位上。
        private static readonly Dictionary<int, int> BorrowedAnimation = new();   // type → 借用的原版类型（0 = 不借）
        private static int _swapOwn;
        private static int _swapBorrowed;
        private static bool _swapped;
        private static NPC _swapNpc;
        private static Asset<Texture2D> _swapTexture;
        private static int _swapFrames;

        private static int BorrowedOf(int type, ModNpc mod)
        {
            if (BorrowedAnimation.TryGetValue(type, out var cached)) return cached;
            var animation = mod.AnimationType;
            if (animation < 0 || animation >= VanillaCounts.Npc)
            {
                if (animation != 0)
                {
                    Safe.Once("npc.animation." + mod.Name,
                        $"[npc] {mod.Name}: AnimationType {animation} 不是原版 NPC，忽略（自定动画写 FindFrame）", true);
                }
                animation = 0;
            }
            BorrowedAnimation[type] = animation;
            return animation;
        }

        /// <summary>把原版 NPC 的槽位临时指向 mod 的贴图与帧数，并把 type 换过去。</summary>
        private static void SwapAnimation(NPC npc, ModNpc mod)
        {
            // 上一次没跑到 postfix（原版 FindFrame 抛错）：先把原版槽位还回去，别一直占着。
            RestoreAnimation();
            if (_swapped || mod == null) return;
            var animation = BorrowedOf(npc.type, mod);
            if (animation <= 0) return;

            var own = npc.type;
            var assets = TextureAssets.Npc;
            var frames = Main.npcFrameCount;
            if (assets == null || frames == null) return;
            if (own < 0 || own >= assets.Length || animation >= assets.Length) return;
            if (own >= frames.Length || animation >= frames.Length) return;

            // 先存后改：万一某个写入抛错，postfix 的 RestoreAnimation 还能把两个槽位写回去。
            _swapTexture = assets[animation];
            _swapFrames = frames[animation];
            _swapOwn = own;
            _swapBorrowed = animation;
            _swapNpc = npc;
            _swapped = true;
            assets[animation] = assets[own];
            frames[animation] = frames[own];
            npc.type = animation;
        }

        /// <summary>还原 AnimationType 的换位（谁换的谁还原，type 也还原）。</summary>
        private static void RestoreAnimation()
        {
            if (!_swapped) return;
            _swapped = false;
            try
            {
                var assets = TextureAssets.Npc;
                var frames = Main.npcFrameCount;
                if (assets != null && _swapBorrowed >= 0 && _swapBorrowed < assets.Length) assets[_swapBorrowed] = _swapTexture;
                if (frames != null && _swapBorrowed >= 0 && _swapBorrowed < frames.Length) frames[_swapBorrowed] = _swapFrames;
                var npc = _swapNpc;
                if (npc != null && npc.type == _swapBorrowed) npc.type = _swapOwn;
            }
            catch (Exception e) { _logger?.Warn($"[npc] AnimationType 还原异常：{e.Message}"); }
            finally { _swapNpc = null; }
        }

        private static long _frameTaken;   // 已经把 FindFrame 整个接管的 NPC 地址（postfix 用来跳过 Globals）

        private static bool FindFramePrefix(NPC __instance)
        {
            var npc = __instance;
            try
            {
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return true;

                // mod 覆写了 FindFrame：原版整段不跑，先把 frame 的 X/Width/Height 填好，作者只写 frame.Y。
                if (Has(mod, "FindFrame"))
                {
                    var height = PrepareFrame(npc);
                    try { mod.FindFrame(npc, height); }
                    catch (Exception e) { Safe.Report(Label(mod, "FindFrame"), e); }
                    foreach (var g in GlobalNpcRegistry.For(npc, "FindFrame"))
                    {
                        try { g.FindFrame(npc, height); }
                        catch (Exception e) { Safe.Report(Label(g, "FindFrame"), e); }
                    }
                    _frameTaken = Address(npc);
                    return false;
                }

                // 没覆写：借 AnimationType 的原版动画分支（type/贴图/帧数换到那个槽位上）。
                if (mod != null) SwapAnimation(npc, mod);
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] FindFrame 异常：{e.Message}");
                return true;
            }
        }

        private static void FindFramePostfix(NPC __instance)
        {
            var npc = __instance;
            var address = Address(npc);
            if (_frameTaken == address) { _frameTaken = 0; return; }

            try
            {
                // 先还原 AnimationType 的换位，再按 mod 自己的贴图算帧高（原版刚跑完，frame 已就位）。
                RestoreAnimation();
                var globals = GlobalNpcRegistry.For(npc, "FindFrame");
                if (globals.Count == 0) return;
                var height = FrameHeight(npc.type);
                foreach (var g in globals)
                {
                    try { g.FindFrame(npc, height); }
                    catch (Exception e) { Safe.Report(Label(g, "FindFrame"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] FindFrame 异常：{e.Message}"); }
        }

        // ================= 对话 =================

        private static void GetChatPostfix(NPC __instance, ref string __result)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                if (Has(mod, "GetChat"))
                {
                    try
                    {
                        var own = mod.GetChat(npc);
                        if (own != null) __result = own;   // own 非空就换掉
                    }
                    catch (Exception e) { Safe.Report(Label(mod, "GetChat"), e); }
                }

                var text = __result;
                foreach (var g in GlobalNpcRegistry.For(npc, "GetChat"))
                {
                    try { g.GetChat(npc, ref text); }
                    catch (Exception e) { Safe.Report(Label(g, "GetChat"), e); }
                }
                __result = text;
            }
            catch (Exception e) { _logger?.Warn($"[npc] GetChat 异常：{e.Message}"); }
        }

        private static bool CanChatPrefix(NPC __instance, ref bool __result)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return true;

                bool? value = null;
                if (Has(mod, "CanChat"))
                {
                    try { value = mod.CanChat(npc); }
                    catch (Exception e) { Safe.Report(Label(mod, "CanChat"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "CanChat"))
                {
                    try
                    {
                        var own = g.CanChat(npc);
                        if (own == false) { __result = false; return false; }
                        if (own == true) value = true;
                    }
                    catch (Exception e) { Safe.Report(Label(g, "CanChat"), e); }
                }
                if (!value.HasValue) return true;
                __result = value.Value;
                return false;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] CanChat 异常：{e.Message}");
                return true;
            }
        }

        // ================= 绘制 =================

        private sealed class DrawFrame
        {
            public long Address;
            public Color Light;
            public bool Tinted;
            public ModNpc Mod;
            public DrawFrame Outer;
        }

        private static DrawFrame _draw;

        /// <summary>DrawEffects 改了画色时，用 GetNPCColorTintedByBuffs 把改过的颜色喂给原版绘制（原版只会再叠一层 buff 色）。</summary>
        private static bool TintedByBuffsPrefix(NPC __instance, Color npcColor, ref Color __result)
        {
            var frame = _draw;
            if (frame == null || !frame.Tinted || frame.Address != Address(__instance)) return true;
            __result = frame.Light;
            return false;
        }

        private static bool DrawPrefix(SpriteBatch mySpriteBatch, NPC rCurrentNPC, bool behindTiles, Vector2 screenPos)
        {
            var npc = rCurrentNPC;
            try
            {
                var mod = Of(npc);
                var pre = GlobalNpcRegistry.For(npc, "PreDraw");
                var post = GlobalNpcRegistry.For(npc, "PostDraw");
                var effects = GlobalNpcRegistry.For(npc, "DrawEffects");
                var modEffects = Has(mod, "DrawEffects");
                var modPre = Has(mod, "PreDraw");
                var modPost = Has(mod, "PostDraw");
                if (!modEffects && !modPre && !modPost && pre.Count == 0 && post.Count == 0 && effects.Count == 0) return true;

                // 光照色 → DrawEffects（mod 再 Globals）→ Globals PreDraw → mod PreDraw
                var light = LightAt(npc);
                var before = light.PackedValue;
                if (modEffects)
                {
                    try { mod.DrawEffects(npc, ref light); }
                    catch (Exception e) { Safe.Report(Label(mod, "DrawEffects"), e); }
                }
                foreach (var g in effects)
                {
                    try { g.DrawEffects(npc, ref light); }
                    catch (Exception e) { Safe.Report(Label(g, "DrawEffects"), e); }
                }

                var go = true;
                foreach (var g in pre)
                {
                    try { if (!g.PreDraw(npc, mySpriteBatch, screenPos, ref light)) go = false; }
                    catch (Exception e) { Safe.Report(Label(g, "PreDraw"), e); }
                }
                if (go && modPre)
                {
                    try { go = mod.PreDraw(npc, mySpriteBatch, screenPos, light); }
                    catch (Exception e) { Safe.Report(Label(mod, "PreDraw"), e); }
                }

                _draw = new DrawFrame
                {
                    Address = Address(npc),
                    Light = light,
                    Tinted = light.PackedValue != before,
                    Mod = mod,
                    Outer = _draw,
                };
                return go;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] PreDraw 异常：{e.Message}");
                return true;
            }
        }

        private static void DrawPostfix(SpriteBatch mySpriteBatch, NPC rCurrentNPC, bool behindTiles, Vector2 screenPos)
        {
            var frame = _draw;
            if (frame == null || frame.Address != Address(rCurrentNPC)) return;
            var npc = rCurrentNPC;
            try
            {
                // 原版没画（PreDraw 否决）也照样跑 PostDraw（CallAll）。
                if (Has(frame.Mod, "PostDraw"))
                {
                    try { frame.Mod.PostDraw(npc, mySpriteBatch, screenPos, frame.Light); }
                    catch (Exception e) { Safe.Report(Label(frame.Mod, "PostDraw"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "PostDraw"))
                {
                    try { g.PostDraw(npc, mySpriteBatch, screenPos, frame.Light); }
                    catch (Exception e) { Safe.Report(Label(g, "PostDraw"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] PostDraw 异常：{e.Message}"); }
            finally { _draw = frame.Outer; }
        }

        private static bool GetAlphaPrefix(NPC __instance, Color newColor, ref Color __result)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return true;

                var globals = GlobalNpcRegistry.For(npc, "GetAlpha");
                var modAlpha = Has(mod, "GetAlpha");
                if (!modAlpha && globals.Count == 0) return true;

                // Globals 先，谁先给出非 null 就用谁；然后看 mod。
                foreach (var g in globals)
                {
                    try
                    {
                        var value = g.GetAlpha(npc, newColor);
                        if (value.HasValue) { __result = value.Value; return false; }
                    }
                    catch (Exception e) { Safe.Report(Label(g, "GetAlpha"), e); }
                }
                if (modAlpha)
                {
                    try
                    {
                        var value = mod.GetAlpha(npc, newColor);
                        if (value.HasValue) { __result = value.Value; return false; }
                    }
                    catch (Exception e) { Safe.Report(Label(mod, "GetAlpha"), e); }
                }
                return true;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] GetAlpha 异常：{e.Message}");
                return true;
            }
        }

        // ================= 首领头像三件 =================

        private static void BossHeadSlotPostfix(NPC __instance, ref int __result)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                if (Has(mod, "BossHeadSlot"))
                {
                    try { mod.BossHeadSlot(npc, ref __result); }
                    catch (Exception e) { Safe.Report(Label(mod, "BossHeadSlot"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "BossHeadSlot"))
                {
                    try { g.BossHeadSlot(npc, ref __result); }
                    catch (Exception e) { Safe.Report(Label(g, "BossHeadSlot"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] BossHeadSlot 异常：{e.Message}"); }
        }

        private static void BossHeadRotationPostfix(NPC __instance, ref float __result)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                if (Has(mod, "BossHeadRotation"))
                {
                    try { mod.BossHeadRotation(npc, ref __result); }
                    catch (Exception e) { Safe.Report(Label(mod, "BossHeadRotation"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "BossHeadRotation"))
                {
                    try { g.BossHeadRotation(npc, ref __result); }
                    catch (Exception e) { Safe.Report(Label(g, "BossHeadRotation"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] BossHeadRotation 异常：{e.Message}"); }
        }

        private static void BossHeadEffectsPostfix(NPC __instance, ref SpriteEffects __result)
        {
            try
            {
                var npc = __instance;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return;

                if (Has(mod, "BossHeadSpriteEffects"))
                {
                    try { mod.BossHeadSpriteEffects(npc, ref __result); }
                    catch (Exception e) { Safe.Report(Label(mod, "BossHeadSpriteEffects"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "BossHeadSpriteEffects"))
                {
                    try { g.BossHeadSpriteEffects(npc, ref __result); }
                    catch (Exception e) { Safe.Report(Label(g, "BossHeadSpriteEffects"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] BossHeadSpriteEffects 异常：{e.Message}"); }
        }

        // ================= DrawBehind =================

        private static readonly HashSet<int> Behind = new();
        private static readonly List<int> BehindScratch = new();

        private static void CacheDrawsPostfix()
        {
            if (Behind.Count == 0) return;
            BehindScratch.Clear();
            BehindScratch.AddRange(Behind);
            foreach (var index in BehindScratch)
            {
                var npcs = Main.npc;
                var npc = npcs != null && index >= 0 && index < npcs.Length ? npcs[index] : null;
                var mod = npc != null && npc.active && npc.whoAmI == index ? Of(npc) : null;
                if (!Has(mod, "DrawBehind")) { Behind.Remove(index); continue; }
                try { mod.DrawBehind(npc, index); }
                catch (Exception e)
                {
                    Behind.Remove(index);
                    Safe.Report(Label(mod, "DrawBehind"), e);
                }
            }
        }

        // ================= CloneDefaults 的循环克隆检测 =================

        private static readonly HashSet<int> Cloning = new();

        internal static bool StartClone(int type) => Cloning.Add(type);

        internal static void EndClone(int type) => Cloning.Remove(type);
    }
}
