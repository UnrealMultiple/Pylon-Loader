// BuffLoader：ModBuff / GlobalBuff 的钩子接线——buff 不是实体，钩子都是"遍历 buffType/buffTime 数组"。
// 每帧：ModBuff 移掉 buff 就回退重跑；GlobalBuff 整轮共用可写索引，type 0 = 到头。加 buff / 文本 / 图标 / 取消，顺序 Global → ModBuff → 原版。
// Pylon 专有：宿主槽位换类型（原版有编译期 BuffID.Count 比较，IL2CPP 无 IL 可改，调用期临时映射用完还原）；
// buffImmune 每实例定长，内容就绪后统一加长；不 patch SpriteBatch.Draw（真机串 SpriteEffects），PreDraw false 连时间文字一起不画。
// On.* 订阅：handler 首参 orig，原版之后要跑的就把 orig 放最前面；即使不用的参数也按生成签名声明。
using System;
using System.Collections.Generic;
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
        private static ModLogger _logger;
        private static bool _installed;

        internal static void Install(ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            _logger = logger;

            // 一次装全所有钩子（后来的 mod 也能被覆盖到），分派处再用 OverridePlan/注册表按需跳过。
            // ---- 每帧 ----
            On.Terraria.Player.UpdateBuffs += PlayerBuffs;
            On.Terraria.NPC.UpdateNPC_BuffSetFlags += NpcBuffs;

            // ---- 加 buff ----
            // 玩家 buff：ApplyPlayer 在 AddBuff_ActuallyTryToAddTheBuff 之后、ReApplyPlayer 在续时间处。
            On.Terraria.Player.AddBuff_ActuallyTryToAddTheBuff += AppliedPlayer;
            On.Terraria.Player.AddBuff_TryUpdatingExistingBuffTime += ReApplyPlayer;
            On.Terraria.NPC.AddBuff += NpcAddBuff;

            // ---- 文本 ----
            On.Terraria.Lang.GetBuffName += GetBuffName;
            On.Terraria.Main.GetBuffTooltip += GetBuffTooltip;

            // ---- 条上图标 ----
            On.GUIBuffs.ItemDraw += ItemDraw;

            // ---- 点图标取消 ----
            On.GUIBuffs.RemoveBuff_1 += RemoveBuff;

            // ---- 宿主槽位（Pylon 专有，见文件头 ①）----
            On.GUIBuffs.Draw += SwapDraw;
            On.GUIBuffs.UpdateBuffNavigation += SwapNavigation;

            _logger?.Info("[buff] BuffLoader 已挂（更新/加/文本/图标/取消）");
        }

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

        private static void PlayerBuffs(On.Terraria.Player.orig_UpdateBuffs orig, Player self, int buffType)
        {
            orig(self, buffType);
            var player = self;
            if (player == null) return;
            var types = player.buffType;
            var times = player.buffTime;
            if (types == null || times == null) return;
            UpdateModBuffs(types, times, player, null);
            UpdateGlobalBuffs(types, times, player, null);
        }

        private static void NpcBuffs(On.Terraria.NPC.orig_UpdateNPC_BuffSetFlags orig, NPC self, bool flag)
        {
            orig(self, flag);
            var npc = self;
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
        private static bool AppliedPlayer(On.Terraria.Player.orig_AddBuff_ActuallyTryToAddTheBuff orig, Player self, int type, int time)
        {
            var result = orig(self, type, time);
            if (!result) return result;
            var mod = Of(type);
            if (mod == null || !mod.Plan.Has("ApplyPlayer")) return result;
            try { mod.ApplyPlayer(self, time); }
            catch (Exception e) { Safe.Report($"{mod.Name}.ApplyPlayer", e); }
            return result;
        }

        // 返回 true = "已经有了"：游戏不再放一个 / 不再续时间；返回 false 才让原版继续。
        private static bool ReApplyPlayer(On.Terraria.Player.orig_AddBuff_TryUpdatingExistingBuffTime orig, Player self, int type, int time)
        {
            try
            {
                var player = self;
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
                    if (stop) return true;
                }

                var mod = Of(type);
                if (mod == null) return orig(self, type, time);   // 不是 mod buff：交回原版

                // index < 0 时 renew = true（照常加）；否则问 mod，false = 不续时间。
                var renew = index < 0 || !mod.Plan.Has("ReApplyPlayer") || mod.ReApplyPlayer(player, time, index);
                if (!renew) return true;
                return orig(self, type, time);
            }
            catch (Exception e)
            {
                Safe.Report("buff.ReApplyPlayer", e);
                return orig(self, type, time);
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

        private static void NpcAddBuff(On.Terraria.NPC.orig_AddBuff orig, NPC self, int type, int time, bool quiet)
        {
            // 先挂上：就算跳过原版也要摘（成对才不会被嵌套调用打乱）。
            var frame = new NpcAddBuffFrame { Npc = self, Type = type, Time = time, Outer = _npcAddBuff };
            _npcAddBuff = frame;
            var go = true;
            try
            {
                GrowImmune(self, BuffCount());   // Pylon 专有 ②

                var index = self.FindBuffIndex(type);

                // gbuff.ReApplyNPC：任一 Global 返回 true 就整个不续（连原版都不调）。
                if (index >= 0)
                {
                    var stop = false;
                    foreach (var g in GlobalBuffRegistry.For("ReApplyNPC"))
                    {
                        var slot = index;
                        try { if (g.ReApplyNPC(type, self, time, ref slot)) stop = true; }
                        catch (Exception e) { Safe.Report($"{g.Name}.ReApplyNPC", e); }
                    }
                    if (stop) go = false;
                }

                var mod = Of(type);
                if (go && mod != null && index >= 0)
                {
                    // ModBuff.ReApplyNPC 的 false 与 GlobalBuff 相反——false = 不续时间。
                    if (mod.Plan.Has("ReApplyNPC") && !mod.ReApplyNPC(self, time, index)) go = false;
                }
                frame.Apply = go && mod != null && mod.Plan.Has("ApplyNPC");
            }
            catch (Exception e)
            {
                Safe.Report("buff.ReApplyNPC", e);
            }

            if (go) orig(self, type, time, quiet);

            // （原版之后）被免疫/没加上就没有 ApplyNPC（回头查一次列表）。
            var end = _npcAddBuff;
            if (end == null) return;
            _npcAddBuff = end.Outer;
            if (!end.Apply) return;
            try
            {
                if (end.Npc == null || end.Npc.FindBuffIndex(end.Type) < 0) return;
                var mod = Of(end.Type);
                if (mod != null) mod.ApplyNPC(end.Npc, end.Time);
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

        private static string GetBuffName(On.Terraria.Lang.orig_GetBuffName orig, int id)
        {
            if (_texting) return orig(id);
            var type = RealTypeOf(id);          // 换槽期间问到的是宿主类型
            if (!HasText(type)) return orig(id);

            try
            {
                var name = orig(id);
                var tip = Lang.GetBuffDescription(id);
                ModifyText(type, ref name, ref tip);
                return name ?? "";
            }
            catch (Exception e) { Safe.Report("Lang.GetBuffName", e); return orig(id); }
        }

        private static string GetBuffTooltip(On.Terraria.Main.orig_GetBuffTooltip orig, Player player, int buffType)
        {
            if (!_texting)
            {
                var type = RealTypeOf(buffType);
                if (HasText(type))
                {
                    try
                    {
                        string name;
                        _texting = true;
                        try { name = Lang.GetBuffName(type); }
                        finally { _texting = false; }

                        var tip = orig(player, buffType);
                        ModifyText(type, ref name, ref tip);
                        return tip ?? "";
                    }
                    catch (Exception e) { Safe.Report("Main.GetBuffTooltip", e); }
                }
            }
            return orig(player, buffType);
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

        private static void ItemDraw(On.GUIBuffs.orig_ItemDraw orig, GUIBuffs self, ItemGrid_Layout gridLayout,
                                      int index, Vector2 position, float scale)
        {
            var globalsPre = GlobalBuffRegistry.For("PreDraw");
            var globalsPost = GlobalBuffRegistry.For("PostDraw");
            if (globalsPre.Count == 0 && globalsPost.Count == 0 && !HasModBuffs()) { orig(self, gridLayout, index, position, scale); return; }

            // 先挂上：跳不跳原版 postfix 都会跑，成对才能扛住嵌套调用。
            var frame = new DrawFrame { Outer = _draw };
            _draw = frame;
            var skip = false;
            try
            {
                var gui = self;
                if (gui == null || index < 0 || index >= gui.numActiveBuffs) { orig(self, gridLayout, index, position, scale); return; }
                var activeBuffs = gui.activeBuffs;
                if (activeBuffs == null || index >= activeBuffs.Length) { orig(self, gridLayout, index, position, scale); return; }

                var buffIndex = activeBuffs[index];   // 条上第 index 格 = 玩家列表里的第 buffIndex 个
                var player = Main.player[Main.myPlayer];
                var types = player != null ? player.buffType : null;
                if (types == null || buffIndex < 0 || buffIndex >= types.Length) { orig(self, gridLayout, index, position, scale); return; }

                var type = RealType(player, buffIndex);
                if (type <= 0) { orig(self, gridLayout, index, position, scale); return; }
                var mod = Of(type);
                var modPre = mod != null && mod.Plan.Has("PreDraw");
                var modPost = mod != null && mod.Plan.Has("PostDraw");
                if (!modPre && !modPost && globalsPre.Count == 0 && globalsPost.Count == 0) { orig(self, gridLayout, index, position, scale); return; }

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

                skip = !draw;
            }
            catch (Exception e) { Safe.Report("buff.PreDraw", e); }

            if (!skip) orig(self, gridLayout, index, position, scale);

            // PostDraw 不管 PreDraw 有没有说不画，都要跑。
            var end = _draw;
            if (end == null) return;
            _draw = end.Outer;
            if (!end.Dispatch) return;
            try
            {
                if (end.Mod != null && end.Mod.Plan.Has("PostDraw"))
                {
                    try { end.Mod.PostDraw(Main.spriteBatch, end.BuffIndex, end.Params); }
                    catch (Exception e) { Safe.Report($"{end.Mod.Name}.PostDraw", e); }
                }
                foreach (var g in GlobalBuffRegistry.For("PostDraw"))
                {
                    try { g.PostDraw(Main.spriteBatch, end.Type, end.BuffIndex, end.Params); }
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

        private static void RemoveBuff(On.GUIBuffs.orig_RemoveBuff_1 orig, GUIBuffs self, int buff)
        {
            // 先挂上：跳不跳原版后段都会跑，成对才不会被嵌套调用打乱。
            var frame = new RemoveFrame { Index = buff, Outer = _remove };
            _remove = frame;
            var skip = false;
            try
            {
                var player = Main.player[Main.myPlayer];
                frame.Player = player;
                var types = player != null ? player.buffType : null;
                var times = player != null ? player.buffTime : null;
                if (types == null || times == null) { orig(self, buff); return; }
                if (buff < 0 || buff >= types.Length || buff >= times.Length) { orig(self, buff); return; }

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
                    if (!allowed) skip = true;
                }

                var mod = Of(type);
                if (!skip && mod != null)
                {
                    var debuffs = Main.debuff;
                    var debuff = debuffs != null && type >= 0 && type < debuffs.Length && debuffs[type];
                    bool? can = null;
                    if (mod.Plan.Has("CanRemove"))
                    {
                        try { can = mod.CanRemove(player, frame.Time, buff, debuff); }
                        catch (Exception e) { Safe.Report($"{mod.Name}.CanRemove", e); }
                    }

                    if (can == false) skip = true;   // buff 留着
                    else if (can == true && debuff)
                    {
                        // 减益：直接扣掉，不让"再点一下确认"拦着。
                        player.DelBuff(buff);
                        skip = true;
                    }
                }
            }
            catch (Exception e) { Safe.Report("buff.CanRemove", e); }

            if (!skip) orig(self, buff);

            var end = _remove;
            if (end == null) return;
            _remove = end.Outer;
            try
            {
                var player = end.Player;
                if (player == null || end.Type == 0) return;
                var types = player.buffType;
                if (types == null || end.Index < 0 || end.Index >= types.Length) return;
                if (RealType(player, end.Index) == end.Type) return;   // 没被去掉

                var mod = Of(end.Type);
                if (mod == null || !mod.Plan.Has("OnRemove")) return;
                mod.OnRemove(player, end.Time, end.Index);
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

        private static void SwapDraw(On.GUIBuffs.orig_Draw orig, GUIBuffs self)
        {
            SwapBegin();
            try { orig(self); }
            finally { SwapEnd(); }
        }

        private static void SwapNavigation(On.GUIBuffs.orig_UpdateBuffNavigation orig, GUIBuffs self)
        {
            SwapBegin();
            try { orig(self); }
            finally { SwapEnd(); }
        }

        private static void SwapBegin()
        {
            if (_swap != null) { _swapDepth++; return; }   // 已在换过的那一层里（Draw 里又调导航之类）
            if (!HasModBuffs()) return;

            try
            {
                var player = Main.player[Main.myPlayer];
                var types = player != null ? player.buffType : null;
                var times = player != null ? player.buffTime : null;
                if (types == null || times == null) return;

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
        }

        private static void SwapEnd()
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