using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Localization;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.Localization;

namespace Pylon.ModLoader.Pipelines;
/// <summary>群落图鉴美术：每个群落建一个图鉴元素，三个图鉴入口把图标/背景/出生页换成作者贴图。</summary>
internal static class BiomeBestiaryHooks
{
    private const int ShadowFrame = 64;
    private const int IconWidth = 30, IconHeight = 30;
    private const int BackgroundWidth = 115, BackgroundHeight = 65;

    private sealed class Art
    {
        public Texture2D? Icon;
        public Texture2D? Background;
        public string Key = "";
    }

    private static readonly Dictionary<nint, Art> Arts = [];
    private static readonly List<ModBiome> Used = [];
    private static readonly Dictionary<nint, Asset<Texture2D>> BackgroundAssets = [];
    private static bool _installed;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static SpawnConditionBestiaryInfoElement? Create(ModBiome biome)
    {
        try
        {
            var key = "BiomeName." + biome.Name;
            var text = new LocalizedText(key, biome.Name);
            LocalizationKeys.Register(key, text);

            var background = biome.BackgroundColor is { } color
                ? new Il2CppSystem.Nullable<Color>(color)
                : new Il2CppSystem.Nullable<Color>();
            var element = new SpawnConditionBestiaryInfoElement(key, ShadowFrame, null!, background);

            Arts[element.Pointer] = new Art
            {
                Icon = Load(biome, biome.BestiaryIcon, IconWidth, IconHeight),
                Background = Load(biome, biome.BackgroundPath, BackgroundWidth, BackgroundHeight),
                Key = key,
            };
            Install();
            return element;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[biome] {biome.Name} 图鉴元素建失败：{Safe.Describe(e)}");
            return null;
        }
    }

    internal static void AddTo(BestiaryEntry entry, ModNpc npc)
    {
        if (npc.SpawnModBiomes.Length == 0) return;
        foreach (var type in npc.SpawnModBiomes)
        {
            if (BiomeTable.At(type) is not { } biome || biome.BestiaryElement is not { } element) continue;
            entry.Info.Add(Wrap(element));
            if (!Used.Contains(biome)) Used.Add(biome);
        }
    }

    internal static void RegisterFilters(BestiaryDatabase database)
    {
        foreach (var biome in Used)
        {
            if (biome.BestiaryElement is not { } element) continue;
            try
            {
                var filter = new Terraria.GameContent.Bestiary.Filters.ByInfoElement(Wrap(element));
                database.Register(new Terraria.GameContent.Bestiary.IBestiaryEntryFilter(filter.Pointer));
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[biome] {biome.Name} 图鉴过滤器注册失败：{Safe.Describe(e)}");
            }
        }
        Used.Clear();
    }

    private static Terraria.GameContent.Bestiary.IBestiaryInfoElement Wrap(
        Terraria.GameContent.Bestiary.SpawnConditionBestiaryInfoElement element) =>
        new(element.Pointer);

    private static Texture2D? Load(ModBiome biome, string? path, int width, int height)
    {
        if (string.IsNullOrEmpty(path) || biome.Mod is null || !ModContent.HasAsset(biome.Mod, path)) return null;
        try
        {
            var asset = biome.Mod.LoadAsset(path);
            var texture = asset.Value;
            if (texture.Width == width && texture.Height == height) return texture;
            biome.Mod.Logger.Info($"{path} 需要为 {width}x{height} 像素");
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[biome] {biome.Name} 图鉴美术（{path}）：{Safe.Describe(e)}");
        }
        return null;
    }

    private static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.GameContent.Bestiary.FilterProviderInfoElement.GetDisplay += GetDisplay;
        On.Terraria.GameContent.Bestiary.SpawnConditionBestiaryInfoElement.GetBackgroundImage += GetBackgroundImage;
        On.GUIBestiary.SpawnDraw += SpawnDraw;
    }

    private static void GetDisplay(On.Terraria.GameContent.Bestiary.FilterProviderInfoElement.orig_GetDisplay orig,
        FilterProviderInfoElement self, out Texture2D texture, out Rectangle frame)
    {
        orig(self, out texture, out frame);
        if (!Arts.TryGetValue(self.Pointer, out var art) || art.Icon is not Texture2D icon) return;
        texture = icon;
        frame = new Rectangle(0, 0, IconWidth, IconHeight);
    }

    private static Asset<Texture2D> GetBackgroundImage(
        On.Terraria.GameContent.Bestiary.SpawnConditionBestiaryInfoElement.orig_GetBackgroundImage orig,
        SpawnConditionBestiaryInfoElement self)
    {
        if (Arts.TryGetValue(self.Pointer, out var art) && art.Background is { } background)
        {
            if (!BackgroundAssets.TryGetValue(self.Pointer, out var asset))
            {
                asset = new Asset<Texture2D>("mod-biome-best-" + self.Pointer);
                asset.SubmitLoadedContent(background, null);
                BackgroundAssets[self.Pointer] = asset;
            }
            return asset;
        }
        return orig(self);
    }

    private static void SpawnDraw(On.GUIBestiary.orig_SpawnDraw orig, GUIBestiary self,
        ItemGrid_Layout gridLayout, int index, Vector2 position, float scale)
    {
        Art? art = null;
        try
        {
            var providers = self?.FilterProviders;
            if (providers is not null && index >= 0 && index < providers.Count &&
                Arts.TryGetValue(providers[index].Pointer, out var found) && found.Icon is not null)
                art = found;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[biome] 图鉴出生页取条目异常：{Safe.Describe(e)}");
        }

        if (art?.Icon is not Texture2D icon)
        {
            orig(self!, gridLayout, index, position, scale);
            return;
        }

        var layout = Bestiary_Layout.Instance;
        var backing = layout.EntrySpawnBacking;
        var size = backing.Size;
        ControlAnchor.SetGridItemRegion(new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y));
        GUIPanel.DrawAtPosition(backing, position, size, false);
        Main.spriteBatch.Draw(icon, GUIPanel.Region(layout.EntrySpawnIcon), Color.White);
        GUIString.Draw(layout.EntrySpawnType, Language.GetTextValue(art.Key));
    }
}
