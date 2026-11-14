using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Graphics.Light;

namespace Pylon.ModLoader;

/// <summary>
/// NPC 管线：AI / 事件 / 绘制 / 首领头像。分派方向与弹幕相反——Global 先、本 NPC 后（AI 与 PostAI 除外）。
/// 每个游戏钩子入口只解析一次"实例 + Global 表"，跨钩子上下文用管线实例上的定长结构体栈。
/// </summary>
internal sealed class NpcPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static NpcPipeline Instance { get; } = new();

    private const int FrameDepth = 2;
    private const float MasterDifficulty = 2f;

    private readonly NpcBinding _binding = new();
    private readonly NpcDefaults _defaults;
    private readonly NpcTown _town;
    private readonly NpcSavePipeline _save = new();
    private readonly NpcDrawFrame[] _draws = new NpcDrawFrame[FrameDepth];
    private readonly HashSet<int> _reported = [];
    private readonly HashSet<int> _cloning = [];
    private readonly Dictionary<int, int> _borrowedAnimation = [];
    private readonly HashSet<int> _behind = [];
    private readonly List<int> _behindScratch = [];

    private int _drawDepth;

    private bool _swapped;
    private NPC? _swapNpc;
    private int _swapOwn;
    private int _swapBorrowed;
    private int _swapFrames;
    private Asset<Texture2D> _swapTexture = null!;

    private NpcPipeline()
    {
        _defaults = new NpcDefaults(_binding);
        _town = new NpcTown();
    }

    public string Name => "npc";

    public IReadOnlyList<TableRequest> Tables =>
        [new(NpcTables.Set, () => ModContent.NextType(ContentKind.Npc), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    private ref NpcDrawFrame Draw => ref _draws[_drawDepth - 1];

    public bool Install()
    {
        _binding.Prime();
        _defaults.Install();
        _town.Install();
        _save.Install();
        NpcHeadHooks.Install();
        NpcSpawn.Install();
        NpcHitHooks.Install();
        NpcContactHooks.Install();

        On.Terraria.Main.DrawNPCs += DrawNpcs;
        On.Terraria.NPC.AI += Ai;
        On.Terraria.NPC.NewNPC += Spawned;
        On.Terraria.NPC.UpdateNPC_BuffSetFlags += ResetEffects;
        On.Terraria.NPC.checkDead += CheckDead;
        On.Terraria.NPC.CheckActive += CheckActive;
        On.Terraria.NPC.FindFrame += FindFrame;
        On.Terraria.NPC.NPCLoot += Loot;
        On.Terraria.NPC.HitEffect += HitEffect;
        On.Terraria.NPC.ScaleStats_ByPlayerCount += Scaling;
        On.Terraria.NPC.UpdateNPC_BuffApplyDOTs += LifeRegen;
        On.Terraria.NPC.GetChat += GetChat;
        On.Terraria.NPC.get_CanTalk += ForceCanTalk;
        On.Terraria.NPC.GetNPCColorTintedByBuffs += TintedByBuffs;
        On.Terraria.NPC.GetAlpha += Alpha;
        On.Terraria.NPC.GetBossHeadTextureIndex += BossHeadSlot;
        On.Terraria.NPC.GetBossHeadRotation += BossHeadRotation;
        On.Terraria.NPC.GetBossHeadSpriteEffects += BossHeadEffects;
        On.Terraria.Main.DrawNPCDirect_SpriteBatch_NPC_bool_Vector2 += DrawDirect;
        On.Terraria.Main.DrawNPCDirect_SpriteBatch_NPC_bool_Vector2_LightMap_RectangleRef += DrawDirectLit;
        On.Terraria.Main.CacheNPCDraws += CacheDraws;

        Log.Info("[npc] 已挂（AI / 事件 / 绘制 / 首领头像 / 城镇）");
        return true;
    }

    public void Refresh() => _binding.Prime();

#region 外部入口（作者 API 需要）

    internal ModNpc? InstanceOf(NPC? npc) => _binding.InstanceOf(npc);

    internal bool StartClone(int type) => _cloning.Add(type);

    internal void EndClone(NPC npc, ModNpc instance, int clonedType)
    {
        _binding.RebindTo(npc, instance);
        GlobalNpcRegistry.Attach(npc, true);
        _cloning.Remove(clonedType);
    }

    internal void SetupShops(IModLog log)
    {
        PatchNameLimit(log);
        _town.SetupShops(log);
    }

    private static void PatchNameLimit(IModLog log)
    {
        try
        {
            var total = VanillaCounts.Npc + ModContent.GetContent<ModNpc>().Count;
            var old = (uint)(VanillaCounts.Npc - 2);
            var updated = (uint)(total - 2);
            var changed = Pylon.NaviteAPI.Host.PatchNative.CompareLimit("Terraria.Lang", "GetNPCName", 1, old, updated, 0, 0);
            if (changed > 0) log.Info($"[npc] Lang.GetNPCName 上限补丁：{old}→{updated}（{changed} 处）");
            else log.Warn($"[npc] Lang.GetNPCName 上限补丁没生效（{old}→{updated}）");
        }
        catch (Exception e)
        {
            log.Warn("[npc] Lang.GetNPCName 上限补丁异常：" + e.Message);
        }
    }

#endregion

#region AI

    private void Ai(On.Terraria.NPC.orig_AI orig, NPC self)
    {
        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.AnyWith(HookId.PreAI, HookId.AI, HookId.PostAI);
        if (mod is null && !globals)
        {
            orig(self);
            return;
        }

        var go = true;

        if (globals)
        {
            foreach (var global in GlobalNpcRegistry.For(self, HookId.PreAI))
            {
                try
                {
                    if (!global.PreAI(self)) go = false;
                }
                catch (Exception e)
                {
                    Fail(global, HookId.PreAI, e);
                }
            }
        }

        if (go && mod is not null && mod.HookEnabled(HookId.PreAI))
        {
            try
            {
                go = mod.PreAI(self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PreAI, e);
                go = true;
            }
        }

        if (go)
        {
            var savedType = self.type;
            var swapped = false;
            try
            {
                if (mod is not null && mod.AIType > 0)
                {
                    swapped = true;
                    self.type = mod.AIType;
                }

                orig(self);
            }
            finally
            {
                if (swapped) self.type = savedType;
            }

            if (mod is not null && mod.HookEnabled(HookId.AI))
            {
                try
                {
                    mod.AI(self);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.AI, e);
                }
            }

            if (globals)
            {
                foreach (var global in GlobalNpcRegistry.For(self, HookId.AI))
                {
                    try
                    {
                        global.AI(self);
                    }
                    catch (Exception e)
                    {
                        Fail(global, HookId.AI, e);
                    }
                }
            }
        }

        if (mod is not null && mod.HookEnabled(HookId.PostAI))
        {
            try
            {
                mod.PostAI(self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PostAI, e);
            }
        }

        if (!globals) return;
        foreach (var global in GlobalNpcRegistry.For(self, HookId.PostAI))
        {
            try
            {
                global.PostAI(self);
            }
            catch (Exception e)
            {
                Fail(global, HookId.PostAI, e);
            }
        }
    }

    private int Spawned(On.Terraria.NPC.orig_NewNPC orig, IEntitySource source, int x, int y, int type, int start,
                        float ai0, float ai1, float ai2, float ai3, int target)
    {
        var index = orig(source, x, y, type, start, ai0, ai1, ai2, ai3, target);

        try
        {
            var npcs = Main.npc;
            if (npcs is null || index < 0 || index >= npcs.Length - 1) return index;

            var npc = npcs[index];
            if (npc is null || !npc.active) return index;

            var mod = _binding.InstanceOf(npc);
            if (mod is not null && mod.HookEnabled(HookId.OnSpawn))
            {
                try
                {
                    mod.OnSpawn(npc, source);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.OnSpawn, e);
                }
            }

            foreach (var global in GlobalNpcRegistry.For(npc, HookId.OnSpawn))
            {
                try
                {
                    global.OnSpawn(npc, source);
                }
                catch (Exception e)
                {
                    Fail(global, HookId.OnSpawn, e);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[npc] OnSpawn 异常：{e.Message}");
        }

        return index;
    }

#endregion

#region ResetEffects / DrawBehind

    private void ResetEffects(On.Terraria.NPC.orig_UpdateNPC_BuffSetFlags orig, NPC self, bool lowerBuffTime)
    {
        try
        {
            var mod = _binding.InstanceOf(self);

            if (mod is not null && mod.HookEnabled(HookId.ResetEffects))
            {
                try
                {
                    mod.ResetEffects(self);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ResetEffects, e);
                }
            }

            foreach (var global in GlobalNpcRegistry.For(self, HookId.ResetEffects))
            {
                try
                {
                    global.ResetEffects(self);
                }
                catch (Exception e)
                {
                    Fail(global, HookId.ResetEffects, e);
                }
            }

            if (mod is not null && mod.HookEnabled(HookId.DrawBehind)) _behind.Add(self.whoAmI);
        }
        catch (Exception e)
        {
            Log.Warn($"[npc] ResetEffects 异常：{e.Message}");
        }

        orig(self, lowerBuffTime);
    }

    private void CacheDraws(On.Terraria.Main.orig_CacheNPCDraws orig, Main self)
    {
        orig(self);
        if (_behind.Count == 0) return;

        _behindScratch.Clear();
        _behindScratch.AddRange(_behind);

        foreach (var index in _behindScratch)
        {
            var npcs = Main.npc;
            var npc = npcs is not null && index >= 0 && index < npcs.Length ? npcs[index] : null;
            if (npc is null || !npc.active || npc.whoAmI != index)
            {
                _behind.Remove(index);
                continue;
            }

            var mod = _binding.InstanceOf(npc);
            if (mod is null || !mod.HookEnabled(HookId.DrawBehind))
            {
                _behind.Remove(index);
                continue;
            }

            try
            {
                mod.DrawBehind(npc, index);
            }
            catch (Exception e)
            {
                _behind.Remove(index);
                Fail(mod, HookId.DrawBehind, e);
            }
        }
    }

#endregion

#region 死亡与伤害反馈

    private void CheckDead(On.Terraria.NPC.orig_checkDead orig, NPC self)
    {
        if (!self.active || self.life > 0 || (self.realLife >= 0 && self.realLife != self.whoAmI))
        {
            orig(self);
            return;
        }

        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.CheckDead);
        if (mod is null && globals.Count == 0)
        {
            orig(self);
            return;
        }

        var alive = true;

        if (mod is not null && mod.HookEnabled(HookId.CheckDead))
        {
            try
            {
                if (!mod.CheckDead(self)) alive = false;
            }
            catch (Exception e)
            {
                Fail(mod, HookId.CheckDead, e);
            }
        }

        foreach (var global in globals)
        {
            try
            {
                if (!global.CheckDead(self)) alive = false;
            }
            catch (Exception e)
            {
                Fail(global, HookId.CheckDead, e);
            }
        }

        if (alive) orig(self);
    }

    private void Loot(On.Terraria.NPC.orig_NPCLoot orig, NPC self)
    {
        var mod = _binding.InstanceOf(self);
        var preKill = GlobalNpcRegistry.For(self, HookId.PreKill);
        var onKill = GlobalNpcRegistry.For(self, HookId.OnKill);
        var modPreKill = mod is not null && mod.HookEnabled(HookId.PreKill);
        var modOnKill = mod is not null && mod.HookEnabled(HookId.OnKill);
        if (!modPreKill && !modOnKill && preKill.Count == 0 && onKill.Count == 0)
        {
            orig(self);
            return;
        }

        if (Main.netMode == 1)
        {
            orig(self);
            return;
        }

        var allowed = true;
        foreach (var global in preKill)
        {
            try
            {
                if (global.PreKill(self)) continue;
                allowed = false;
                break;
            }
            catch (Exception e)
            {
                Fail(global, HookId.PreKill, e);
            }
        }

        if (allowed && modPreKill)
        {
            try
            {
                allowed = mod!.PreKill(self);
            }
            catch (Exception e)
            {
                Fail(mod!, HookId.PreKill, e);
            }
        }

        if (!allowed) return;

        orig(self);

        if (modOnKill)
        {
            try
            {
                mod!.OnKill(self);
            }
            catch (Exception e)
            {
                Fail(mod!, HookId.OnKill, e);
            }
        }

        foreach (var global in onKill)
        {
            try
            {
                global.OnKill(self);
            }
            catch (Exception e)
            {
                Fail(global, HookId.OnKill, e);
            }
        }
    }

    private void HitEffect(On.Terraria.NPC.orig_HitEffect orig, NPC self, int hitDirection, double dmg)
    {
        orig(self, hitDirection, dmg);

        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.HitEffect);
        if (mod is null && globals.Count == 0) return;

        if (mod is not null && mod.HookEnabled(HookId.HitEffect))
        {
            try
            {
                mod.HitEffect(self, hitDirection, (int)dmg);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.HitEffect, e);
            }
        }

        foreach (var global in globals)
        {
            try
            {
                global.HitEffect(self, hitDirection, (int)dmg);
            }
            catch (Exception e)
            {
                Fail(global, HookId.HitEffect, e);
            }
        }
    }

    private void LifeRegen(On.Terraria.NPC.orig_UpdateNPC_BuffApplyDOTs orig, NPC self)
    {
        var mod = _binding.InstanceOf(self);
        if (mod is null || !mod.HookEnabled(HookId.UpdateLifeRegen))
        {
            orig(self);
            return;
        }

        try
        {
            var regen = self.lifeRegen;
            mod.UpdateLifeRegen(self, ref regen);
            self.lifeRegen = regen;
        }
        catch (Exception e)
        {
            Fail(self, HookId.UpdateLifeRegen, e);
        }

        orig(self);
    }

    private void Scaling(On.Terraria.NPC.orig_ScaleStats_ByPlayerCount orig, NPC self, int numPlayers)
    {
        orig(self, numPlayers);

        var mod = _binding.InstanceOf(self);
        var modScales = mod is not null && mod.HookEnabled(HookId.ApplyDifficultyAndPlayerScaling);
        var globals = GlobalNpcRegistry.For(self, HookId.ApplyDifficultyAndPlayerScaling);
        if (!modScales && globals.Count == 0) return;

        try
        {
            NPC.GetStatScalingFactors(numPlayers, out var balance, out _);
            const float bossAdjustment = 1f;

            if (modScales)
            {
                try
                {
                    mod!.ApplyDifficultyAndPlayerScaling(self, numPlayers, balance, bossAdjustment);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ApplyDifficultyAndPlayerScaling, e);
                }
            }

            foreach (var global in globals)
            {
                try
                {
                    global.ApplyDifficultyAndPlayerScaling(self, numPlayers, balance, bossAdjustment);
                }
                catch (Exception e)
                {
                    Fail(global, HookId.ApplyDifficultyAndPlayerScaling, e);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[npc] ApplyDifficultyAndPlayerScaling 异常：{e.Message}");
        }
    }

    private void CheckActive(On.Terraria.NPC.orig_CheckActive orig, NPC self)
    {
        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.CheckActive);
        if (mod is null && globals.Count == 0)
        {
            orig(self);
            return;
        }

        var alive = true;

        if (mod is not null && mod.HookEnabled(HookId.CheckActive))
        {
            try
            {
                if (!mod.CheckActive(self)) alive = false;
            }
            catch (Exception e)
            {
                Fail(mod, HookId.CheckActive, e);
            }
        }

        foreach (var global in globals)
        {
            if (!alive) break;
            try
            {
                if (!global.CheckActive(self)) alive = false;
            }
            catch (Exception e)
            {
                Fail(global, HookId.CheckActive, e);
            }
        }

        if (alive) orig(self);
    }

    internal static void EnsureNpcTypeTables()
    {
        var mods = ModContent.GetContent<ModNpc>();
        if (mods.Count == 0) return;

        try
        {
            var spawn = NPCID.Sets.SpecialSpawningRules;
            if (spawn is not null)
            {
                foreach (var npc in mods)
                {
                    if (spawn.ContainsKey(npc.Type)) continue;
                    spawn[npc.Type] = 0;
                }
            }
        }
        catch (Exception e) { Log.Warn("[npc] 刷怪规则表补充失败：" + e.GetType().Name); }

        try
        {
            var immunity = NPCID.Sets.DebuffImmunitySets;
            if (immunity is not null)
            {
                foreach (var npc in mods)
                {
                    if (immunity.ContainsKey(npc.Type)) continue;
                    immunity[npc.Type] = new NPCDebuffImmunityData();
                }
            }
        }
        catch (Exception e) { Log.Warn("[npc] 免疫表补充失败：" + e.GetType().Name); }
    }

    private bool ForceCanTalk(On.Terraria.NPC.orig_get_CanTalk orig, NPC self)
    {
        var vanilla = orig(self);
        if (vanilla) return true;

        var mod = _binding.InstanceOf(self);
        if (mod is null) return false;

        try
        {
            return mod.CanChat(self) ?? true;
        }
        catch (Exception e)
        {
            Fail(mod, HookId.GetChat, e);
            return true;
        }
    }

    private string GetChat(On.Terraria.NPC.orig_GetChat orig, NPC self)
    {
        var result = orig(self);

        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.GetChat);
        if (mod is null && globals.Count == 0) return result;

        try
        {
            if (mod is not null && mod.HookEnabled(HookId.GetChat) && mod.GetChat(self) is { } chat) result = chat;
            foreach (var global in globals) global.GetChat(self, ref result);
        }
        catch (Exception e)
        {
            Log.Warn($"[npc] GetChat 异常：{e.Message}");
        }

        return result;
    }

