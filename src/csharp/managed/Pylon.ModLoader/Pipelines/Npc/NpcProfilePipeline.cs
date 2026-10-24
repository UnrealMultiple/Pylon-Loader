using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>城镇 NPC 外观档案：把作者的档案写进游戏自带 TownNPCProfiles 表，游戏自己按表查询（变体/名字/贴图/头像）。</summary>
internal static class NpcProfilePipeline
{
    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Build()
    {
        var profiles = TownNPCProfiles.Instance?._townNPCProfiles;
        if (profiles is null)
        {
            Log.Warn("[profile] TownNPCProfiles 还没就绪，城镇 NPC 档案跳过");
            return;
        }

        var anyGlobal = GlobalNpcRegistry.AnyWith(HookId.ModifyTownNPCProfile);
        var registered = 0;

        foreach (var modNpc in ModContent.GetContent<ModNpc>())
        {
            if (!modNpc.Registered || modNpc.Type <= 0) continue;
            var own = modNpc.Hooks.Has(HookId.TownNPCProfile);
            if (!own && !anyGlobal) continue;
            if (!IsTownNpc(modNpc.Type)) continue;

            ITownNPCProfile? profile = null;
            if (own) Safe.Run($"{modNpc.Name}.TownNPCProfile", () => profile = modNpc.TownNPCProfile());
            profile = DispatchGlobals(modNpc.Type, profile);
            if (profile is null) continue;

            profiles[modNpc.Type] = profile;
            registered++;
        }

        if (anyGlobal)
        {
            for (var type = 1; type < VanillaCounts.Npc; type++)
            {
                if (!IsTownNpc(type)) continue;
                var profile = DispatchGlobals(type, null);
                if (profile is null) continue;
                profiles[type] = profile;
                registered++;
            }
        }

        Log.Info($"[profile] 城镇 NPC 档案：注册 {registered}");
    }

    private static bool IsTownNpc(int type)
    {
        var sample = SampleOf(type);
        return sample is not null && sample.townNPC;
    }

    private static NPC? SampleOf(int type)
    {
        var samples = ContentSamples.NpcsByNetId;
        if (samples is null || !samples.TryGetValue(type, out var sample)) return null;
        return sample is not null && sample.Pointer != 0 ? sample : null;
    }

    private static ITownNPCProfile? DispatchGlobals(int type, ITownNPCProfile? profile)
    {
        var sample = SampleOf(type);
        if (sample is null) return profile;

        var globals = GlobalNpcRegistry.For(sample, HookId.ModifyTownNPCProfile);
        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                profile = globals[g].ModifyTownNPCProfile(sample) ?? profile;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[profile] {globals[g].Name}.ModifyTownNPCProfile 异常：{Safe.Describe(e)}");
            }
        }
        return profile;
    }
}
