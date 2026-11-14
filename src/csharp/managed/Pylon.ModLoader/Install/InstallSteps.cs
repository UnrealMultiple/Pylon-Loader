using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>安装步骤的内容操作：每个方法只做一件事，可单独重入（表被重建时会再跑一次）。</summary>
internal static class InstallSteps
{
    private static IModLog Log => LoaderHost.Current.Log;

    public static void LoadTextures(LoaderHost host)
    {
        if (LoaderHost.Current.Mods.Count > 0) PylonTexture.Logger = LoaderHost.Current.Mods.Mods[0].Logger;

        var loaded = 0;
        foreach (var type in ModContent.GetContent<ModType>())
        {
            if (string.IsNullOrEmpty(type.TexturePath)) continue;
            if (type is ModMount) continue;

            try
            {
                if (type.Texture is null)
                {
                    type.Texture = type.Mod.LoadAsset(type.TexturePath);
                    loaded++;
                }
            }
            catch (Exception e)
            {
                host.Log.Warn($"{type.Name} 的贴图跳过（{type.TexturePath}）：{Safe.Describe(e)}");
            }
        }

        host.Log.Info($"贴图：上传 {loaded}");
    }

    public static void WriteTextures(LoaderHost host)
    {
        _ = host;
        var written = 0;

        UnloadedItems.WriteTextures();

        foreach (var type in ModContent.GetContent<ModType>())
        {
            switch (type)
            {
                case ModItem item:
                    ApplyItemName(item);
                    ApplyItemTooltip(item);
                    ApplyItemFlame(item);
                    break;
                case ModBuff buff:
                    ApplyBuffText(buff);
                    break;
            }

            if (type.Texture is null) continue;
            var table = TableFor(type);
            if (table is null) continue;
            try
            {
                if (TextureSlots.TryWrite(table.Value, type.Type, type.Texture)) written++;
                else Log.Warn($"{type.Name} 的贴图没写进 {table}[{type.Type}]（表长 {TextureSlots.Length(table.Value)}）");
            }
            catch (Exception e)
            {
                Log.Warn($"{type.Name} 的贴图写槽失败：{Safe.Describe(e)}");
            }
        }
        Log.Info($"贴图：写入表槽 {written}");
    }

    public static void RegisterItemSamples()
    {
        var items = ContentSamples.ItemsByType;
        if (items is null) return;

        var count = 0;
        foreach (var type in ModContent.GetContent<ModItem>())
        {
            try
            {
                var sample = new Item();
                sample.SetDefaults(type.Type, null);
                items[type.Type] = sample;

                var persistentId = (type.Mod?.Name ?? "Mod") + "/" + type.Name;
                if (ContentSamples.ItemPersistentIdsByNetIds is { } byId) byId[type.Type] = persistentId;
                if (ContentSamples.ItemNetIdsByPersistentIds is { } byName) byName[persistentId] = type.Type;
                count++;
            }
            catch (Exception e)
            {
                Log.Warn($"{type.Name} 的 ContentSamples 样例注册失败：{Safe.Describe(e)}");
            }
        }

        count += UnloadedItems.RegisterSamples(items,
            ContentSamples.ItemPersistentIdsByNetIds, ContentSamples.ItemNetIdsByPersistentIds);
        Log.Info($"ContentSamples 物品样例：{count}");
    }

    public static void NpcStaticDefaults()
    {
        var count = 0;
        foreach (var type in ModContent.GetContent<ModNpc>())
        {
            try
            {
                type.SetStaticDefaults();
                type.PostStaticDefaults();
                Safe.Run(type.Name + ".ModifyNPCLoot", () => type.ModifyNPCLoot(new NPCLoot(type.Type)));
                count++;
            }
            catch (Exception e)
            {
                Log.Warn($"{type.Name} 的 NPC 静态默认值失败：{Safe.Describe(e)}");
            }
        }
        Log.Info($"NPC 静态默认值：{count}");
    }

