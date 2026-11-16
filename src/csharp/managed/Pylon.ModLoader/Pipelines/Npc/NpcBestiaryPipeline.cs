using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>图鉴：内容就绪时给每个 mod NPC 建 Enemy/TownNPC/Critter 条目，先跑 ModNpc/GlobalNpc.SetBestiary，再注册并补掉落。</summary>
internal static class NpcBestiaryPipeline
{
    internal static void Build()
    {
        var database = Main.BestiaryDB;
        if (database is null) return;

        var added = 0;
        foreach (var own in ModContent.GetContent<ModNpc>())
        {
            try
            {
                if (Register(database, own)) added++;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[bestiary] {own.Name} 图鉴条目异常：{Safe.Describe(e)}");
            }
        }

        BiomeBestiaryHooks.RegisterFilters(database);

        if (GlobalNpcRegistry.AnyWith(HookId.SetBestiary))
        {
            for (var type = 1; type < NPCID.Count; type++)
            {
                try
                {
                    Vanilla(database, type);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[bestiary] 原版 NPC {type} 图鉴条目异常：{Safe.Describe(e)}");
                }
            }
        }

        if (added > 0) LoaderHost.Current.Log.Info($"[bestiary] 注册 {added} 个 mod NPC 图鉴条目");
    }

    private static bool Register(BestiaryDatabase database, ModNpc own)
    {
        var type = own.Type;
        RemoveExisting(database, type);

        if (HiddenByDrawOffset(type)) return false;

        var sample = Sample(type);
        var entry = sample is not null && sample.townNPC
            ? BestiaryEntry.TownNPC(type)
            : CountsAsCritter(type)
                ? BestiaryEntry.Critter(type)
                : BestiaryEntry.Enemy(type);

        if (own.HookEnabled(HookId.SetBestiary))
        {
            try
            {
                own.SetBestiary(database, entry);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[bestiary] {own.Name}.SetBestiary 异常：{Safe.Describe(e)}");
            }
        }

        BiomeBestiaryHooks.AddTo(entry, own);

        DispatchGlobals(sample, database, entry);

        database.Register(entry);
        database.ExtractDropsForNPC(Main.ItemDropsDB, type);
        return true;
    }

    private static void Vanilla(BestiaryDatabase database, int type)
    {
        if (database.FindEntryByNPCID(type) is not { } entry) return;

        var sample = Sample(type);
        if (sample is null) return;

        DispatchGlobals(sample, database, entry);
    }

    private static void DispatchGlobals(NPC? sample, BestiaryDatabase database, BestiaryEntry entry)
    {
        foreach (var global in GlobalNpcRegistry.For(sample, HookId.SetBestiary))
        {
            try
            {
                global.SetBestiary(sample!, database, entry);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[bestiary] {global.FullKey}.SetBestiary 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static void RemoveExisting(BestiaryDatabase database, int type)
    {
        if (database.FindEntryByNPCID(type) is not { } existing) return;

        var entries = database.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Pointer != existing.Pointer) continue;
            entries.RemoveAt(i);
            return;
        }
    }

    private static bool HiddenByDrawOffset(int type)
    {
        var table = NPCID.Sets.NPCBestiaryDrawOffset;
        return table is not null && table.TryGetValue(type, out var modifiers) && modifiers.Hide;
    }

    private static bool CountsAsCritter(int type)
    {
        var table = NPCID.Sets.CountsAsCritter;
        return table is not null && (uint)type < (uint)table.Length && table[type];
    }

    private static NPC? Sample(int type)
    {
        var samples = ContentSamples.NpcsByNetId;
        if (samples is null) return null;
        return samples.TryGetValue(type, out var sample) ? sample : null;
    }
}
