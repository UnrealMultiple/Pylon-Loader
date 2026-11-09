using System;
using System.Collections.Generic;
using System.IO;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 背景贴图：每个 <c>Backgrounds/*.png</c> 占一个游戏背景槽（原版槽位之后），
/// 并把 <c>TextureAssets.Background</c> / <c>Main.backgroundWidth/Height</c> 扩到总数——
/// </summary>
public static class BackgroundTextureLoader
{
    private sealed class Entry
    {
        public string Key = "";
        public Mod Mod = null!;
        public string Relative = "";
        public string File = "";
    }

    private static readonly Dictionary<string, int> Backgrounds = new(StringComparer.Ordinal);
    private static readonly List<Entry> Pending = [];
    private static int _vanillaCount = -1;

    public static int VanillaCount => _vanillaCount >= 0 ? _vanillaCount : _vanillaCount = Main.maxBackgrounds;

    public static int TotalCount => VanillaCount + Pending.Count;

    public static int GetBackgroundSlot(Mod mod, string texture)
    {
        if (!TryGetBackgroundSlot(mod, texture, out var slot))
            throw new KeyNotFoundException($"BackgroundTextureLoader: no background '{texture}'");

        return slot;
    }

    public static bool TryGetBackgroundSlot(Mod mod, string texture, out int slot) =>
        TryGetBackgroundSlot($"{mod.Name}/{texture}", out slot);

    public static bool TryGetBackgroundSlot(string texture, out int slot)
    {
        lock (Backgrounds)
        {
            if (Backgrounds.TryGetValue(Trim(texture), out var index))
            {
                slot = VanillaCount + index;
                return true;
            }
        }

        slot = 0;
        return false;
    }

    public static void AddBackgroundTexture(Mod mod, string texture)
    {
        if (mod is null) throw new ArgumentNullException(nameof(mod));
        if (texture is null) throw new ArgumentNullException(nameof(texture));

        var relative = Trim(texture);
        if (relative.StartsWith(mod.Name + "/", StringComparison.Ordinal)) relative = relative[(mod.Name.Length + 1)..];

        var file = Path.Combine(mod.Directory, relative + ".png");
        if (!File.Exists(file))
            throw new FileNotFoundException($"{mod.Name}: background '{texture}' not found", relative + ".png");

        var key = mod.Name + "/" + relative;
        lock (Backgrounds)
        {
            if (Backgrounds.ContainsKey(key)) return;
            Backgrounds[key] = Pending.Count;
            Pending.Add(new Entry { Key = key, Mod = mod, Relative = relative, File = file });
        }
    }

    internal static void AutoloadBackgrounds(Mod mod)
    {
        try
        {
            if (!Directory.Exists(mod.Directory)) return;

            foreach (var file in Directory.EnumerateFiles(mod.Directory, "*.png", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (!normalized.Contains("/backgrounds/", StringComparison.OrdinalIgnoreCase)) continue;

                var relative = Path.GetRelativePath(mod.Directory, file).Replace('\\', '/');
                relative = relative[..^4];
                try { AddBackgroundTexture(mod, relative); }
                catch (Exception e) { LoaderHost.Current.Log.Warn($"[background] {mod.Name}/{relative} 跳过：{e.Message}"); }
            }
        }
        catch (Exception e)
        {
            LoaderHost.Current.Log.Warn($"[background] {mod.Name} 的背景目录扫描失败：{e.Message}");
        }
    }

    /// <summary>扩 <c>TextureAssets.Background</c> 与宽高表，并上传待装贴图；Build 期调用。</summary>
    internal static void ResizeArrays()
    {
        if (Pending.Count == 0) return;

        var total = TotalCount;
        var first = VanillaCount;

        // 仓库统一的扩容框架（Tables.ArrayGrow）：引用表走写屏障，值类型表按元素大小拷。
        var textures = TextureAssets.Background;
        if (textures is null || textures.Length < total)
        {
            if (Tables.ArrayGrow.Grow(ref textures, total)) TextureAssets.Background = textures;
        }

        var widths = Main.backgroundWidth;
        if (widths is null || widths.Length < total)
        {
            if (Tables.ArrayGrow.Grow(ref widths, total)) Main.backgroundWidth = widths;
        }

        var heights = Main.backgroundHeight;
        if (heights is null || heights.Length < total)
        {
            if (Tables.ArrayGrow.Grow(ref heights, total)) Main.backgroundHeight = heights;
        }

        for (var index = 0; index < Pending.Count; index++)
        {
            var entry = Pending[index];
            var slot = first + index;
            try
            {
                var asset = entry.Mod.LoadAsset(entry.Relative + ".png");

                var bgTextures = TextureAssets.Background;
                var bgWidths = Main.backgroundWidth;
                var bgHeights = Main.backgroundHeight;
                if (bgTextures is null || bgWidths is null || bgHeights is null ||
                    slot >= bgTextures.Length || slot >= bgWidths.Length || slot >= bgHeights.Length)
                {
                    LoaderHost.Current.Log.Error($"[background] {entry.Key} 扩表后槽位 {slot} 仍越界");
                    continue;
                }

                bgTextures[slot] = asset;

                Textures.PylonTexture.Size(asset.Value, out var width, out var height);
                bgWidths[slot] = width;
                bgHeights[slot] = height;
            }
            catch (Exception e)
            {
                LoaderHost.Current.Log.Error($"[background] {entry.Key} 贴图加载失败：{e.Message}");
            }
        }

        LoaderHost.Current.Log.Info($"[background] mod 背景贴图 {Pending.Count} 张（槽位 {first}..{total - 1}）");
    }

    /// <summary>取槽位贴图；槽位越界或空返回 null。</summary>
    internal static Texture2D? Texture(int slot)
    {
        var textures = TextureAssets.Background;
        if (textures is null || slot < 0 || slot >= textures.Length) return null;

        try
        {
            return textures[slot]?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static string Trim(string texture) =>
        texture.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? texture[..^4] : texture;
}