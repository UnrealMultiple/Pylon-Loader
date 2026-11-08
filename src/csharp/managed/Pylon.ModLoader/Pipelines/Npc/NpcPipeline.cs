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

    private static IModLog Log => LoaderHost.Current.Log;

    private ref NpcDrawFrame Draw => ref _draws[_drawDepth - 1];

    public bool Install()
    {
        _binding.Prime();
        _defaults.Install();
        _town.Install();

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
        On.Terraria.NPC.GetChat += GetChat;
        On.Terraria.NPC.get_CanTalk += ForceCanTalk;
        On.Terraria.NPC.GetNPCColorTintedByBuffs += TintedByBuffs;
        On.Terraria.NPC.GetAlpha += Alpha;
        On.Terraria.NPC.GetBossHeadTextureIndex += BossHeadSlot;
        On.Terraria.NPC.GetBossHeadRotation += BossHeadRotation;
        On.Terraria.NPC.GetBossHeadSpriteEffects += BossHeadEffects;
        On.Terraria.Main.DrawNPCDirect += DrawDirect;
        On.Terraria.Main.DrawNPCDirect_1 += DrawDirectLit;
        On.Terraria.Main.CacheNPCDraws += CacheDraws;

        Log.Info("[npc] 已挂（AI / 事件 / 绘制 / 首领头像 / 城镇）");
        return true;
    }

    /// <summary>新 Mod 注册后模板变多：位集要重新挂到模板上。</summary>
    public void Refresh() => _binding.Prime();

#region 外部入口（作者 API 需要）

    internal bool StartClone(int type) => _cloning.Add(type);

    /// <summary>CloneDefaults 收尾：绑定换回原实例，Global 实体表按原类型重建。</summary>
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

    /// <summary>
    /// <c>Lang.GetNPCName</c> 把 <c>netID - 1 &gt; 695</c> 编进了机器码，
    /// mod 类型（&gt; 696）直接走"没有名字"分支——Lang._npcNameCache 写得再对，读出来仍是空。
    /// </summary>
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
            // 最后一格是游戏的空槽。
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

            // DrawBehind 的时机在 CacheNPCDraws，那时早已离开 ResetEffects：这里先记下来。
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
            if (!alive) break;
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

        // 联机客户端：掉落只由服务端算，OnKill 也不跑，否则表现与主机分叉。
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
            // 这版游戏的 NPC.difficulty 是 float（0 经典 / 1 专家 / 2 大师），不是枚举。
            var bossAdjustment = self.difficulty >= MasterDifficulty ? 0.85f : 1f;

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

    /// <summary>
    /// 进世界后补 NPC 类型表。<c>NPCID.Sets</c> 的字典会在世界加载时重建，安装期写的会丢；
    /// 只补"类/int 值"的表（刷怪规则、免疫），结构体值字典（图鉴偏移）不能从托管写。
    /// <para>
    /// <b>不补 <c>NPCPortraits</c></b>：原先拿"向导"的 provider 顶给 mod 类型，对话就显示向导的头像。
    /// 这张表不能拿别的 NPC 顶替：原版 <c>Player.SetTalkNPC</c> 先把 <c>Main.npcChatPortrait</c> 清空，
    /// 再用 <c>TryGetValue</c> 查表——查不到就保持空，对话照原版画 NPC 本体（与梅林等没有特写头像的
    /// 原版城镇 NPC 一致）。
    /// </para>
    /// </summary>
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

    /// <summary>
    /// <c>get_CanTalk</c> 钩子：原版对 <c>type &gt; n-1</c> 返回 false，mod 城镇 NPC 因此点不开对话。
    /// 作者的 <c>CanChat</c> 没否决就强制 true。
    /// </summary>
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

    /// <summary>把原版槽位的贴图与帧数临时指向自己，并把 type 换过去；返回是否真的换了。</summary>
    private bool SwapAnimation(NPC npc)
    {
        // 上一次没跑到还原（原版 FindFrame 抛错）：先补一次，别让槽位被永久占着。
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

        // 先存后改：万一某个写入抛错，还原还能把两个槽位写回去。
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

    private void DrawDirect(On.Terraria.Main.orig_DrawNPCDirect orig, Main self, SpriteBatch batch, NPC npc,
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

    private void DrawDirectLit(On.Terraria.Main.orig_DrawNPCDirect_1 orig, Main self, SpriteBatch batch, NPC npc,
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

    /// <summary>返回 false = 这条管线不参与，调用方直接走原版。</summary>
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

        var mod = _binding.InstanceOf(self);
        var globals = GlobalNpcRegistry.For(self, HookId.BossHeadSlot);
        if (mod is null && globals.Count == 0) return slot;

        try
        {
            if (mod is not null && mod.HookEnabled(HookId.BossHeadSlot)) mod.BossHeadSlot(self, ref slot);
            foreach (var global in globals) global.BossHeadSlot(self, ref slot);
        }
        catch (Exception e)
        {
            Fail(mod, HookId.BossHeadSlot, e);
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

    /// <summary>同一 (类型, 钩子) 只记一条：AI/绘制这类每帧路径出错会刷屏。</summary>
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
