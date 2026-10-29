// 层：Mod 框架 / Loaders —— RecipeLoader。
//
// 配方是 Main.recipe 一张表 + 若干派生表（材料/可制作/快速查找/微光变换/样例），AddRecipes 之后必须重建，
// 否则新配方在菜单里不出现或材料判定出错。三个入口：
//   · FindRecipes：摘掉条件不满足/被 GlobalRecipe 拒绝的配方，超 maxRecipes 的部分自己补（Limit Fix）；
//   · GetIngredientCraftingDiscount：每件材料实际扣多少（回调可改）；
//   · CraftItem_GrantItem：做出来之前跑 OnCraft 回调。
using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader
{
    /// <summary>ModRecipe 的回调/条件挂接 + 配方派生表重建。</summary>
    internal static class RecipeLoader
    {
        // MAX_RECIPES：README 说 1.4.5.8.6 是 3700；C# 侧是编译期常量（dump: maxRecipes = 3700），同版本下等价。
        internal const int MaxRecipes = 3700;

        private static ModLogger _logger;
        private static bool _installed;

        private sealed class Extra
        {
            public List<Condition> Conditions;
            public List<ModRecipe.ConsumeIngredientCallback> Consume;
            public List<ModRecipe.OnCraftCallback> Craft;
        }

        private static readonly Dictionary<int, Extra> ByIndex = new();      // Main.recipe 里的位置 → 条件与回调
        private static readonly Dictionary<long, Extra> ByAddress = new();   // 同一个 Extra，按 Recipe 对象地址
        private static readonly Dictionary<string, RecipeGroup> Groups = new(StringComparer.Ordinal);
        private static readonly HashSet<int> Stations = new();               // 拿 mod 方块当工作台的配方

        private static int _added;
        private static int _beyond;
        private static bool _filtering;

        /// <summary>早阶段：装钩子（只装一次）。</summary>
        internal static void Install(ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            _logger = logger;

            On.Terraria.Recipe.FindRecipes += FindRecipes;
            On.Terraria.Recipe.GetIngredientCraftingDiscount += DiscountPostfix;
            On.Terraria.Main.CraftItem_GrantItem += GrantItemPrefix;
            // 超上限那部分：向导弹窗与物品变体表也要跟着看全
            On.GUICraftGuidePopup.FindRecipes += GuidePostfix;
            On.Terraria.Recipe.UpdateItemVariants += VariantsPostfix;

            _logger?.Info("[recipe] RecipeLoader 已挂（条件过滤 / 消耗回调 / OnCraft / 超上限）");
        }

        // ===================== 注册（ModRecipe.Register 调） =====================

        // Added
        internal static void Added(int index, int station, ModRecipe recipe)
        {
            _added++;
            if (ModContent.Find(ContentKind.Tile, station) != null) Stations.Add(station);

            if (index >= MaxRecipes)
            {
                _beyond++;
                Safe.Once("recipe.beyondMax", $"[recipe] 配方数超过 {MaxRecipes}（第 {_beyond} 条在 {index}），" +
                                              "做菜菜单/向导走超上限分支");
            }
            if (recipe == null) return;

            // #Extras：条件和回调都按位置与对象记两份。
            var conditions = recipe.Conditions;
            var consume = recipe.ConsumeCallbacks;
            var craft = recipe.CraftCallbacks;
            if (conditions.Count == 0 && consume.Count == 0 && craft.Count == 0) return;

            var extra = new Extra { Conditions = conditions, Consume = consume, Craft = craft };
            ByIndex[index] = extra;
            try
            {
                var address = Address(recipe.Recipe);
                if (address != 0) ByAddress[address] = extra;
            }
            catch (Exception e) { _logger?.Warn($"[recipe] 记录配方失败：{e.Message}"); }

            if (conditions.Count > 0) _filtering = true;
        }

        // HookGlobal
        internal static void WantFilter() => _filtering = true;

        // Group / SetGroup
        internal static RecipeGroup Group(string name) =>
            name != null && Groups.TryGetValue(name, out var group) ? group : null;

        internal static void SetGroup(string name, RecipeGroup group)
        {
            if (name != null && group != null) Groups[name] = group;
        }

        private static Extra Of(int index) => ByIndex.TryGetValue(index, out var extra) ? extra : null;

        private static Extra Of(Recipe recipe)
        {
            if (recipe == null) return null;
            try
            {
                var address = Address(recipe);
                return address != 0 && ByAddress.TryGetValue(address, out var extra) ? extra : null;
            }
            catch { return null; }
        }

        private static long Address(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj) =>
            obj == null ? 0 : (long)obj.Pointer;

        private static bool HasGlobal(string method)
        {
            foreach (var global in ModContent.GetContent<GlobalRecipe>())
                if (global.Plan != null && global.Plan.Has(method)) return true;
            return false;
        }

        // ===================== 制作菜单 =====================

        private static int _oldRecipe;

        private static void FindRecipes(On.Terraria.Recipe.orig_FindRecipes orig, bool canDelayCheck)
        {
            // ===== prefix：菜单前处理 =====
            _oldRecipe = 0;
            try
            {
                var beyond = !canDelayCheck && Recipe.numRecipes > MaxRecipes;
                if (beyond || _filtering)
                {
                    if (beyond) GrowAvailable(Recipe.numRecipes);

                    var available = Main.availableRecipe;
                    var focus = Main.focusRecipe;
                    if (available != null && focus >= 0 && focus < available.Length) _oldRecipe = available[focus];
                }
            }
            catch (Exception e) { _logger?.Warn($"[recipe] 制作菜单前处理失败：{e.Message}"); }

            orig(canDelayCheck);   // 始终让游戏自己跑完 FindRecipes

            // ===== postfix：菜单后处理 =====
            try
            {
                var beyond = !canDelayCheck && Recipe.numRecipes > MaxRecipes;
                if (beyond) Safe.Run("receitas alem do limite", () => FindBeyondMax(_oldRecipe));
                if (_filtering) Safe.Run("condicoes das receitas", () => Filter(_oldRecipe));
            }
            catch (Exception e) { _logger?.Warn($"[recipe] 制作菜单后处理失败：{e.Message}"); }
        }

        // #Filter：留在 availableRecipe 里的顺序不变，只是压实。
        private static void Filter(int oldRecipe)
        {
            var available = Main.availableRecipe;
            if (available == null) return;

            var count = Math.Min(Main.numAvailableRecipes, available.Length);
            var kept = new List<int>(count);
            for (var k = 0; k < count; k++)
            {
                var index = available[k];
                if (!Available(index)) continue;
                kept.Add(index);
            }
            if (kept.Count == count) return;

            // 托管数组一次赋值（interop 逐元素写 struct 数组会炸，见 migration-map §6）。
            var grown = new int[available.Length];
            for (var i = 0; i < kept.Count; i++) grown[i] = kept[i];
            for (var i = kept.Count; i < available.Length; i++) grown[i] = available[i];
            Main.availableRecipe = grown;
            Main.numAvailableRecipes = kept.Count;
            Safe.Run("recipe.TryRefocusingRecipe", () => Terraria.Recipe.TryRefocusingRecipe(oldRecipe));
        }

        // #Available：条件抛错算不满足；GlobalRecipe 一票否决。
        private static bool Available(int index)
        {
            var recipes = Main.recipe;
            var recipe = recipes != null && index >= 0 && index < recipes.Length ? recipes[index] : null;
            if (recipe == null) return false;

            var extra = Of(index);
            if (extra != null)
            {
                foreach (var condition in extra.Conditions)
                {
                    if (!Safe.Run("Condition " + condition.DescriptionKey, condition.IsMet, false)) return false;
                }
            }
            foreach (var global in ModContent.GetContent<GlobalRecipe>())
            {
                if (global.Plan == null || !global.Plan.Has("RecipeAvailable")) continue;
                if (!Safe.Run(global.Name + ".RecipeAvailable", () => global.RecipeAvailable(recipe), false)) return false;
            }
            return true;
        }

        // ===================== 材料消耗 =====================

        // 折扣是"实际扣多少"的入口（炼金桌折扣叠加）；回调能改，但不能扣得比玩家有的多（游戏只按原始需求检查过）。
        private static int DiscountPostfix(On.Terraria.Recipe.orig_GetIngredientCraftingDiscount orig,
                                           Recipe self, Player player, Recipe.RequiredItemEntry req)
        {
            var result = orig(self, player, req);
            try
            {
                var extra = Of(self);
                var globals = HasGlobal("ConsumeItem");
                if ((extra == null || extra.Consume.Count == 0) && !globals) return result;

                var stack = req.stack;
                var type = req.IsRecipeGroup ? req.RecipeGroup.GetPlaceholderItemType() : req.itemIdOrRecipeGroup;
                var amount = stack - result;

                if (extra != null)
                {
                    foreach (var callback in extra.Consume)
                        Safe.Run("ConsumeIngredientCallback",
                            () => callback(self, type, ref amount, false));
                }
                if (globals)
                {
                    foreach (var global in ModContent.GetContent<GlobalRecipe>())
                    {
                        if (global.Plan == null || !global.Plan.Has("ConsumeItem")) continue;
                        Safe.Run(global.Name + ".ConsumeItem", () => global.ConsumeItem(self, type, ref amount));
                    }
                }

                var owned = Safe.Run("GetAvailableItemCount", () => Terraria.Recipe.GetAvailableItemCount(req), 0);
                var consumed = Math.Max(0, Math.Min(amount, Math.Max(stack, owned)));
                result = stack - consumed;
            }
            catch (Exception e) { _logger?.Warn($"[recipe] 消耗回调失败：{e.Message}"); }
            return result;
        }

        // ===================== 做出来之前 =====================

        private static void GrantItemPrefix(On.Terraria.Main.orig_CraftItem_GrantItem orig,
                                            Recipe recipe, Item result, bool quickCraft)
        {
            try
            {
                var extra = Of(recipe);
                if (extra != null)
                {
                    foreach (var callback in extra.Craft)
                        Safe.Run("OnCraftCallback", () => callback(recipe, result, null, null));
                }
                foreach (var global in ModContent.GetContent<GlobalRecipe>())
                {
                    if (global.Plan == null || !global.Plan.Has("OnCraft")) continue;
                    Safe.Run(global.Name + ".OnCraft", () => global.OnCraft(result, recipe));
                }
            }
            catch (Exception e) { _logger?.Warn($"[recipe] OnCraft 失败：{e.Message}"); }
            orig(recipe, result, quickCraft);
        }

        // ===================== 超出 Recipe.maxRecipes 的那部分 =====================

        // availableRecipe / availableRecipeY 是按 maxRecipes 建的：先长表。
        private static void GrowAvailable(int total)
        {
            var available = Main.availableRecipe;
            if (available != null && available.Length < total)
            {
                var grown = new int[total];
                for (var i = 0; i < available.Length; i++) grown[i] = available[i];
                Main.availableRecipe = grown;
            }
            var positions = Main.availableRecipeY;
            if (positions != null && positions.Length < total)
            {
                var grown = new float[total];
                for (var i = 0; i < positions.Length; i++) grown[i] = positions[i];
                Main.availableRecipeY = grown;
            }
        }

        // #FindBeyondMax：游戏只走到 maxRecipes，剩下的自己补。
        private static void FindBeyondMax(int oldRecipe)
        {
            var total = Terraria.Recipe.numRecipes;
            if (total <= MaxRecipes) return;

            var player = Main.LocalPlayer;
            Terraria.Recipe.CollectItemsToCraftWithFrom(player);

            var recipes = Main.recipe;
            for (var i = MaxRecipes; i < total; i++)
            {
                var recipe = recipes != null && i < recipes.Length ? recipes[i] : null;
                if (recipe == null || recipe.createItem == null || recipe.createItem.type == 0) break;

                if (Terraria.Recipe.PlayerMeetsTileRequirements(player, recipe) &&
                    Terraria.Recipe.PlayerMeetsEnvironmentConditions(player, recipe) &&
                    Terraria.Recipe.CollectedEnoughItemsToCraft(recipe))
                {
                    Terraria.Recipe.AddToAvailableRecipes(i);
                }
            }
            Terraria.Recipe.TryRefocusingRecipe(oldRecipe);
        }

        // #FindGuideBeyondMax：向导弹窗按"材料里有没有这件物品"过滤。
        private static void GuidePostfix(On.GUICraftGuidePopup.orig_FindRecipes orig, GUICraftGuidePopup self)
        {
            orig(self);
            try
            {
                var total = Terraria.Recipe.numRecipes;
                if (total <= MaxRecipes) return;

                var item = Main.guideItem;
                if (item == null || item.type < 1 || item.stack < 1) return;

                var found = self.availableGuideRecipe;
                if (found == null) return;
                if (found.Length < total)
                {
                    var grown = new int[total];
                    for (var i = 0; i < found.Length; i++) grown[i] = found[i];
                    self.availableGuideRecipe = grown;
                    found = grown;
                }

                var recipes = Main.recipe;
                var count = self.numAvailableGuideRecipes;
                for (var i = MaxRecipes; i < total; i++)
                {
                    var recipe = recipes != null && i < recipes.Length ? recipes[i] : null;
                    if (recipe == null || recipe.createItem == null || recipe.createItem.type == 0) break;
                    if (!UsesItem(recipe, item.type)) continue;
                    if (count >= found.Length) break;
                    found[count++] = i;
                }
                self.numAvailableGuideRecipes = count;
            }
            catch (Exception e) { _logger?.Warn($"[recipe] 向导超上限失败：{e.Message}"); }
        }

        // #UsesItem：查 requiredItemQuickLookup（15 格，0 = 到头）。
        private static bool UsesItem(Recipe recipe, int type)
        {
            var lookup = recipe.requiredItemQuickLookup;
            if (lookup == null) return false;
            for (var i = 0; i < ModRecipe.MaxIngredients; i++)
            {
                if (i >= lookup.Length) return false;
                var entry = lookup[i];
                if (entry.itemIdOrRecipeGroup == 0) return false;
                if (entry.Matches(type)) return true;
            }
            return false;
        }

        // #RefreshBeyondMax：物品变体表重算时把超上限那批也刷一遍。
        private static void VariantsPostfix(On.Terraria.Recipe.orig_UpdateItemVariants orig)
        {
            orig();
            try
            {
                var total = Terraria.Recipe.numRecipes;
                if (total <= MaxRecipes) return;

                var recipes = Main.recipe;
                for (var i = MaxRecipes; i < total; i++)
                {
                    var recipe = recipes != null && i < recipes.Length ? recipes[i] : null;
                    if (recipe == null) break;

                    recipe.createItem?.Refresh(true);
                    var ingredients = recipe.requiredItem;
                    if (ingredients == null) continue;
                    for (var j = 0; j < ingredients.Length; j++)
                    {
                        var ingredient = ingredients[j];
                        if (ingredient == null || ingredient.type == 0) break;
                        ingredient.Refresh(true);
                    }
                }
            }
            catch (Exception e) { _logger?.Warn($"[recipe] 变体刷新失败：{e.Message}"); }
        }

        // ===================== 收尾：重建派生表 =====================

        internal static void Finish(ModLogger logger)
        {
            if (_added == 0) return;
            _logger = logger ?? _logger;

            // 派生表重建，顺序固定。⚠ 两条方法在 dump 里是 private static 但 interop 生成为 public，
            // 反射找 NonPublic 找不到（真机：静默跳过、表从不重建），直接调用即可。
            Step("CreateRequiredItemQuickLookups", () => Terraria.Recipe.CreateRequiredItemQuickLookups());
            Step("UpdateMaterialFieldForAllRecipes", () => Terraria.Recipe.UpdateMaterialFieldForAllRecipes());
            Step("UpdateWhichItemsAreMaterials", Terraria.Recipe.UpdateWhichItemsAreMaterials);
            Step("UpdateWhichItemsAreCrafted", Terraria.Recipe.UpdateWhichItemsAreCrafted);
            Step("ShimmerTransforms.UpdateRecipeSets", Terraria.GameContent.ShimmerTransforms.UpdateRecipeSets);
            Step("ContentSamples.FixItemsAfterRecipesAreAdded", Terraria.ID.ContentSamples.FixItemsAfterRecipesAreAdded);
            Step("SetCraftingStations", SetCraftingStations);

            _logger?.Info($"[recipe] 配方收尾：mod 配方 {_added} 条（回调/条件 {ByIndex.Count} 条），" +
                          $"总配方 {Terraria.Recipe.numRecipes}");
            if (_beyond > 0)
                _logger?.Info($"[recipe] 其中 {_beyond} 条超过位置 {MaxRecipes}，由制作菜单/向导的超上限分支覆盖");
        }

        // SetCraftingStations：拿 mod 方块当工作台的配方要配上"放它的物品"，否则菜单里没有图标和名字。
        private static void SetCraftingStations()
        {
            if (Stations.Count == 0) return;

            var table = TileID.Sets.CraftingStationItemId;
            if (table == null) { _logger?.Warn("[recipe] 没有 TileID.Sets.CraftingStationItemId，mod 工作台没有图标"); return; }

            var top = 0;
            foreach (var tile in Stations) top = Math.Max(top, tile + 1);
            if (table.Length < top)
            {
                var grown = new int[top];
                for (var i = 0; i < table.Length; i++) grown[i] = table[i];
                TileID.Sets.CraftingStationItemId = grown;
                table = grown;
            }

            foreach (var tile in Stations)
            {
                if (tile < 0 || tile >= table.Length) continue;
                if (table[tile] != 0) continue;   // mod 自己已经登记过
                var mod = ModContent.Find(ContentKind.Tile, tile) as ModTile;
                var item = mod != null ? mod.ItemDrop : 0;
                if (item > 0) table[tile] = item;
                else _logger?.Info($"[recipe] {mod?.Name ?? tile.ToString()} 被配方当工作台，但没有物品放置它（菜单里没有图标）");
            }
        }

        private static void Step(string name, Action action) => Safe.Run("receitas: " + name, action);


    }
}