#endregion

#region 帧

    private void FindFrame(On.Terraria.NPC.orig_FindFrame orig, NPC self)
    {
        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.FindFrame);

        if (mod is not null && mod.HookEnabled(HookId.FindFrame))
        {
            var frameHeight = _defaults.PrepareFrame(self);
            try
            {
                mod.FindFrame(self, frameHeight);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.FindFrame, e);
                orig(self);
                return;
            }

            foreach (var global in globals)
            {
                try
                {
                    global.FindFrame(self, frameHeight);
                }
                catch (Exception e)
                {
                    Fail(global, HookId.FindFrame, e);
                }
            }

            return;
        }

        if (globals.Count == 0)
        {
            orig(self);
            if (mod is not null) _defaults.EnsureFrame(self);
            return;
        }

        var borrowed = SwapAnimation(self);
        try
        {
            orig(self);
        }
        finally
        {
            RestoreAnimation(borrowed);
        }

        if (mod is not null) _defaults.EnsureFrame(self);

        var height = _defaults.FrameHeight(self.type);
        foreach (var global in globals)
        {
            try
            {
                global.FindFrame(self, height);
            }
            catch (Exception e)
            {
                Fail(global, HookId.FindFrame, e);
            }
        }
    }

    private int BorrowedOf(ModNpc mod)
    {
        var type = mod.Type;
        if (_borrowedAnimation.TryGetValue(type, out var cached)) return cached;

        var animation = mod.AnimationType;
        if (animation < 0 || animation >= VanillaCounts.Npc)
        {
            if (animation != 0)
            {
                Safe.Once("npc.animation." + mod.Name,
                    $"[npc] {mod.Name}: AnimationType {animation} 不是原版 NPC，忽略（自定动画请覆写 FindFrame）", true);
            }

            animation = 0;
        }

        _borrowedAnimation[type] = animation;
        return animation;
    }

    private bool SwapAnimation(NPC npc)
    {
        RestoreAnimation(_swapped);

        var mod = _binding.InstanceOf(npc);
        if (mod is null) return false;

        var animation = BorrowedOf(mod);
        if (animation <= 0) return false;

        var own = npc.type;
        var assets = TextureAssets.Npc;
        var frames = Main.npcFrameCount;
        if (assets is null || frames is null) return false;
        if (own < 0 || own >= assets.Length || animation >= assets.Length) return false;
        if (own >= frames.Length || animation >= frames.Length) return false;

        _swapTexture = assets[animation];
        _swapFrames = frames[animation];
        _swapOwn = own;
        _swapBorrowed = animation;
        _swapNpc = npc;

        assets[animation] = assets[own];
        frames[animation] = frames[own];
        npc.type = animation;
        _swapped = true;
        return true;
    }

    private void RestoreAnimation(bool swapped)
    {
        if (!swapped || _swapNpc is null) return;

        _swapped = false;
        try
        {
            var assets = TextureAssets.Npc;
            var frames = Main.npcFrameCount;
            if (assets is not null && _swapBorrowed >= 0 && _swapBorrowed < assets.Length)
                assets[_swapBorrowed] = _swapTexture;
            if (frames is not null && _swapBorrowed >= 0 && _swapBorrowed < frames.Length)
                frames[_swapBorrowed] = _swapFrames;
            _swapNpc.type = _swapOwn;
        }
        catch (Exception e)
        {
            Log.Warn($"[npc] AnimationType 还原异常：{e.Message}");
        }
        finally
        {
            _swapNpc = null;
        }
    }

