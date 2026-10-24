// 层：Mod 框架 / Loaders —— RarityLoader。
//
// 稀有度只是 item.rare 的数字，颜色由 ItemRarity._rarities 查；游戏把 >= 11 当紫色，
// 所以 mod 稀有度在三个地方接管：Initialize（颜色写进 _rarities）、GetPopupRarityColor（拾取色）、
// Prefix（升降稀有度被夹到 11 时用 GetPrefixedRarity 重算）。地上物品名暂不接管（要复刻整段绘制）。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader
{
    /// <summary>ModRarity 的占号、颜色表、拾取色、前缀升降稀有度。</summary>
    internal static class RarityLoader
    {
        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _installed;

        /// <summary>原版稀有度数量（ItemRarityID.Count，1.4.5 是 12）。</summary>
        internal const int VanillaCount = 12;

        internal static int Count => VanillaCount + ModContent.Count(ContentKind.Rarity);

        internal static ModRarity Of(int type) => ModContent.Find(ContentKind.Rarity, type) as ModRarity;

        internal static Color ColorOf(int type)
        {
            var mod = Of(type);
            if (mod == null) return Color.White;
            try { return mod.RarityColor; }
            catch (Exception e) { _logger?.Warn($"[rarity] {mod.Name}.RarityColor 异常：{e.Message}"); return Color.White; }
        }

        internal static void Install(Harmony harmony, ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            _harmony = harmony;
            _logger = logger;

            GameHooks.Install(harmony, logger, typeof(RarityLoader), Targets());
            _logger?.Info("[rarity] RarityLoader 已挂（颜色表/拾取色/前缀升降）");
        }

        private static GameHooks.Hook[] Targets() => new[]
        {
            new GameHooks.Hook { Type = typeof(ItemRarity), Method = "Initialize", Args = Type.EmptyTypes, Handler = nameof(InitializePostfix) },
            new GameHooks.Hook { Type = typeof(Item), Method = "GetPopupRarityColor", Args = new[] { typeof(int) }, Prefix = true, Handler = nameof(PopupColorPrefix) },
            new GameHooks.Hook { Type = typeof(Item), Method = "Prefix", Args = new[] { typeof(int), typeof(bool).MakeByRefType() },
                Prefix = true, Handler = nameof(PrefixPrefix) },
            new GameHooks.Hook { Type = typeof(Item), Method = "Prefix", Args = new[] { typeof(int), typeof(bool).MakeByRefType() },
                Handler = nameof(PrefixPostfix) },
        };

        internal static List<string> MissingTargets()
        {
            var missing = new List<string>();
            foreach (var target in Targets())
            {
                if (GameHooks.Find(target.Type, target.Method, target.Args) == null) missing.Add(target.Label);
                if (typeof(RarityLoader).GetMethod(target.Handler, BindingFlags.Static | BindingFlags.NonPublic) == null)
                    missing.Add("补丁方法 " + target.Handler);
            }
            return missing;
        }

        // ================= 颜色表 =================

        internal static void Setup(ModLogger logger)
        {
            var rarities = ModContent.GetContent<ModRarity>();
            if (rarities.Count == 0) return;

            try
            {
                foreach (var rarity in rarities)
                {
                    try { rarity.SetStaticDefaults(); }
                    catch (Exception e) { logger?.Warn($"{rarity.Name}.SetStaticDefaults 异常：{e.Message}"); }
                }

                AddToTable();
                logger?.Info($"[rarity] 稀有度安装：{rarities.Count} 个（类型 {VanillaCount}..{Count - 1}）");
            }
            catch (Exception e) { logger?.Warn($"[rarity] 安装失败：{e.Message}"); }
        }

        /// <summary>ItemRarity.Initialize 会重建 _rarities：每次跑完都把 mod 稀有度加回去。</summary>
        private static void AddToTable()
        {
            try
            {
                var table = ItemRarity._rarities;
                if (table == null) return;
                foreach (var rarity in ModContent.GetContent<ModRarity>())
                    table[rarity.Type] = ColorOf(rarity.Type);
            }
            catch (Exception e) { _logger?.Warn($"[rarity] 写颜色表失败：{e.Message}"); }
        }

        private static void InitializePostfix()
        {
            AddToTable();
            foreach (var rarity in ModContent.GetContent<ModRarity>())
            {
                try { rarity.SetStaticDefaults(); }
                catch { /* 已跑过；重建时再跑一次失败不影响颜色 */ }
            }
        }

        private static bool PopupColorPrefix(int itemRarity, ref Color __result)
        {
            var mod = Of(itemRarity);
            if (mod == null) return true;
            __result = ColorOf(itemRarity);
            return false;
        }

        // ================= 前缀升降稀有度 =================

        private sealed class Frame
        {
            public long Address;
            public int BaseRare;
            public bool Active;
        }

        private static Frame _frame;

        private static bool PrefixPrefix(Item __instance, int prefixWeWant, ref bool rolledPrefixIsTopTier)
        {
            try
            {
                var baseRare = __instance.rare;
                // 只有 mod 稀有度的物品才接管（原版稀有度不碰）。
                _frame = Of(baseRare) != null
                    ? new Frame { Address = Address(__instance), BaseRare = baseRare, Active = true }
                    : null;
                return true;
            }
            catch { return true; }
        }

        private static void PrefixPostfix(Item __instance, int prefixWeWant, ref bool rolledPrefixIsTopTier, bool __result)
        {
            var frame = _frame;
            _frame = null;
            if (frame == null || !frame.Active || !__result || prefixWeWant == -3) return;
            if (frame.Address != Address(__instance)) return;

            try
            {
                // 前缀把稀有度夹到了 11（紫色）：用 valueMult 换算成 -2..+2 的档位，交给 mod 决定。
                var value = 1f;
                if (__instance.prefix != 0)
                {
                    try
                    {
                        __instance.TryGetPrefixStatMultipliersForItem(__instance.prefix,
                            out _, out _, out _, out _, out _, out _, out _, out _, out _, out value);
                    }
                    catch { value = 1f; }
                }

                var offset = value >= 1.2f ? 2 : value >= 1.05f ? 1 : value <= 0.8f ? -2 : value <= 0.95f ? -1 : 0;
                var mod = Of(frame.BaseRare);
                var rare = mod != null ? mod.GetPrefixedRarity(offset, value) : frame.BaseRare;
                if (rare > -11) rare = Math.Min(Math.Max(rare, -1), Count - 1);
                __instance.rare = rare;
            }
            catch (Exception e) { _logger?.Warn($"[rarity] 前缀稀有度异常：{e.Message}"); }
        }

        private static long Address(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj) =>
            obj == null ? 0 : (long)obj.Pointer;
    }
}
