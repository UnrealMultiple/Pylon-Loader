using Terraria;
using Terraria.IO;

namespace Pylon.ModLoader;

/// <summary>玩家侧车存档：把 ModPlayer 存档族接到玩家文件读写；PreSavePlayer→写 .plr→PreSaveCustomData→SaveData→PostSavePlayer，读档后逐作者 LoadData，侧车与发型共用 .pl.json。</summary>
internal sealed class PlayerSavePipeline : ILoaderModule
{
    public static PlayerSavePipeline Instance { get; } = new();

    public string Name => "playersave";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.InternalSavePlayerFile += SavePlayerFile;
        On.Terraria.Player.LoadPlayer += LoadPlayerFile;

        Log.Info("[playersave] 已挂（PreSavePlayer / SaveData / PreSaveCustomData / PostSavePlayer / LoadData）");
        return true;
    }

    private static PlayerBinding Binding => PlayerPipeline.Instance.Binding;

    private static void SavePlayerFile(On.Terraria.Player.orig_InternalSavePlayerFile orig, PlayerFileData playerFile)
    {
        var player = playerFile is null ? null : playerFile.Player;
        if (player is not null) Dispatch(player, HookId.PreSavePlayer);

        try
        {
            orig(playerFile);

            if (player is null) return;
            if (PlayerSidecar.DataFile(playerFile) is not { } file) return;

            Dispatch(player, HookId.PreSaveCustomData);

            var all = TagJson.Read(file);
            var templates = Binding.Templates;
            var instances = Binding.Of(player);

            for (var i = 0; i < templates.Count && i < instances.Length; i++)
            {
                var template = templates[i];
                if (!template.HookEnabled(HookId.SaveData)) continue;

                var mod = instances[i];
                var key = KeyOf(template);

                try
                {
                    var tag = new TagCompound();
                    mod.SaveData(tag);
                    if (tag.Count > 0) all.Set(key, tag);
                    else all.Remove(key);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[playersave] {KeyOf(mod)}.SaveData 异常：{Safe.Describe(e)}");
                }
            }

            if (all.Count > 0) TagJson.Write(file, all);
            else File.Delete(file);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[playersave] 存档失败：" + Safe.Describe(e));
        }
        finally
        {
            if (player is not null) Dispatch(player, HookId.PostSavePlayer);
        }
    }

    private static PlayerFileData LoadPlayerFile(On.Terraria.Player.orig_LoadPlayer orig, string playerPath, bool cloudSave)
    {
        var data = orig(playerPath, cloudSave);

        try
        {
            if (data is null) return null!;

            var player = data.Player;
            if (player is null) return data;
            if (PlayerSidecar.DataFile(data) is not { } file) return data;

            var all = TagJson.Read(file);
            var templates = Binding.Templates;
            var instances = Binding.Of(player);

            for (var i = 0; i < templates.Count && i < instances.Length; i++)
            {
                var template = templates[i];
                if (!template.HookEnabled(HookId.LoadData)) continue;
                if (all.Get(KeyOf(template)) is not TagCompound saved) continue;

                var mod = instances[i];
                try
                {
                    mod.LoadData(saved);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[playersave] {KeyOf(mod)}.LoadData 异常：{Safe.Describe(e)}");
                }
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[playersave] 读档失败：" + Safe.Describe(e));
        }

        return data;
    }

    private static void Dispatch(Player player, HookId hook)
    {
        if (!Binding.Active(hook)) return;

        var mods = Binding.Of(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(hook)) continue;

            try
            {
                switch (hook)
                {
                    case HookId.PreSavePlayer: mod.PreSavePlayer(); break;
                    case HookId.PostSavePlayer: mod.PostSavePlayer(); break;
                    case HookId.PreSaveCustomData: mod.PreSaveCustomData(); break;
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playersave] {KeyOf(mod)}.{hook} 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static string KeyOf(ModPlayer mod) =>
        (mod.Mod is null || mod.Mod.Name.Length == 0 ? "?" : mod.Mod.Name) + "/" + mod.GetType().Name;
}