    public static void BuffStaticDefaults()
    {
        var count = 0;
        foreach (var type in ModContent.GetContent<ModBuff>())
        {
            try
            {
                type.SetStaticDefaults();
                type.PostStaticDefaults();
                type.ModifyDisplayName();
                type.ModifyDescription();
                count++;
            }
            catch (Exception e)
            {
                Log.Warn($"{type.Name} 的 buff 静态默认值失败：{Safe.Describe(e)}");
            }
        }
        foreach (var global in GlobalBuffRegistry.All)
        {
            Safe.Run(global.Name + ".SetStaticDefaults", global.SetStaticDefaults);
        }
        Log.Info($"buff 静态默认值：{count}");
    }

    public static void ProjectileTemplates() => ProjectilePipeline.Instance.SetupTemplates(Log);

    public static void RegisterNpcSamples()
    {
        var npcs = ContentSamples.NpcsByNetId;
        if (npcs is null) return;

        var persistentById = ContentSamples.NpcPersistentIdsByNetIds;
        var idByPersistent = ContentSamples.NpcNetIdsByPersistentIds;
        var creditById = ContentSamples.NpcBestiaryCreditIdsByNpcNetIds;
        var sorting = ContentSamples.NpcBestiarySortingId;
        var rarity = ContentSamples.NpcBestiaryRarityStars;

        foreach (var type in ModContent.GetContent<ModNpc>())
        {
            try
            {
                var sample = new NPC();
                sample.SetDefaults(type.Type, new NPCSpawnParams());
                npcs[type.Type] = sample;

                var persistentId = (type.Mod?.Name ?? "Mod") + "/" + type.Name;
                if (persistentById is not null) persistentById[type.Type] = persistentId;
                if (idByPersistent is not null) idByPersistent[persistentId] = type.Type;
                if (creditById is not null) creditById[type.Type] = persistentId;
                if (sorting is not null) sorting[type.Type] = type.Type;

                var stars = type.BestiaryRarityStars;
                if (stars <= 0) stars = Math.Clamp(1 + sample.lifeMax / 250, 1, 5);
                if (rarity is not null) rarity[type.Type] = stars;

                ApplyNpcName(type);
            }
            catch (Exception e)
            {
                Log.Warn($"{type.Name} 的 NPC 样例注册失败：{Safe.Describe(e)}");
            }
        }
    }

    public static void ReapplyTexts()
    {
        var count = 0;
        foreach (var type in ModContent.GetContent<ModType>())
        {
            switch (type)
            {
                case ModItem item:
                    ApplyItemName(item);
                    ApplyItemTooltip(item);
                    count++;
                    break;
                case ModBuff buff:
                    ApplyBuffText(buff);
                    count++;
                    break;
                case ModNpc npc:
                    ApplyNpcName(npc);
                    count++;
                    break;
            }
        }

        Log.Info($"本地化重写：{count} 个内容（世界加载后）");
    }

    private static void ApplyItemName(ModItem item)
    {
        var cache = Lang._itemNameCache;
        if (cache is null || item.Type < 0 || item.Type >= cache.Length) return;

        var key = "ItemName." + item.Name;
        var text = new Terraria.Localization.LocalizedText(key, item.DisplayName);
        cache[item.Type] = text;
        LocalizationKeys.Register(key, text);

    }

    private static void ApplyItemTooltip(ModItem item)
    {
        item.TooltipLines.Clear();
        if (!string.IsNullOrEmpty(item.Tooltip)) item.TooltipLines.AddRange(item.Tooltip.Split('\n'));
        Safe.Run(item.Name + ".ModifyTooltipLines", item.ModifyTooltipLines);
        if (item.TooltipLines.Count == 0) return;

        var cache = Lang._itemTooltipCache;
        if (cache is null || item.Type < 0 || item.Type >= cache.Length) return;

        var key = "Pylon.ItemTooltip." + (item.Mod is null ? "Mod" : item.Mod.Name) + "." + item.Name;
        LocalizationKeys.Register(key, new Terraria.Localization.LocalizedText(key, string.Join("\n", item.TooltipLines)));

        try
        {
            cache[item.Type] = Terraria.UI.ItemTooltip.FromLanguageKey((short)item.Type, key);
        }
        catch
        {
            var lines = new string[item.TooltipLines.Count];
            for (var i = 0; i < lines.Length; i++) lines[i] = item.TooltipLines[i];
            cache[item.Type] = Terraria.UI.ItemTooltip.FromHardcodedText(lines);
        }
    }