#endregion

#region 绘制

    private void DrawNpcs(On.Terraria.Main.orig_DrawNPCs orig, Main self, bool behindTiles)
    {
        try
        {
            if (!Main.dedServ && ModContent.Count(ContentKind.Npc) > 0)
            {
                var npcs = Main.npc;
                var batch = Main.spriteBatch;
                if (npcs is not null && batch is not null)
                {
                    var screen = Main.screenPosition;
                    for (var i = 0; i < npcs.Length; i++)
                    {
                        var npc = npcs[i];
                        if (npc is null || !npc.active || npc.type < VanillaCounts.Npc) continue;
                        if (npc.behindTiles != behindTiles) continue;

                        try
                        {
                            self.DrawNPCDirect(batch, npc, behindTiles, screen);
                        }
                        catch (Exception e)
                        {
                            Fail(npc, HookId.PreDraw, e);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[npc] DrawNPCs 补画异常：{e.Message}");
        }

        orig(self, behindTiles);
    }

    private void DrawDirect(On.Terraria.Main.orig_DrawNPCDirect_SpriteBatch_NPC_bool_Vector2 orig, Main self, SpriteBatch batch, NPC npc,
                            bool behindTiles, Vector2 screenPos)
    {
        if (!DrawPrefix(npc, batch, screenPos, out var go, out var frame))
        {
            orig(self, batch, npc, behindTiles, screenPos);
            return;
        }

        try
        {
            if (go) orig(self, batch, npc, behindTiles, screenPos);
        }
        finally
        {
            if (_drawDepth > 0) _drawDepth--;
        }

        DrawPost(npc, batch, screenPos, in frame);
    }

    private void DrawDirectLit(On.Terraria.Main.orig_DrawNPCDirect_SpriteBatch_NPC_bool_Vector2_LightMap_RectangleRef orig, Main self, SpriteBatch batch, NPC npc,
                               bool behindTiles, Vector2 screenPos, LightMap lightMap, ref Rectangle lightRegion)
    {
        if (!DrawPrefix(npc, batch, screenPos, out var go, out var frame))
        {
            orig(self, batch, npc, behindTiles, screenPos, lightMap, ref lightRegion);
            return;
        }

        try
        {
            if (go) orig(self, batch, npc, behindTiles, screenPos, lightMap, ref lightRegion);
        }
        finally
        {
            if (_drawDepth > 0) _drawDepth--;
        }

        DrawPost(npc, batch, screenPos, in frame);
    }

    private bool DrawPrefix(NPC npc, SpriteBatch batch, Vector2 screenPos, out bool go, out NpcDrawFrame frame)
    {
        go = true;
        frame = default;

        var mod = _binding.InstanceOf(npc);
        var effects = GlobalNpcRegistry.For(npc, HookId.DrawEffects);
        var pre = GlobalNpcRegistry.For(npc, HookId.PreDraw);

        var modDraws = mod is not null &&
                       (mod.HookEnabled(HookId.DrawEffects) || mod.HookEnabled(HookId.PreDraw) || mod.HookEnabled(HookId.PostDraw));
        if (!modDraws && effects.Count == 0 && pre.Count == 0 && !GlobalNpcRegistry.AnyWith(HookId.PostDraw))
            return false;

        var light = LightAt(npc);
        var before = light.PackedValue;

        if (mod is not null && mod.HookEnabled(HookId.DrawEffects))
        {
            try
            {
                mod.DrawEffects(npc, ref light);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.DrawEffects, e);
            }
        }

        foreach (var global in effects)
        {
            try
            {
                global.DrawEffects(npc, ref light);
            }
            catch (Exception e)
            {
                Fail(global, HookId.DrawEffects, e);
            }
        }

        foreach (var global in pre)
        {
            try
            {
                if (!global.PreDraw(npc, batch, screenPos, ref light)) go = false;
            }
            catch (Exception e)
            {
                Fail(global, HookId.PreDraw, e);
            }
        }

        if (go && mod is not null && mod.HookEnabled(HookId.PreDraw))
        {
            try
            {
                go = mod.PreDraw(npc, batch, screenPos, light);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PreDraw, e);
                go = true;
            }
        }

        frame = new NpcDrawFrame
        {
            Address = NpcBinding.Address(npc),
            Light = light,
            Tinted = light.PackedValue != before,
            Mod = mod,
        };

        if (_drawDepth < FrameDepth) _draws[_drawDepth++] = frame;
        return true;
    }

    private void DrawPost(NPC npc, SpriteBatch batch, Vector2 screenPos, in NpcDrawFrame frame)
    {
        if (frame.Mod is not null && frame.Mod.HookEnabled(HookId.PostDraw))
        {
            try
            {
                frame.Mod.PostDraw(npc, batch, screenPos, frame.Light);
            }
            catch (Exception e)
            {
                Fail(frame.Mod, HookId.PostDraw, e);
            }
        }

        foreach (var global in GlobalNpcRegistry.For(npc, HookId.PostDraw))
        {
            try
            {
                global.PostDraw(npc, batch, screenPos, frame.Light);
            }
            catch (Exception e)
            {
                Fail(global, HookId.PostDraw, e);
            }
        }
    }

    private Color TintedByBuffs(On.Terraria.NPC.orig_GetNPCColorTintedByBuffs orig, NPC self, Color npcColor)
    {
        if (_drawDepth > 0)
        {
            ref var frame = ref Draw;
            if (frame.Tinted && frame.Address == NpcBinding.Address(self)) return frame.Light;
        }

        return orig(self, npcColor);
    }

    private Color Alpha(On.Terraria.NPC.orig_GetAlpha orig, NPC self, Color newColor)
    {
        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.GetAlpha);
        if (mod is null && globals.Count == 0) return orig(self, newColor);

        try
        {
            foreach (var global in globals)
            {
                if (global.GetAlpha(self, newColor) is not { } verdict) continue;
                return verdict;
            }

            if (mod is not null && mod.HookEnabled(HookId.GetAlpha) && mod.GetAlpha(self, newColor) is { } mine) return mine;
        }
        catch (Exception e)
        {
            Fail(mod, HookId.GetAlpha, e);
        }

        return orig(self, newColor);
    }

    private int BossHeadSlot(On.Terraria.NPC.orig_GetBossHeadTextureIndex orig, NPC self)
    {
        var slot = orig(self);
        var registered = NpcHeadHooks.BossSlot(self.type);
        if (registered >= 0) slot = registered;

        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.BossHeadSlot);
        if (mod is null && globals.Count == 0) return slot;

        if (mod is not null && mod.HookEnabled(HookId.BossHeadSlot))
        {
            try { mod.BossHeadSlot(self, ref slot); }
            catch (Exception e) { Fail(mod, HookId.BossHeadSlot, e); }
        }
        foreach (var global in globals)
        {
            try { global.BossHeadSlot(self, ref slot); }
            catch (Exception e) { Safe.Report($"[npc] {global.Name}.BossHeadSlot", e); }
        }

        return slot;
    }

    private float BossHeadRotation(On.Terraria.NPC.orig_GetBossHeadRotation orig, NPC self)
    {
        var rotation = orig(self);

        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.BossHeadRotation);
        if (mod is null && globals.Count == 0) return rotation;

        try
        {
            if (mod is not null && mod.HookEnabled(HookId.BossHeadRotation)) mod.BossHeadRotation(self, ref rotation);
            foreach (var global in globals) global.BossHeadRotation(self, ref rotation);
        }
        catch (Exception e)
        {
            Fail(mod, HookId.BossHeadRotation, e);
        }

        return rotation;
    }

    private SpriteEffects BossHeadEffects(On.Terraria.NPC.orig_GetBossHeadSpriteEffects orig, NPC self)
    {
        var effects = orig(self);

        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.BossHeadSpriteEffects);
        if (mod is null && globals.Count == 0) return effects;

        try
        {
            if (mod is not null && mod.HookEnabled(HookId.BossHeadSpriteEffects))
                mod.BossHeadSpriteEffects(self, ref effects);
            foreach (var global in globals) global.BossHeadSpriteEffects(self, ref effects);
        }
        catch (Exception e)
        {
            Fail(mod, HookId.BossHeadSpriteEffects, e);
        }

        return effects;
    }

    private static Color LightAt(NPC npc)
    {
        try
        {
            var x = npc.position.X + npc.width * 0.5f;
            var y = npc.position.Y + npc.height * 0.5f;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return Color.White;

            return Lighting.GetColor((int)(x / 16f), (int)(y / 16f));
        }
        catch
        {
            return Color.White;
        }
    }

#endregion

#region 工具

    private void Fail(NPC npc, HookId hook, Exception exception) => FailCore(npc.type, hook, exception);

    private void Fail(object? owner, HookId hook, Exception exception) =>
        FailCore(owner?.GetType().Name.GetHashCode() ?? 0, hook, exception);

    private void FailCore(int key, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(key, hook))) return;
        ContentErrors.Report($"[npc] {hook} 异常：{Safe.Describe(exception)}");
    }

    private struct NpcDrawFrame
    {
        public long Address;
        public Color Light;
        public bool Tinted;
        public ModNpc? Mod;
    }
#endregion

}

/// <summary>NPC 编号相关的原生表：NPC、Lang、贴图、对话、表情。</summary>
internal static class NpcTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.ID.NPCID.Sets),
        typeof(Terraria.Main),
        typeof(Terraria.Lang),
        typeof(Terraria.NPC),
        typeof(Terraria.GameContent.TextureAssets),
        typeof(Terraria.GameContent.UI.EmoteBubble),
        typeof(Terraria.GameContent.ConditionalDialogue),
    ];

    internal static readonly TableSet Set = new("NPC", () => Terraria.ID.NPCID.Count, Types);

}
