// 层：Mod 框架 / Loaders —— RarityLoader。
//
// 稀有度只是 item.rare 的数字，颜色由 ItemRarity._rarities 查；游戏把 >= 11 当紫色，
// 所以 mod 稀有度在三个地方接管：Initialize（颜色写进 _rarities）、GetPopupRarityColor（拾取色）、
// Prefix（升降稀有度被夹到 11 时用 GetPrefixedRarity 重算）。地上物品名暂不接管（要复刻整段绘制）。
using System;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader
{
    /// <summary>ModRarity 的占号、颜色表、拾取色、前缀升降稀有度。</summary>
    internal sealed class RarityLoader : ILoaderModule
    {
        public RarityLoader(ModLogger logger) => _logger = logger;

        public string Name => "rarity";

        public int Order => 430;

        private static ModLogger _logger;

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

        public bool Install()
        {
            On.Terraria.GameContent.UI.ItemRarity.Initialize += InitializePostfix;
            On.Terraria.Item.GetPopupRarityColor_1 += PopupColorPrefix;
            On.Terraria.Item.Prefix_1 += PrefixRarity;   // 旧的前后置合成一个 handler

            _logger?.Info("[rarity] RarityLoader 已挂（颜色表/拾取色/前缀升降）");
            return true;
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

        private static void InitializePostfix(On.Terraria.GameContent.UI.ItemRarity.orig_Initialize orig)
        {
            orig();
            AddToTable();
            foreach (var rarity in ModContent.GetContent<ModRarity>())
            {
                try { rarity.SetStaticDefaults(); }
                catch { /* 已跑过；重建时再跑一次失败不影响颜色 */ }
            }
        }

        private static Color PopupColorPrefix(On.Terraria.Item.orig_GetPopupRarityColor_1 orig, int itemRarity)
        {
            var mod = Of(itemRarity);
            if (mod == null) return orig(itemRarity);
            return ColorOf(itemRarity);
        }

        // ================= 前缀升降稀有度 =================

        private sealed class Frame
        {
            public long Address;
            public int BaseRare;
            public bool Active;
        }

        private static Frame _frame;

        // 旧实现是 prefix＋postfix（prefix 记 baseRare，postfix 按结果重算稀有度）；On 引擎里合成一个：先记帧、再原版、最后改。
        private static bool PrefixRarity(On.Terraria.Item.orig_Prefix_1 orig, Item self, int prefixWeWant, ref bool rolledPrefixIsTopTier)
        {
            try
            {
                var baseRare = self.rare;
                // 只有 mod 稀有度的物品才接管（原版稀有度不碰）。
                _frame = Of(baseRare) != null
                    ? new Frame { Address = Address(self), BaseRare = baseRare, Active = true }
                    : null;
            }
            catch { /* 记帧失败照跑原版 */ }

            var result = orig(self, prefixWeWant, ref rolledPrefixIsTopTier);

            var frame = _frame;
            _frame = null;
            if (frame == null || !frame.Active || !result || prefixWeWant == -3) return result;
            if (frame.Address != Address(self)) return result;

            try
            {
                // 前缀把稀有度夹到了 11（紫色）：用 valueMult 换算成 -2..+2 的档位，交给 mod 决定。
                var value = 1f;
                if (self.prefix != 0)
                {
                    try
                    {
                        self.TryGetPrefixStatMultipliersForItem(self.prefix,
                            out _, out _, out _, out _, out _, out _, out _, out _, out _, out value);
                    }
                    catch { value = 1f; }
                }

                var offset = value >= 1.2f ? 2 : value >= 1.05f ? 1 : value <= 0.8f ? -2 : value <= 0.95f ? -1 : 0;
                var mod = Of(frame.BaseRare);
                var rare = mod != null ? mod.GetPrefixedRarity(offset, value) : frame.BaseRare;
                if (rare > -11) rare = Math.Min(Math.Max(rare, -1), Count - 1);
                self.rare = rare;
            }
            catch (Exception e) { _logger?.Warn($"[rarity] 前缀稀有度异常：{e.Message}"); }
            return result;
        }

        private static long Address(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj) =>
            obj == null ? 0 : (long)obj.Pointer;
    }
}