    private static Asset<Texture2D>? _blankFlame;

    private static void ApplyItemFlame(ModItem item)
    {
        try
        {
            var stem = System.IO.Path.ChangeExtension(item.TexturePath, null);
            var flamePath = stem + "_Flame.png";
            var asset = item.Mod is not null && ModContent.HasAsset(item.Mod, flamePath)
                ? item.Mod.LoadAsset(flamePath)
                : BlankFlame();
            if (asset is null) return;

            TextureSlots.TryWrite(TextureTable.ItemFlame, item.Type, asset);
        }
        catch (Exception e)
        {
            Log.Warn($"{item.Name} 的火焰贴图写槽失败：{Safe.Describe(e)}");
        }
    }

    private static Asset<Texture2D>? BlankFlame()
    {
        if (_blankFlame is not null) return _blankFlame;

        try
        {
            var texture = PylonTexture.Decode(RawPng.Encode(4, 4, new uint[16]), "pylon/blank-flame");
            var asset = new Asset<Texture2D>("pylon:blank-flame");
            asset.SubmitLoadedContent(texture, null);
            _blankFlame = asset;
        }
        catch (Exception e)
        {
            Log.Warn("空白火焰生成失败：" + Safe.Describe(e));
        }

        return _blankFlame;
    }

    private static void ApplyBuffText(ModBuff buff)
    {
        var name = string.IsNullOrEmpty(buff.DisplayName) ? buff.Name : buff.DisplayName;
        var description = buff.Description ?? "";

        var names = Lang._buffNameCache;
        if (names is not null && buff.Type >= 0 && buff.Type < names.Length)
        {
            var key = "BuffName." + buff.Name;
            var text = new Terraria.Localization.LocalizedText(key, name);
            names[buff.Type] = text;
            LocalizationKeys.Register(key, text);
        }

        var descriptions = Lang._buffDescriptionCache;
        if (descriptions is not null && buff.Type >= 0 && buff.Type < descriptions.Length)
        {
            var key = "BuffDescription." + buff.Name;
            var text = new Terraria.Localization.LocalizedText(key, description);
            descriptions[buff.Type] = text;
            LocalizationKeys.Register(key, text);
        }
    }

    private static void ApplyNpcName(ModNpc npc)
    {
        if (npc.Type < 0) return;

        var need = Math.Max(npc.Type + 1, ModContent.NextType(ContentKind.Npc));
        var cache = Lang._npcNameCache;
        if (cache is not null && cache.Length < need && ArrayGrow.Grow(ref cache, need))
            Lang._npcNameCache = cache;
        if (cache is null || npc.Type >= cache.Length) return;

        var text = string.IsNullOrEmpty(npc.DisplayName) ? npc.Name : npc.DisplayName;
        var key = "NPCName." + npc.Name;
        var localized = new Terraria.Localization.LocalizedText(key, text);
        cache[npc.Type] = localized;
        LocalizationKeys.Register(key, localized);

        try
        {
            var search = NPCID.Search;
            if (search is not null && !search.ContainsName(npc.FullKey)) search.Add(npc.FullKey, npc.Type);
        }
        catch (Exception e)
        {
            Log.Warn($"{npc.Name} 登记 NPCID.Search 失败：{Safe.Describe(e)}");
        }
    }

    private static TextureTable? TableFor(ModType type) => type switch
    {
        ModGore => TextureTable.Gore,
        ModCloud => TextureTable.Cloud,
        ModHair => TextureTable.PlayerHair,
        ModItem => TextureTable.Item,
        ModTile => TextureTable.Tile,
        ModWall => TextureTable.Wall,
        ModProjectile => TextureTable.Projectile,
        ModNpc => TextureTable.Npc,
        ModBuff => TextureTable.Buff,
        _ => null,
    };
}
