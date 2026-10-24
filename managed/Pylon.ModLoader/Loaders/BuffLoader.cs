// BuffLoader：ModBuff / GlobalBuff 的钩子接线——buff 不是实体，钩子都是"遍历 buffType/buffTime 数组"。
// 每帧：ModBuff 移掉 buff 就回退重跑；GlobalBuff 整轮共用可写索引，type 0 = 到头。加 buff / 文本 / 图标 / 取消，顺序 Global → ModBuff → 原版。
// Pylon 专有：宿主槽位换类型（原版有编译期 BuffID.Count 比较，IL2CPP 无 IL 可改，调用期临时映射用完还原）；
// buffImmune 每实例定长，内容就绪后统一加长；不 patch SpriteBatch.Draw（真机串 SpriteEffects），PreDraw false 连时间文字一起不画。
// Harmony IL2CPP 约束：补丁体只调静态方法；参数按名字绑定（不碰的参数别声明）；失败写日志不静默。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader
{
    /// <summary>ModBuff / GlobalBuff 的钩子接线。</summary>
    internal static class BuffLoader
    {
        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _installed;

        internal static void Install(Harmony harmony, ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            _harmony = harmony;
            _logger = logger;

            GameHooks.Install(harmony, logger, typeof(BuffLoader), Targets());
            _logger?.Info("[buff] BuffLoader 已挂（更新/加/文本/图标/取消）");
        }

        // 一次装全所有钩子（后来的 mod 也能被覆盖到），分派处再用 OverridePlan/注册表按需跳过。
        private static GameHooks.Hook[] Targets() => new[]
        {
            // ---- 每帧 ----
            new GameHooks.Hook { Type = typeof(Player), Method = "UpdateBuffs", Args = new[] { typeof(int) }, Handler = nameof(PlayerBuffsPostfix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "UpdateNPC_BuffSetFlags", Args = new[] { typeof(bool) }, Handler = nameof(NpcBuffsPostfix) },

            // ---- 加 buff ----
            // Pylon 专有：原版没有这条，用 AddBuff 前补 buffImmune 扩容。
            new GameHooks.Hook { Type = typeof(Player), Method = "AddBuff", Args = new[] { typeof(int), typeof(int), typeof(bool) },
                Prefix = true, Handler = nameof(PlayerAddBuffPrefix) },
            new GameHooks.Hook { Type = typeof(Player), Method = "AddBuff_ActuallyTryToAddTheBuff", Args = new[] { typeof(int), typeof(int) },
                Handler = nameof(AppliedPlayerPostfix) },
            new GameHooks.Hook { Type = typeof(Player), Method = "AddBuff_TryUpdatingExistingBuffTime", Args = new[] { typeof(int), typeof(int) },
                Prefix = true, Handler = nameof(ReApplyPlayerPrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "AddBuff", Args = new[] { typeof(int), typeof(int), typeof(bool) },
                Prefix = true, Handler = nameof(NpcAddBuffPrefix) },
            new GameHooks.Hook { Type = typeof(NPC), Method = "AddBuff", Args = new[] { typeof(int), typeof(int), typeof(bool) },
                Handler = nameof(NpcAddBuffPostfix) },

            // ---- 文本 ----
            new GameHooks.Hook { Type = typeof(Lang), Method = "GetBuffName", Args = new[] { typeof(int) }, Handler = nameof(GetBuffNamePostfix) },
            new GameHooks.Hook { Type = typeof(Main), Method = "GetBuffTooltip", Args = new[] { typeof(Player), typeof(int) },
                Handler = nameof(GetBuffTooltipPostfix) },

            // ---- 条上图标 ----
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "ItemDraw",
                Args = new[] { typeof(ItemGrid_Layout), typeof(int), typeof(Vector2), typeof(float) },
                Prefix = true, Handler = nameof(ItemDrawPrefix) },
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "ItemDraw",
                Args = new[] { typeof(ItemGrid_Layout), typeof(int), typeof(Vector2), typeof(float) },
                Handler = nameof(ItemDrawPostfix) },

            // ---- 点图标取消 ----
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "RemoveBuff", Args = new[] { typeof(int) },
                Prefix = true, Handler = nameof(RemoveBuffPrefix) },
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "RemoveBuff", Args = new[] { typeof(int) },
                Handler = nameof(RemoveBuffPostfix) },

            // ---- 宿主槽位（Pylon 专有，见文件头 ①）----
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "Draw", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(SwapPrefix) },
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "Draw", Args = Type.EmptyTypes, Handler = nameof(SwapPostfix) },
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "UpdateBuffNavigation", Args = Type.EmptyTypes, Prefix = true, Handler = nameof(SwapPrefix) },
            new GameHooks.Hook { Type = typeof(GUIBuffs), Method = "UpdateBuffNavigation", Args = Type.EmptyTypes, Handler = nameof(SwapPostfix) },
        };

        private static ModBuff Of(int type) => ModContent.Find(ContentKind.Buff, type) as ModBuff;

        private static bool HasModBuffs() => ModContent.Count(ContentKind.Buff) > 0;

        // ================= buffImmune 数组扩容 =================
        // Player/NPC.buffImmune 是每实例 bool[BuffID.Count]；没变长加 mod buff 就越界（真机：喝药水没反应、没图标）。

        private static int BuffCount() => ModContent.NextType(ContentKind.Buff);

        /// <summary>把所有玩家/NPC 的 buffImmune 扩到当前 buff 总数（内容就绪后调一次）。</summary>
        internal static void GrowImmuneArrays()
        {
            var need = BuffCount();
            if (need <= VanillaCounts.Buff) return;
            try
            {
                var players = Main.player;
                if (players != null)
                    for (var i = 0; i < players.Length; i++) GrowImmune(players[i], need);
                var npcs = Main.npc;
                if (npcs != null)
                    for (var i = 0; i < npcs.Length; i++) GrowImmune(npcs[i], need);
            }
            catch (Exception e) { _logger?.Warn($"[buff] buffImmune 扩容失败：{e.Message}"); }
        }

        // 后建的实例由 AddBuff 前的这两条再补一次。
        private static void PlayerAddBuffPrefix(Player __instance) => GrowImmune(__instance, BuffCount());

        private static void GrowImmune(Player player, int need)
        {
            if (player == null) return;
            try
            {
                var old = player.buffImmune;
                if (old != null && old.Length >= need) return;
                player.buffImmune = Grow(old, need);
            }
            catch (Exception e) { Safe.Report("Player.buffImmune", e); }
        }

        private static void GrowImmune(NPC npc, int need)
        {
            if (npc == null) return;
            try
            {
                var old = npc.buffImmune;
                if (old != null && old.Length >= need) return;
                npc.buffImmune = Grow(old, need);
            }
            catch (Exception e) { Safe.Report("NPC.buffImmune", e); }
        }

        // ⚠ 不能 new Il2CppStructArray<T>(len) 再逐元素写：interop 的 setref 在 ARM64 上直接 SIGSEGV
        // （PC=0x7cf774，真机踩过）。托管数组 + 隐式转换（内部原生拷贝）才安全。
        private static bool[] Grow(Il2CppStructArray<bool> old, int need)
        {
            var grown = new bool[need];
            if (old != null) for (var i = 0; i < old.Length && i < need; i++) grown[i] = old[i];
            return grown;
        }

        // ================= 每帧更新 =================

        private static void PlayerBuffsPostfix(Player __instance)
        {
            var player = __instance;
            if (player == null) return;
            var types = player.buffType;
            var times = player.buffTime;
            if (types == null || times == null) return;
            UpdateModBuffs(types, times, player, null);
            UpdateGlobalBuffs(types, times, player, null);
        }

        private static void NpcBuffsPostfix(NPC __instance)
        {
            var npc = __instance;
            if (npc == null) return;
            var types = npc.buffType;
            var times = npc.buffTime;
            if (types == null || times == null) return;
            UpdateModBuffs(types, times, null, npc);
            UpdateGlobalBuffs(types, times, null, npc);
        }

        // ModBuff：取完 type 再查有没有被移掉——移掉了前移一格要回头重跑；玩家/NPC 回调不同、遍历共用。
        private static void UpdateModBuffs(Il2CppStructArray<int> types, Il2CppStructArray<int> times,
                                           Player player, NPC npc)
        {
            if (!HasModBuffs()) return;
            var hook = player != null ? "UpdatePlayer" : "UpdateNPC";
            for (var k = 0; k < types.Length && k < times.Length; k++)
            {
                var type = types[k];
                if (type < VanillaCounts.Buff || times[k] <= 0) continue;
                var mod = Of(type);
                if (mod == null || !mod.Plan.Has(hook)) continue;
                try
                {
                    if (player != null) mod.UpdatePlayer(player, k);
                    else mod.UpdateNPC(npc, k);
                }
                catch (Exception e) { Safe.Report($"{mod.Name}.{hook}", e); }
                if (types[k] != type) k--;
            }
        }

        // GlobalBuff：所有 buff（含原版）；type == 0 就是列表到头。
        private static void UpdateGlobalBuffs(Il2CppStructArray<int> types, Il2CppStructArray<int> times,
                                              Player player, NPC npc)
        {
            var hook = player != null ? "UpdatePlayer" : "UpdateNPC";
            var globals = GlobalBuffRegistry.For(hook);
            if (globals.Count == 0) return;
            for (var k = 0; k < types.Length && k < times.Length; k++)
            {
                var type = types[k];
                if (type == 0) break;
                if (times[k] <= 0) continue;

                var index = k;   // 整轮 Global 共用同一个可写索引
                foreach (var g in globals)
                {
                    try
                    {
                        if (player != null) g.UpdatePlayer(type, player, ref index);
                        else g.UpdateNPC(type, npc, ref index);
                    }
                    catch (Exception e) { Safe.Report($"{g.Name}.{hook}", e); }
                }
                if (index != k) k = index;
            }
        }

        // ================= 加 buff =================

        // 原版说加上了，才调 ApplyPlayer。
        private static void AppliedPlayerPostfix(Player __instance, int type, int time, ref bool __result)
        {
            if (!__result) return;
            var mod = Of(type);
            if (mod == null || !mod.Plan.Has("ApplyPlayer")) return;
            try { mod.ApplyPlayer(__instance, time); }
            catch (Exception e) { Safe.Report($"{mod.Name}.ApplyPlayer", e); }
        }

        // 返回 true = "已经有了"：游戏不再放一个 / 不再续时间。
        private static bool ReApplyPlayerPrefix(Player __instance, int type, int time, ref bool __result)
        {
            try
            {
                var player = __instance;
                var index = player.FindBuffIndex(type);

                // gbuff.ReApplyPlayer：任一 Global 返回 true 就不续时间（会跑完全部）。
                if (index >= 0)
                {
                    var stop = false;
                    foreach (var g in GlobalBuffRegistry.For("ReApplyPlayer"))
                    {
                        var slot = index;
                        try { if (g.ReApplyPlayer(type, player, time, ref slot)) stop = true; }
                        catch (Exception e) { Safe.Report($"{g.Name}.ReApplyPlayer", e); }
                    }
                    if (stop) { __result = true; return false; }
                }

                var mod = Of(type);
                if (mod == null) return true;   // 不是 mod buff：交回原版

                // index < 0 时 renew = true（照常加）；否则问 mod，false = 不续时间。
                var renew = index < 0 || !mod.Plan.Has("ReApplyPlayer") || mod.ReApplyPlayer(player, time, index);
                if (!renew) { __result = true; return false; }
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("buff.ReApplyPlayer", e);
                return true;
            }
        }

        /// <summary>NPC.AddBuff 的一次调用；prefix 挂、postfix 摘（成对，除非 prefix 抛错——它不会）。</summary>
        private sealed class NpcAddBuffFrame
        {
            public NPC Npc;
            public int Type;
            public int Time;
            public bool Apply;   // 原版真的加上去了才调 ApplyNPC
            public NpcAddBuffFrame Outer;
        }

        private static NpcAddBuffFrame _npcAddBuff;

        private static bool NpcAddBuffPrefix(NPC __instance, int type, int time)
        {
            // 先挂上：Harmony 就算跳过原版也会跑 postfix，成对才不会被嵌套调用打乱。
            var frame = new NpcAddBuffFrame { Npc = __instance, Type = type, Time = time, Outer = _npcAddBuff };
            _npcAddBuff = frame;
            try
            {
                GrowImmune(__instance, BuffCount());   // Pylon 专有 ②

                var index = __instance.FindBuffIndex(type);

                // gbuff.ReApplyNPC：任一 Global 返回 true 就整个不续（连原版都不调）。
                if (index >= 0)
                {
                    var stop = false;
                    foreach (var g in GlobalBuffRegistry.For("ReApplyNPC"))
                    {
                        var slot = index;
                        try { if (g.ReApplyNPC(type, __instance, time, ref slot)) stop = true; }
                        catch (Exception e) { Safe.Report($"{g.Name}.ReApplyNPC", e); }
                    }
                    if (stop) return false;
                }

                var mod = Of(type);
                if (mod == null) return true;   // 不是 mod buff：交回原版

                if (index >= 0)
                {
                    // ModBuff.ReApplyNPC 的 false 与 GlobalBuff 相反——false = 不续时间。
                    if (!mod.Plan.Has("ReApplyNPC")) return true;
                    return mod.ReApplyNPC(__instance, time, index);
                }

                frame.Apply = mod.Plan.Has("ApplyNPC");
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("buff.ReApplyNPC", e);
                return true;
            }
        }

        private static void NpcAddBuffPostfix(NPC __instance, int type)
        {
            var frame = _npcAddBuff;
            if (frame == null) return;
            _npcAddBuff = frame.Outer;
            if (!frame.Apply) return;
            // 被免疫/没加上就没有 ApplyNPC（回头查一次列表）。
            try
            {
                if (frame.Npc == null || frame.Npc.FindBuffIndex(frame.Type) < 0) return;
                var mod = Of(frame.Type);
                if (mod != null) mod.ApplyNPC(frame.Npc, frame.Time);
            }
            catch (Exception e) { Safe.Report("buff.ApplyNPC", e); }
        }

        // ================= 文本 =================

        private static bool _texting;   // 在 ModifyBuffText 里：名字/说明直接走原版，防止递归

        /// <summary>这个名字/说明有人要改吗？</summary>
        private static bool HasText(int type)
        {
            var mod = Of(type);
            return (mod != null && mod.Plan.Has("ModifyBuffText")) || GlobalBuffRegistry.For("ModifyBuffText").Count > 0;
        }

        private static void ModifyText(int type, ref string name, ref string tip)
        {
            var rare = 0;
            _texting = true;
            try
            {
                var mod = Of(type);
                if (mod != null && mod.Plan.Has("ModifyBuffText"))
                {
                    try { mod.ModifyBuffText(ref name, ref tip, ref rare); }
                    catch (Exception e) { Safe.Report($"{mod.Name}.ModifyBuffText", e); }
                }
                foreach (var g in GlobalBuffRegistry.For("ModifyBuffText"))
                {
                    try { g.ModifyBuffText(type, ref name, ref tip, ref rare); }
                    catch (Exception e) { Safe.Report($"{g.Name}.ModifyBuffText", e); }
                }
            }
            finally { _texting = false; }
        }

        private static void GetBuffNamePostfix(int id, ref string __result)
        {
            if (_texting) return;
            var type = RealTypeOf(id);          // 换槽期间问到的是宿主类型
            if (!HasText(type)) return;

            try
            {
                var name = __result;
                var tip = Lang.GetBuffDescription(id);
                ModifyText(type, ref name, ref tip);
                __result = name ?? "";
            }
            catch (Exception e) { Safe.Report("Lang.GetBuffName", e); }
        }

        private static void GetBuffTooltipPostfix(Player player, int buffType, ref string __result)
        {
            if (_texting) return;
            var type = RealTypeOf(buffType);
            if (!HasText(type)) return;

            try
            {
                string name;
                _texting = true;
                try { name = Lang.GetBuffName(type); }
                finally { _texting = false; }

                var tip = __result;
                ModifyText(type, ref name, ref tip);
                __result = tip ?? "";
            }
            catch (Exception e) { Safe.Report("Main.GetBuffTooltip", e); }
        }

        // ================= 条上图标 =================

        private sealed class DrawFrame
        {
            public ModBuff Mod;
            public int Type;
            public int BuffIndex;
            public BuffDrawParams Params;
            public bool Dispatch;   // 分派过了（否则 postfix 什么都不做）
            public DrawFrame Outer;
        }

        private static DrawFrame _draw;

        private static bool ItemDrawPrefix(GUIBuffs __instance, int index, Vector2 position)
        {
            var globalsPre = GlobalBuffRegistry.For("PreDraw");
            var globalsPost = GlobalBuffRegistry.For("PostDraw");
            if (globalsPre.Count == 0 && globalsPost.Count == 0 && !HasModBuffs()) return true;

            // 先挂上：跳不跳原版 postfix 都会跑，成对才能扛住嵌套调用。
            var frame = new DrawFrame { Outer = _draw };
            _draw = frame;
            try
            {
                var gui = __instance;
                if (gui == null || index < 0 || index >= gui.numActiveBuffs) return true;
                var activeBuffs = gui.activeBuffs;
                if (activeBuffs == null || index >= activeBuffs.Length) return true;

                var buffIndex = activeBuffs[index];   // 条上第 index 格 = 玩家列表里的第 buffIndex 个
                var player = Main.player[Main.myPlayer];
                var types = player != null ? player.buffType : null;
                if (types == null || buffIndex < 0 || buffIndex >= types.Length) return true;

                var type = RealType(player, buffIndex);
                if (type <= 0) return true;
                var mod = Of(type);
                var modPre = mod != null && mod.Plan.Has("PreDraw");
                var modPost = mod != null && mod.Plan.Has("PostDraw");
                if (!modPre && !modPost && globalsPre.Count == 0 && globalsPost.Count == 0) return true;

                var drawParams = NewDrawParams(type, position);
                var draw = true;
                foreach (var g in globalsPre)
                {
                    // Global 的 All：**全跑**，任一个返回 false 就不画（不短路）。
                    try { if (!g.PreDraw(Main.spriteBatch, type, buffIndex, drawParams)) draw = false; }
                    catch (Exception e) { Safe.Report($"{g.Name}.PreDraw", e); }
                }
                if (draw && modPre)
                {
                    try { draw = mod.PreDraw(Main.spriteBatch, buffIndex, drawParams); }
                    catch (Exception e) { Safe.Report($"{mod.Name}.PreDraw", e); draw = true; }   // 出错不算 false
                }

                frame.Mod = mod;
                frame.Type = type;
                frame.BuffIndex = buffIndex;
                frame.Params = drawParams;
                frame.Dispatch = true;

                // false = 连图标和时间文字都不画（手机版两者在同一个 ItemDraw 里）。
                return draw;
            }
            catch (Exception e)
            {
                Safe.Report("buff.PreDraw", e);
                return true;
            }
        }

        private static void ItemDrawPostfix(GUIBuffs __instance, int index)
        {
            var frame = _draw;
            if (frame == null) return;
            _draw = frame.Outer;
            if (!frame.Dispatch) return;

            // PostDraw 不管 PreDraw 有没有说不画，都要跑。
            try
            {
                if (frame.Mod != null && frame.Mod.Plan.Has("PostDraw"))
                {
                    try { frame.Mod.PostDraw(Main.spriteBatch, frame.BuffIndex, frame.Params); }
                    catch (Exception e) { Safe.Report($"{frame.Mod.Name}.PostDraw", e); }
                }
                foreach (var g in GlobalBuffRegistry.For("PostDraw"))
                {
                    try { g.PostDraw(Main.spriteBatch, frame.Type, frame.BuffIndex, frame.Params); }
                    catch (Exception e) { Safe.Report($"{g.Name}.PostDraw", e); }
                }
            }
            catch (Exception e) { Safe.Report("buff.PostDraw", e); }
        }

        /// <summary>BuffDrawParams 的初值：原版"什么都不改"时那样。</summary>
        private static BuffDrawParams NewDrawParams(int type, Vector2 position)
        {
            var texture = BuffTexture(type);
            var width = 0;
            var height = 0;
            if (texture != null)
            {
                try { Textures.PylonTexture.Size(texture, out width, out height); }
                catch { }
            }

            return new BuffDrawParams
            {
                Texture = texture,
                Position = position,
                TextPosition = position,
                SourceRectangle = new Rectangle(0, 0, width, height),
                MouseRectangle = new Rectangle((int)(position.X - width / 2f), (int)(position.Y - height / 2f), width, height),
                DrawColor = Color.White,
            };
        }

        private static Texture2D BuffTexture(int type)
        {
            try
            {
                var textures = TextureAssets.Buff;
                if (textures == null || type < 0 || type >= textures.Length) return null;
                return textures[type]?.Value;
            }
            catch { return null; }
        }

        // ================= 点图标取消 =================

        private sealed class RemoveFrame
        {
            public Player Player;
            public int Type;
            public int Index;
            public int Time;
            public RemoveFrame Outer;
        }

        private static RemoveFrame _remove;

        private static bool RemoveBuffPrefix(GUIBuffs __instance, int buff)
        {
            // 先挂上：跳不跳原版 postfix 都会跑，成对才不会被嵌套调用打乱。
            var frame = new RemoveFrame { Index = buff, Outer = _remove };
            _remove = frame;
            try
            {
                var player = Main.player[Main.myPlayer];
                frame.Player = player;
                var types = player != null ? player.buffType : null;
                var times = player != null ? player.buffTime : null;
                if (types == null || times == null) return true;
                if (buff < 0 || buff >= types.Length || buff >= times.Length) return true;

                var type = RealType(player, buff);   // 换槽期间这里是宿主类型
                frame.Type = type;
                frame.Time = times[buff];

                // gbuff.RightClick（链在外层，先跑）：全跑，任一个 false 就整个不取消。
                if (type > 0)
                {
                    var allowed = true;
                    foreach (var g in GlobalBuffRegistry.For("RightClick"))
                    {
                        try { if (!g.RightClick(type, buff)) allowed = false; }
                        catch (Exception e) { Safe.Report($"{g.Name}.RightClick", e); }
                    }
                    if (!allowed) return false;
                }

                var mod = Of(type);
                if (mod == null) return true;

                var debuffs = Main.debuff;
                var debuff = debuffs != null && type >= 0 && type < debuffs.Length && debuffs[type];
                bool? can = null;
                if (mod.Plan.Has("CanRemove"))
                {
                    try { can = mod.CanRemove(player, frame.Time, buff, debuff); }
                    catch (Exception e) { Safe.Report($"{mod.Name}.CanRemove", e); }
                }

                if (can == false) return false;   // buff 留着
                if (can == true && debuff)
                {
                    // 减益：直接扣掉，不让"再点一下确认"拦着。
                    player.DelBuff(buff);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("buff.CanRemove", e);
                return true;
            }
        }

        private static void RemoveBuffPostfix(GUIBuffs __instance, int buff)
        {
            var frame = _remove;
            if (frame == null) return;
            _remove = frame.Outer;

            try
            {
                var player = frame.Player;
                if (player == null || frame.Type == 0) return;
                var types = player.buffType;
                if (types == null || frame.Index < 0 || frame.Index >= types.Length) return;
                if (RealType(player, frame.Index) == frame.Type) return;   // 没被去掉

                var mod = Of(frame.Type);
                if (mod == null || !mod.Plan.Has("OnRemove")) return;
                mod.OnRemove(player, frame.Time, frame.Index);
            }
            catch (Exception e) { Safe.Report("buff.OnRemove", e); }
        }

        // ================= 显示：mod buff 临时映射到空的原版槽位（Pylon 专有） =================
        // 原版 Draw/UpdateBuffNavigation 有编译期 BuffID.Count 比较，mod buff 会被跳过甚至抹掉；IL2CPP 无 IL 可改，
        // 就调用期间把 mod buff 换到这个玩家没占用的原版槽位（贴图/名字/说明/减益/无时间/不存档一起换），结束即还原。
        // 位置与计时不受影响：图标、时间、取消都仍按 buff 索引走。

        private struct TypeSwap
        {
            public int Index;      // 玩家 buff 列表里的槽位
            public int Original;   // 真实（mod）类型
            public int Host;       // 临时顶替的原版类型
        }

        private sealed class SwapFrame
        {
            public Player Player;
            public readonly List<TypeSwap> Types = new();
            public readonly List<(int Host, Asset<Texture2D> Saved)> Assets = new();
            public readonly List<(int Host, bool Saved)> Debuffs = new();
            public readonly List<(int Host, bool Saved)> NoTime = new();
            public readonly List<(int Host, bool Saved)> NoSave = new();
            public readonly List<(int Host, Terraria.Localization.LocalizedText Saved)> Names = new();
            public readonly List<(int Host, Terraria.Localization.LocalizedText Saved)> Descriptions = new();
        }

        private static SwapFrame _swap;
        private static int _swapDepth;

        private static bool SwapPrefix()
        {
            if (_swap != null) { _swapDepth++; return true; }   // 已在换过的那一层里（Draw 里又调导航之类）
            if (!HasModBuffs()) return true;

            try
            {
                var player = Main.player[Main.myPlayer];
                var types = player != null ? player.buffType : null;
                var times = player != null ? player.buffTime : null;
                if (types == null || times == null) return true;

                // 宿主候选 = 这个玩家身上没有的原版 buff（宿主类型在列表里唯一，还原才能按值做）。
                var used = new bool[VanillaCounts.Buff];
                var count = Math.Min(types.Length, times.Length);
                for (var i = 0; i < count; i++)
                {
                    var t = types[i];
                    if (t > 0 && t < VanillaCounts.Buff) used[t] = true;
                }

                var textures = TextureAssets.Buff;
                var frame = new SwapFrame { Player = player };
                // 先挂上：换到一半抛异常时，postfix 也能把已经换掉的那几个还原（按值还原，空表就是空转）。
                _swap = frame;
                _swapDepth = 1;
                var host = 1;
                for (var i = 0; i < count; i++)
                {
                    var type = types[i];
                    if (type < VanillaCounts.Buff || times[i] <= 0) continue;
                    var mod = Of(type);
                    if (mod == null) continue;

                    // 用 mod 自己持有的 Asset（表槽可能被游戏重建踩掉）；没有贴图就不映射：宁可不出图标，
                    // 也不能冒用宿主的贴图与名字（真机：坐骑 buff 显示成黑曜石药水）。
                    var texture = mod.Texture;
                    if (texture == null && textures != null && type < textures.Length) texture = textures[type];
                    if (texture == null) continue;

                    while (host < VanillaCounts.Buff && used[host]) host++;
                    if (host >= VanillaCounts.Buff) break;   // 原版槽位用完了
                    var slot = host++;

                    frame.Types.Add(new TypeSwap { Index = i, Original = type, Host = slot });
                    types[i] = slot;
                    if (textures != null && slot < textures.Length)
                    {
                        frame.Assets.Add((slot, textures[slot]));
                        textures[slot] = texture;
                    }
                    SwapText(Lang._buffNameCache, type, slot, frame.Names);
                    SwapText(Lang._buffDescriptionCache, type, slot, frame.Descriptions);
                    SwapFlag(Main.debuff, type, slot, frame.Debuffs);
                    // 坐骑等 buff 是"不显示时间/不存档"的：这两张表也换，否则条上会冒出 0s。
                    SwapFlag(Main.buffNoTimeDisplay, type, slot, frame.NoTime);
                    SwapFlag(Main.buffNoSave, type, slot, frame.NoSave);
                }
            }
            catch (Exception e) { Safe.Report("buff.换槽", e); }
            return true;
        }

        private static void SwapPostfix()
        {
            var frame = _swap;
            if (frame == null) return;
            if (--_swapDepth > 0) return;   // 内层调用：外层还持有换槽
            _swap = null;

            try
            {
                RestoreTypes(frame);

                var textures = TextureAssets.Buff;
                foreach (var (slot, saved) in frame.Assets)
                    if (textures != null && slot >= 0 && slot < textures.Length) textures[slot] = saved;

                RestoreFlags(Main.buffNoTimeDisplay, frame.NoTime);
                RestoreFlags(Main.buffNoSave, frame.NoSave);
                RestoreFlags(Main.debuff, frame.Debuffs);
                RestoreText(Lang._buffNameCache, frame.Names);
                RestoreText(Lang._buffDescriptionCache, frame.Descriptions);
            }
            catch (Exception e) { Safe.Report("buff.换槽还原", e); }
        }

        /// <summary>这次绘制/导航里，玩家 buff 列表第 buffIndex 个真正对应的类型（换过就还原成 mod 类型）。</summary>
        private static int RealType(Player player, int buffIndex)
        {
            var type = player.buffType[buffIndex];
            var frame = _swap;
            if (frame == null) return type;
            foreach (var swap in frame.Types)
                if (swap.Index == buffIndex && swap.Host == type) return swap.Original;
            return type;
        }

        /// <summary>文本钩子拿到的类型可能是宿主：换回真实类型。</summary>
        private static int RealTypeOf(int type)
        {
            var frame = _swap;
            if (frame == null) return type;
            foreach (var swap in frame.Types)
                if (swap.Host == type) return swap.Original;
            return type;
        }

        // 按值还原：换槽窗口里 DelBuff 可能把列表前移，按索引写回会塞错槽甚至让已取消的 buff 复活；宿主类型这帧没有，按值安全。
        private static void RestoreTypes(SwapFrame frame)
        {
            var types = frame.Player?.buffType;
            if (types == null) return;
            for (var i = 0; i < types.Length; i++)
            {
                var host = types[i];
                if (host <= 0 || host >= VanillaCounts.Buff) continue;
                foreach (var swap in frame.Types)
                    if (swap.Host == host) { types[i] = swap.Original; break; }
            }
        }

        private static void SwapFlag(Il2CppStructArray<bool> table, int type, int host, List<(int Host, bool Saved)> log)
        {
            if (table == null || host < 0 || host >= table.Length || type < 0 || type >= table.Length) return;
            log.Add((host, table[host]));
            table[host] = table[type];
        }

        private static void RestoreFlags(Il2CppStructArray<bool> table, List<(int Host, bool Saved)> log)
        {
            if (table == null) return;
            foreach (var (slot, saved) in log)
                if (slot >= 0 && slot < table.Length) table[slot] = saved;
        }

        private static void SwapText(Il2CppReferenceArray<Terraria.Localization.LocalizedText> cache,
                                     int type, int host,
                                     List<(int Host, Terraria.Localization.LocalizedText Saved)> log)
        {
            if (cache == null || type < 0 || type >= cache.Length || host < 0 || host >= cache.Length) return;
            var text = cache[type];
            if (text == null) return;
            log.Add((host, cache[host]));
            cache[host] = text;
        }

        private static void RestoreText(Il2CppReferenceArray<Terraria.Localization.LocalizedText> cache,
                                        List<(int Host, Terraria.Localization.LocalizedText Saved)> log)
        {
            if (cache == null) return;
            foreach (var (slot, saved) in log)
                if (slot >= 0 && slot < cache.Length) cache[slot] = saved;
        }
    }
}
