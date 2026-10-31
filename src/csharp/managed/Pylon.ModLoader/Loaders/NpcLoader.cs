// 层：Mod 框架 / Loaders —— NpcLoader（ModNpc/GlobalNpc 钩子接线：AI/事件/绘制/对话，一个钩子一个补丁）。
//
//   · AI 链：Global/Mod 的 PreAI→AI→PostAI（AIType 期间临时换 type）；每个钩子内部先看类型/Global 表早退。
//   · 不 patch SpriteBatch（真机踩过：Unity 绘制入口被重写会串 SpriteEffects，出现"倒影"重绘）。
//   · 一个 Global 抛错只丢它自己那一次调用；补丁方法体只调静态方法（IL2CPP 约束）。
using System;
using System.Collections.Generic;
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
    internal sealed class NpcLoader : ILoaderModule, ILoaderModuleRefresh
    {
        public NpcLoader(ModLogger logger) => _logger = logger;

        public string Name => "npc";

        public int Order => 300;

        private static ModLogger _logger;

        /// <summary>大师难度（这版是 float，Master = 2）。</summary>
        private const float MasterDifficulty = 2f;

        // ================= 装配 =================

        /// <summary>装 hook；Global 门控由 <see cref="Refresh"/> 在每次 Mod 注册后刷新。</summary>
        public bool Install()
        {
                On.Terraria.Main.DrawNPCs += DrawNpcsPrefix;                          // 绘制入口（Pylon 专有补画）
                On.Terraria.NPC.AI += Ai;                                             // AI：PreAI 否决 + 游戏 AI + AI/PostAI 分派
                On.Terraria.NPC.NewNPC += NewNpcPostfix;                              // OnSpawn
                On.Terraria.NPC.UpdateNPC_BuffSetFlags += ResetEffectsPrefix;         // ResetEffects（同时记 DrawBehind）
                On.Terraria.NPC.checkDead += CheckDead;
                On.Terraria.NPC.CheckActive += CheckActive;
                On.Terraria.NPC.FindFrame += FindFrame;                               // FindFrame（mod 覆写接管 / AnimationType 借用）
                On.Terraria.NPC.NPCLoot += Loot;                                      // 死亡：PreKill 否决 + 原版掉落 + OnKill
                On.Terraria.NPC.HitEffect += HitEffectPostfix;
                On.Terraria.NPC.ScaleStats_ByPlayerCount += ScalingPostfix;           // 难度/人数缩放（private 也进了 hook）
                On.Terraria.NPC.GetChat += GetChatPostfix;
                On.Terraria.Main.DrawNPCDirect += Draw;                               // 两套 DrawNPCDirect 重载各挂一个
                On.Terraria.Main.DrawNPCDirect_1 += DrawLight;
                On.Terraria.NPC.GetNPCColorTintedByBuffs += TintedByBuffsPrefix;      // DrawEffects 改色喂回原版绘制
                On.Terraria.NPC.GetAlpha += GetAlphaPrefix;
                On.Terraria.NPC.GetBossHeadTextureIndex += BossHeadSlotPostfix;
                On.Terraria.NPC.GetBossHeadRotation += BossHeadRotationPostfix;
                On.Terraria.NPC.GetBossHeadSpriteEffects += BossHeadEffectsPostfix;
                On.Terraria.Main.CacheNPCDraws += CacheDrawsPostfix;                  // DrawBehind

                // NPC.get_CanTalk / get_CanBeTalkedTo：代理里没有可挂的方法（属性 getter 不在 hook 范围），CanChat 钩子暂不可用。
                _logger?.Warn("[npc] NPC.get_CanTalk/get_CanBeTalkedTo 没有可挂的方法，CanChat 钩子暂不可用");

                _logger?.Info("[npc] NpcLoader 已挂（AI/事件/绘制/对话）");
            return true;
        }

        public void Refresh()
        {
            // 过滤器：没有 Global 覆写这套钩子时，原版 NPC 一个钩子都不用进。
            _anyGlobalAny = GlobalNpcRegistry.AnyWith(GlobalHooks);
            _anyGlobalAI = GlobalNpcRegistry.AnyWith("PreAI", "AI", "PostAI");
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
        private static void DrawNpcsPrefix(On.Terraria.Main.orig_DrawNPCs orig, Main self, bool behindTiles)
        {
            try
            {
                if (Main.dedServ || ModContent.Count(ContentKind.Npc) == 0) { orig(self, behindTiles); return; }
                var npcs = Main.npc;
                var batch = Main.spriteBatch;
                if (npcs == null || batch == null) { orig(self, behindTiles); return; }
                var screen = Main.screenPosition;

                for (var i = 0; i < npcs.Length; i++)
                {
                    var npc = npcs[i];
                    if (npc == null || !npc.active) continue;
                    if (npc.type < VanillaCounts.Npc) continue;      // 原版循环自己会画
                    if (npc.behindTiles != behindTiles) continue;   // 两趟各画各的层

                    try { self.DrawNPCDirect(batch, npc, behindTiles, screen); }
                    catch (Exception e) { Safe.Once("npc.draw." + npc.type, $"[npc] DrawNPCDirect 异常：{e.Message}", true); }
                }
                orig(self, behindTiles);
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] DrawNPCs 补画异常：{e.Message}");
                orig(self, behindTiles);
            }
        }

        // ================= AI =================

        /// <summary>一次 AI 调用整体（旧 prefix+postfix 合并）：全局 PreAI → mod PreAI（可否决）→ 游戏 AI
        /// （AIType 期间临时换 type）→ AI/PostAI 分派；被否决时游戏 AI 与 AI 都不跑，PostAI 照跑。</summary>
        private static void Ai(On.Terraria.NPC.orig_AI orig, NPC self)
        {
            var npc = self;
            var mod = Of(npc);
            // 原版 NPC：没有 Global 的 AI 钩子就整段不进（marks 过滤 + AI 本地/全局互斥的等价物）。
            if (mod == null && !_anyGlobalAI) { orig(self); return; }

            var go = true;
            try
            {
                // —— 前缀（PreAI） ——
                try
                {
                    if (_anyGlobalAI)
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
                }
                catch (Exception e)
                {
                    // 旧 AiPrefix 的 catch：警告后照样跑游戏 AI 与 postfix。
                    _logger?.Warn($"[npc] PreAI 异常：{e.Message}");
                    go = true;
                }

                if (go)
                {
                    // AIType：只在游戏 AI 期间把 type 换成它，AI/PostAI 之前还原。
                    var savedType = npc.type;
                    var swapped = false;
                    if (mod != null && mod.AIType > 0)
                    {
                        swapped = true;
                        npc.type = mod.AIType;
                    }
                    try { orig(self); }
                    finally { if (swapped) npc.type = savedType; }

                    // —— 后缀（AI） ——
                    if (Has(mod, "AI"))
                    {
                        try { mod.AI(npc); }
                        catch (Exception e) { Safe.Report(Label(mod, "AI"), e); }
                    }
                    if (_anyGlobalAI)
                    {
                        foreach (var g in GlobalNpcRegistry.For(npc, "AI"))
                        {
                            try { g.AI(npc); }
                            catch (Exception e) { Safe.Report(Label(g, "AI"), e); }
                        }
                    }
                }

                // —— 后缀（PostAI；被 PreAI 否决也照跑） ——
                if (Has(mod, "PostAI"))
                {
                    try { mod.PostAI(npc); }
                    catch (Exception e) { Safe.Report(Label(mod, "PostAI"), e); }
                }
                if (_anyGlobalAI)
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
                // 安全网：只有前缀包装等意外到得了这（分派都逐调用 try/catch）。
                _logger?.Warn($"[npc] AI 异常：{e.Message}");
            }
        }

        // ================= OnSpawn =================

        private static int NewNpcPostfix(On.Terraria.NPC.orig_NewNPC orig, IEntitySource source, int X, int Y,
                                         int Type, int Start, float ai0, float ai1, float ai2, float ai3, int Target)
        {
            var index = orig(source, X, Y, Type, Start, ai0, ai1, ai2, ai3, Target);
            try
            {
                var npcs = Main.npc;
                // 最后一格是游戏的"空槽"（index < 200 同义）
                if (npcs == null || index < 0 || index >= npcs.Length - 1) return index;
                var npc = npcs[index];
                if (npc == null || !npc.active) return index;

                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return index;

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
            return index;
        }

        // ================= ResetEffects / DrawBehind 跟踪 =================

        private static void ResetEffectsPrefix(On.Terraria.NPC.orig_UpdateNPC_BuffSetFlags orig, NPC self, bool lowerBuffTime)
        {
            try
            {
                var npc = self;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) { orig(self, lowerBuffTime); return; }

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
            orig(self, lowerBuffTime);
        }

        // ================= 死亡 =================

        private static void CheckDead(On.Terraria.NPC.orig_checkDead orig, NPC self)
        {
            var npc = self;
            var go = true;
            try
            {
                if (!npc.active || npc.life > 0) { orig(self); return; }
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) { orig(self); return; }

                // mod 先，Globals 用 AllCall（每个都跑到，任一 false 就否决）。
                if (Has(mod, "CheckDead"))
                {
                    try { if (!mod.CheckDead(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(mod, "CheckDead"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "CheckDead"))
                {
                    if (!go) break;
                    try { if (!g.CheckDead(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(g, "CheckDead"), e); }
                }
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] CheckDead 异常：{e.Message}");
            }
            if (go) orig(self);
        }

        /// <summary>死亡掉落整体（旧 prefix+postfix 合并）：PreKill（可否决，联机客户端不跑）→ 原版 NPCLoot → OnKill 分派。</summary>
        private static void Loot(On.Terraria.NPC.orig_NPCLoot orig, NPC self)
        {
            var npc = self;
            try
            {
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) { orig(self); return; }

                var modPreKill = Has(mod, "PreKill");
                var modOnKill = Has(mod, "OnKill");
                var preKill = GlobalNpcRegistry.For(npc, "PreKill");
                var onKill = GlobalNpcRegistry.For(npc, "OnKill");
                if (!modPreKill && !modOnKill && preKill.Count == 0 && onKill.Count == 0) { orig(self); return; }

                // 联机客户端：掉落只由服务端算。
                if (Main.netMode == 1) { orig(self); return; }

                var go = true;
                foreach (var g in preKill)
                {
                    try { if (!g.PreKill(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(g, "PreKill"), e); }
                }
                if (go && modPreKill)
                {
                    try { go = mod.PreKill(npc); }
                    catch (Exception e) { Safe.Report(Label(mod, "PreKill"), e); }
                }
                if (!go) return;   // PreKill 否决：原版掉落与 OnKill 都不跑（旧 prefix 返回 false）

                orig(self);

                // —— 后缀（OnKill） ——
                if (modOnKill)
                {
                    try { mod.OnKill(npc); }
                    catch (Exception e) { Safe.Report(Label(mod, "OnKill"), e); }
                }
                if (onKill.Count > 0)
                {
                    foreach (var g in onKill)
                    {
                        try { g.OnKill(npc); }
                        catch (Exception e) { Safe.Report(Label(g, "OnKill"), e); }
                    }
                }
            }
            catch (Exception e)
            {
                // 旧 LootPrefix 的 catch：警告后照样跑原版掉落（OnKill 在异常路径也一样不跑）。
                _logger?.Warn($"[npc] PreKill 异常：{e.Message}");
                orig(self);
            }
        }

        // ================= HitEffect / 难度缩放 =================

        private static void HitEffectPostfix(On.Terraria.NPC.orig_HitEffect orig, NPC self, int hitDirection, double dmg)
        {
            orig(self, hitDirection, dmg);
            try
            {
                var npc = self;
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

        private static void ScalingPostfix(On.Terraria.NPC.orig_ScaleStats_ByPlayerCount orig, NPC self, int numPlayers)
        {
            orig(self, numPlayers);
            try
            {
                var npc = self;
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

        private static void CheckActive(On.Terraria.NPC.orig_CheckActive orig, NPC self)
        {
            var npc = self;
            var go = true;
            try
            {
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) { orig(self); return; }

                if (Has(mod, "CheckActive"))
                {
                    try { if (!mod.CheckActive(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(mod, "CheckActive"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "CheckActive"))
                {
                    if (!go) break;
                    try { if (!g.CheckActive(npc)) go = false; }
                    catch (Exception e) { Safe.Report(Label(g, "CheckActive"), e); }
                }
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] CheckActive 异常：{e.Message}");
            }
            if (go) orig(self);
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

        /// <summary>FindFrame 整体（旧 prefix+postfix 合并）：mod 覆写了就接管整段（先填 frame 的 X/Width/Height），
        /// 否则借 AnimationType 的原版动画分支，跑完再按 mod 自己的贴图补跑 Globals。</summary>
        private static void FindFrame(On.Terraria.NPC.orig_FindFrame orig, NPC self)
        {
            var npc = self;
            try
            {
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) { orig(self); return; }

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
                    return;
                }

                // 没覆写：借 AnimationType 的原版动画分支（type/贴图/帧数换到那个槽位上）。
                if (mod != null) SwapAnimation(npc, mod);
                try { orig(self); }
                finally { RestoreAnimation(); }   // 先还原 AnimationType 的换位，再按 mod 自己的贴图算帧高（原版刚跑完，frame 已就位）

                var globals = GlobalNpcRegistry.For(npc, "FindFrame");
                if (globals.Count == 0) return;
                var frameHeight2 = FrameHeight(npc.type);
                foreach (var g in globals)
                {
                    try { g.FindFrame(npc, frameHeight2); }
                    catch (Exception e) { Safe.Report(Label(g, "FindFrame"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] FindFrame 异常：{e.Message}"); }
        }

        // ================= 对话 =================

        private static string GetChatPostfix(On.Terraria.NPC.orig_GetChat orig, NPC self)
        {
            var result = orig(self);
            try
            {
                var npc = self;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return result;

                if (Has(mod, "GetChat"))
                {
                    try
                    {
                        var own = mod.GetChat(npc);
                        if (own != null) result = own;   // own 非空就换掉
                    }
                    catch (Exception e) { Safe.Report(Label(mod, "GetChat"), e); }
                }

                var text = result;
                foreach (var g in GlobalNpcRegistry.For(npc, "GetChat"))
                {
                    try { g.GetChat(npc, ref text); }
                    catch (Exception e) { Safe.Report(Label(g, "GetChat"), e); }
                }
                result = text;
            }
            catch (Exception e) { _logger?.Warn($"[npc] GetChat 异常：{e.Message}"); }
            return result;
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
        private static Color TintedByBuffsPrefix(On.Terraria.NPC.orig_GetNPCColorTintedByBuffs orig, NPC self, Color npcColor)
        {
            var frame = _draw;
            if (frame != null && frame.Tinted && frame.Address == Address(self)) return frame.Light;
            return orig(self, npcColor);
        }

        /// <summary>两套 DrawNPCDirect 重载共用的前缀：光照色 → DrawEffects → PreDraw 否决判定，并压帧
        /// （GetNPCColorTintedByBuffs / PostDraw 靠它）；没有任何绘制钩子时返回 null 帧（handler 直接交回原版）。</summary>
        private static bool DrawPrefixCore(NPC npc, SpriteBatch mySpriteBatch, Vector2 screenPos, out DrawFrame frame)
        {
            frame = null;
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

            frame = new DrawFrame
            {
                Address = Address(npc),
                Light = light,
                Tinted = light.PackedValue != before,
                Mod = mod,
                Outer = _draw,
            };
            _draw = frame;
            return go;
        }

        /// <summary>后缀：原版没画（PreDraw 否决）也照样跑 PostDraw（CallAll）。</summary>
        private static void DrawPostfixCore(NPC npc, SpriteBatch mySpriteBatch, Vector2 screenPos, DrawFrame frame)
        {
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

        /// <summary>DrawNPCDirect（4 参重载）。</summary>
        private static void Draw(On.Terraria.Main.orig_DrawNPCDirect orig, Main self, SpriteBatch mySpriteBatch,
                                 NPC rCurrentNPC, bool behindTiles, Vector2 screenPos)
        {
            var npc = rCurrentNPC;
            DrawFrame frame = null;
            try
            {
                var go = DrawPrefixCore(npc, mySpriteBatch, screenPos, out frame);
                if (frame == null) { orig(self, mySpriteBatch, rCurrentNPC, behindTiles, screenPos); return; }
                if (go) orig(self, mySpriteBatch, rCurrentNPC, behindTiles, screenPos);
                DrawPostfixCore(npc, mySpriteBatch, screenPos, frame);
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] PreDraw 异常：{e.Message}");
            }
            finally { if (frame != null) _draw = frame.Outer; }
        }

        /// <summary>DrawNPCDirect（带 LightMap/lightRegion 的 6 参重载，逻辑同上）。</summary>
        private static void DrawLight(On.Terraria.Main.orig_DrawNPCDirect_1 orig, Main self, SpriteBatch mySpriteBatch,
                                      NPC rCurrentNPC, bool behindTiles, Vector2 screenPos, LightMap lightMap,
                                      ref Rectangle lightRegion)
        {
            var npc = rCurrentNPC;
            DrawFrame frame = null;
            try
            {
                var go = DrawPrefixCore(npc, mySpriteBatch, screenPos, out frame);
                if (frame == null) { orig(self, mySpriteBatch, rCurrentNPC, behindTiles, screenPos, lightMap, ref lightRegion); return; }
                if (go) orig(self, mySpriteBatch, rCurrentNPC, behindTiles, screenPos, lightMap, ref lightRegion);
                DrawPostfixCore(npc, mySpriteBatch, screenPos, frame);
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] PreDraw 异常：{e.Message}");
            }
            finally { if (frame != null) _draw = frame.Outer; }
        }

        private static Color GetAlphaPrefix(On.Terraria.NPC.orig_GetAlpha orig, NPC self, Color newColor)
        {
            try
            {
                var npc = self;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return orig(self, newColor);

                var globals = GlobalNpcRegistry.For(npc, "GetAlpha");
                var modAlpha = Has(mod, "GetAlpha");
                if (!modAlpha && globals.Count == 0) return orig(self, newColor);

                // Globals 先，谁先给出非 null 就用谁；然后看 mod。
                foreach (var g in globals)
                {
                    try
                    {
                        var value = g.GetAlpha(npc, newColor);
                        if (value.HasValue) return value.Value;
                    }
                    catch (Exception e) { Safe.Report(Label(g, "GetAlpha"), e); }
                }
                if (modAlpha)
                {
                    try
                    {
                        var value = mod.GetAlpha(npc, newColor);
                        if (value.HasValue) return value.Value;
                    }
                    catch (Exception e) { Safe.Report(Label(mod, "GetAlpha"), e); }
                }
                return orig(self, newColor);
            }
            catch (Exception e)
            {
                _logger?.Warn($"[npc] GetAlpha 异常：{e.Message}");
                return orig(self, newColor);
            }
        }

        // ================= 首领头像三件 =================

        private static int BossHeadSlotPostfix(On.Terraria.NPC.orig_GetBossHeadTextureIndex orig, NPC self)
        {
            var result = orig(self);
            try
            {
                var npc = self;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return result;

                if (Has(mod, "BossHeadSlot"))
                {
                    try { mod.BossHeadSlot(npc, ref result); }
                    catch (Exception e) { Safe.Report(Label(mod, "BossHeadSlot"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "BossHeadSlot"))
                {
                    try { g.BossHeadSlot(npc, ref result); }
                    catch (Exception e) { Safe.Report(Label(g, "BossHeadSlot"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] BossHeadSlot 异常：{e.Message}"); }
            return result;
        }

        private static float BossHeadRotationPostfix(On.Terraria.NPC.orig_GetBossHeadRotation orig, NPC self)
        {
            var result = orig(self);
            try
            {
                var npc = self;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return result;

                if (Has(mod, "BossHeadRotation"))
                {
                    try { mod.BossHeadRotation(npc, ref result); }
                    catch (Exception e) { Safe.Report(Label(mod, "BossHeadRotation"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "BossHeadRotation"))
                {
                    try { g.BossHeadRotation(npc, ref result); }
                    catch (Exception e) { Safe.Report(Label(g, "BossHeadRotation"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] BossHeadRotation 异常：{e.Message}"); }
            return result;
        }

        private static SpriteEffects BossHeadEffectsPostfix(On.Terraria.NPC.orig_GetBossHeadSpriteEffects orig, NPC self)
        {
            var result = orig(self);
            try
            {
                var npc = self;
                var mod = Of(npc);
                if (mod == null && !_anyGlobalAny) return result;

                if (Has(mod, "BossHeadSpriteEffects"))
                {
                    try { mod.BossHeadSpriteEffects(npc, ref result); }
                    catch (Exception e) { Safe.Report(Label(mod, "BossHeadSpriteEffects"), e); }
                }
                foreach (var g in GlobalNpcRegistry.For(npc, "BossHeadSpriteEffects"))
                {
                    try { g.BossHeadSpriteEffects(npc, ref result); }
                    catch (Exception e) { Safe.Report(Label(g, "BossHeadSpriteEffects"), e); }
                }
            }
            catch (Exception e) { _logger?.Warn($"[npc] BossHeadSpriteEffects 异常：{e.Message}"); }
            return result;
        }

        // ================= DrawBehind =================

        private static readonly HashSet<int> Behind = new();
        private static readonly List<int> BehindScratch = new();

        private static void CacheDrawsPostfix(On.Terraria.Main.orig_CacheNPCDraws orig, Main self)
        {
            orig(self);
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