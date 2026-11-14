using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;

namespace Pylon.ModLoader;

/// <summary>
/// "？"未加载物品池：mod 卸载后读档，原版 SetDefaults 会把不认识的类型号归零，物品静默消失。
/// 这里占住物品表开头的 64 个类型号（先于一切 mod 发号），读档遇到未知键时按键占一格，
/// 物品以"？"占位形态回来（maxStack 放大到能装下原物品），mod 重装后由物品存档管线按原键恢复。
/// 池类型不是注册内容：SetDefaults / ContentSamples / 贴图 / 名字都在各自入口显式分一支出，
/// 名字查询时从占用的键派生（"未加载物品（原物品名）"），不写语言缓存。
/// </summary>
internal static class UnloadedItems
{
    internal const int PoolSize = 64;

    private static int _first;
    private static readonly string?[] SlotKeys = new string[PoolSize];
    private static Asset<GameTexture2D>? _placeholder;

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>首次注册 mod 前调用：占住池的类型号区间（幂等）。</summary>
    internal static void Reserve()
    {
        if (_first > 0) return;

        _first = LoaderHost.Current.Content.NextType(ContentKind.Item);
        LoaderHost.Current.Content.Reserve(ContentKind.Item, PoolSize);
        Log.Info($"[item] ？池已占号：{_first}..{_first + PoolSize - 1}");
    }

    internal static bool IsUnloadedType(int type) =>
        _first > 0 && (uint)(type - _first) < PoolSize;

    // ===================== SetDefaults 分支 =====================

    /// <summary>占位形态：不用也不放置，但堆叠必须容得下原物品。</summary>
    internal static void Apply(Item item, int type)
    {
        try
        {
            item.ResetStats(type);
            item.type = type;
            item.maxStack = 9999;
            if (item.width <= 0) item.width = 16;
            if (item.height <= 0) item.height = 16;
        }
        catch (Exception e)
        {
            Safe.Report("？物品默认值", e);
        }
    }

    // ===================== 键 ↔ 类型 =====================

    /// <summary>读档用的键 → 类型分配：同键复用同一格，新键占第一个空格；池满返回 -1（行进等待名单）。</summary>
    internal static int TypeFor(string key)
    {
        if (_first <= 0 || string.IsNullOrEmpty(key)) return -1;

        for (var i = 0; i < PoolSize; i++)
        {
            if (SlotKeys[i] == key) return _first + i;
        }

        for (var i = 0; i < PoolSize; i++)
        {
            if (SlotKeys[i] is not null) continue;
            SlotKeys[i] = key;
            ApplyCacheName(_first + i);
            return _first + i;
        }

        return -1;
    }

    /// <summary>名字写进语言缓存：GetItemName（LocalizedText 路径）也能拿到，与 mod 物品同一机制。</summary>
    private static void ApplyCacheName(int type)
    {
        try
        {
            var cache = Lang._itemNameCache;
            if (cache is null || type < 0 || type >= cache.Length) return;

            var key = "ItemName.Unloaded" + (type - _first);
            var text = new Terraria.Localization.LocalizedText(key, DisplayNameOf(type) ?? "未加载物品");
            cache[type] = text;
            LocalizationKeys.Register(key, text);
        }
        catch
        {
            // 缓存还没建时忽略：显示入口 GetItemNameValue 是动态派生的，不受影响。
        }
    }

    /// <summary>类型 → 占用的键；不是池类型返回 null。</summary>
    internal static string? KeyFor(int type) =>
        IsUnloadedType(type) ? SlotKeys[type - _first] : null;

    /// <summary>读档开始时清空分配：每个存档的键集独立，残留会把不同存档的物品混进同一格。</summary>
    internal static void ResetSlots()
    {
        for (var i = 0; i < PoolSize; i++) SlotKeys[i] = null;
    }

    /// <summary>？物品的显示名：占用中的格子带上原物品名，方便认出是哪件。</summary>
    internal static string? DisplayNameOf(int type)
    {
        if (!IsUnloadedType(type)) return null;
        var key = SlotKeys[type - _first];
        return key is null ? "未加载物品" : $"未加载物品（{KeyLabel(key)}）";
    }

    private static string KeyLabel(string key)
    {
        var slash = key.IndexOf('/');
        return slash >= 0 && slash + 1 < key.Length ? key[(slash + 1)..] : key;
    }

    // ===================== 样例与贴图 =====================

    /// <summary>全部池类型都要有 ContentSamples 样例（世界侧代码会按类型号查表）；占用的键顺带注册持久 ID。</summary>
    internal static int RegisterSamples(Il2CppSystem.Collections.Generic.Dictionary<int, Item> items,
                                        Il2CppSystem.Collections.Generic.Dictionary<int, string>? persistentByNet,
                                        Il2CppSystem.Collections.Generic.Dictionary<string, int>? netByPersistent)
    {
        if (_first <= 0 || items is null) return 0;

        var count = 0;
        for (var i = 0; i < PoolSize; i++)
        {
            var type = _first + i;
            try
            {
                var sample = new Item();
                sample.SetDefaults(type, null);
                items[type] = sample;

                // 键 → 占位类型：只补空缺，真实 mod 已注册的映射不能被占位盖掉。
                var key = SlotKeys[i];
                if (key is not null && netByPersistent is not null && !netByPersistent.ContainsKey(key))
                {
                    netByPersistent[key] = type;
                    persistentByNet?[type] = key;
                }

                count++;
            }
            catch (Exception e)
            {
                Log.Warn($"[item] ？池样例 {type} 注册失败：{e.Message}");
            }
        }

        return count;
    }

    /// <summary>64 个类型号全部写占位贴图：没贴图的对象画到背包 / 手持就是白块。</summary>
    internal static void WriteTextures()
    {
        if (_first <= 0) return;

        var asset = PlaceholderAsset();
        if (asset is null) return;

        var written = 0;
        for (var i = 0; i < PoolSize; i++)
        {
            try
            {
                if (TextureSlots.TryWrite(TextureTable.Item, _first + i, asset)) written++;
            }
            catch (Exception e)
            {
                Log.Warn($"[item] ？池贴图 {_first + i} 写槽失败：{e.Message}");
            }
        }

        if (written > 0) Log.Info($"[item] ？池贴图：写 {written}");
    }

    private static Asset<GameTexture2D>? PlaceholderAsset()
    {
        if (_placeholder is not null) return _placeholder;

        try
        {
            var texture = PylonTexture.Decode(PlaceholderPng(), "pylon/unloaded-item");
            var asset = new Asset<GameTexture2D>("pylon:unloaded-item");
            asset.SubmitLoadedContent(texture, null);
            _placeholder = asset;
        }
        catch (Exception e)
        {
            Safe.Report("？池占位贴图", e);
        }

        return _placeholder;
    }

    // ===================== 占位贴图（内嵌 PNG） =====================

    /// <summary>16×16：深底白"?"，走 RawPng + 框架的贴图上传管线。</summary>
    private static byte[] PlaceholderPng()
    {
        const int size = 16;
        const uint background = 0xFF34202B;   // ABGR：深紫底
        const uint glyph = 0xFFE8E8F0;        // 浅色问号

        var pixels = new uint[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++) pixels[y * size + x] = Glyph(x, y) ? glyph : background;
        }

        return RawPng.Encode(size, size, pixels);
    }

    private static bool Glyph(int x, int y)
    {
        // 16×16 的问号位图（1 = 笔画）。
        return (y, x) switch
        {
            (2, 5) or (2, 6) or (2, 7) or (2, 8) or (2, 9) or (2, 10) => true,
            (3, 4) or (3, 5) or (3, 10) or (3, 11) => true,
            (4, 4) or (4, 11) => true,
            (5, 10) or (5, 11) => true,
            (6, 9) or (6, 10) => true,
            (7, 8) or (7, 9) => true,
            (8, 8) or (8, 9) => true,
            (10, 8) or (10, 9) => true,
            (12, 8) or (12, 9) => true,
            _ => false,
        };
    }
}
