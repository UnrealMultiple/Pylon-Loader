using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.SaveData;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>城镇 NPC 与房间存档：.wld 不写 mod 类型号，NPC 行与房间行走侧车 &lt;world&gt;.wld.npcs.json，读档按 FullKey 复活，mod 缺席的行进等待名单。</summary>
internal sealed class NpcSavePipeline
{
    private const string NpcSection = "npcs";
    private const string RoomSection = "rooms";

    private List<TagCompound>? _keptNpcs;
    private List<TagCompound>? _keptRooms;
    private List<TagCompound>? _pendingRooms;

    public void Install()
    {
        On.Terraria.IO.WorldFile.InternalSaveWorld += SaveWorld;
        On.Terraria.IO.WorldFile.SaveNPCs += SaveNpcs;
        On.Terraria.WorldGen.clearWorld += ClearWorld;
        On.Terraria.IO.WorldFile.LoadNPCs += LoadNpcs;
        On.Terraria.IO.WorldFile.LoadTownManager += LoadRooms;
    }

    private void SaveWorld(On.Terraria.IO.WorldFile.orig_InternalSaveWorld orig, bool useCloudSaving, bool resetTime)
    {
        var rooms = useCloudSaving ? null : CollectRooms();

        try
        {
            orig(useCloudSaving, resetTime);
        }
        finally
        {
            if (rooms is not null) PutBackRooms(rooms);
        }

        if (useCloudSaving) return;
        WriteFile(rooms);
    }

    private int SaveNpcs(On.Terraria.IO.WorldFile.orig_SaveNPCs orig, Il2CppSystem.IO.BinaryWriter writer)
    {
        var worldData = Main.ActiveWorldFileData;
        if (worldData is null || worldData.IsCloudSave) return orig(writer);

        var npcs = Main.npc;
        List<NPC>? hidden = null;
        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is null || !npc.active || !npc.townNPC) continue;
            if (!ModContent.IsCustom(ContentKind.Npc, npc.type)) continue;
            npc.townNPC = false;
            (hidden ??= new List<NPC>()).Add(npc);
        }

        try
        {
            return orig(writer);
        }
        finally
        {
            if (hidden is not null)
            {
                foreach (var npc in hidden) npc.townNPC = true;
            }
        }
    }

    private List<TagCompound> CollectRooms()
    {
        var result = new List<TagCompound>();
        var manager = WorldGen.TownManager;
        if (manager is null) return result;

        var npcs = Main.npc;
        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is null || !npc.active || !npc.townNPC) continue;
            if (!ModContent.IsCustom(ContentKind.Npc, npc.type)) continue;
            if (!manager.HasRoom(npc.type, out var position)) continue;

            result.Add(new TagCompound
            {
                ["k"] = KeyOf(npc.type),
                ["x"] = position.X,
                ["y"] = position.Y,
            });
            manager.KickOut(npc.type);
        }

        return result;
    }

    private void PutBackRooms(List<TagCompound> rooms)
    {
        var manager = WorldGen.TownManager;
        if (manager is null) return;

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            var type = TypeOf(room.GetString("k"));
            if (type > 0) manager.SetRoom(type, room.GetInt("x"), room.GetInt("y"));
        }
    }

    private void WriteFile(List<TagCompound>? rooms)
    {
        if (DataFile() is null) return;

        var root = WorldSidecar.Read();

        var npcs = new List<object?>();
        var world = Main.npc;
        for (var i = 0; i < world.Length; i++)
        {
            var npc = world[i];
            if (npc is null || !npc.active || !npc.townNPC) continue;
            if (!ModContent.IsCustom(ContentKind.Npc, npc.type)) continue;

            var row = new TagCompound
            {
                ["k"] = KeyOf(npc.type),
                ["x"] = (int)npc.position.X,
                ["y"] = (int)npc.position.Y,
                ["h"] = npc.homeless,
                ["hx"] = npc.homeTileX,
                ["hy"] = npc.homeTileY,
                ["v"] = npc.townNpcVariationIndex,
            };

            var given = npc.GivenName;
            if (!string.IsNullOrEmpty(given)) row["n"] = given;

            var data = SaveData(npc);
            if (data is not null && data.Count > 0) row["d"] = data;

            npcs.Add(row);
        }

        var roomRows = new List<object?>();
        if (rooms is not null) roomRows.AddRange(rooms);
        if (_keptRooms is not null) roomRows.AddRange(_keptRooms);

        if (_keptNpcs is not null) npcs.AddRange(_keptNpcs);
        if (npcs.Count > 0) root.Set(NpcSection, npcs);
        if (roomRows.Count > 0) root.Set(RoomSection, roomRows);

        if (root.Count > 0) WorldSidecar.Write(root);
    }

    private void LoadNpcs(On.Terraria.IO.WorldFile.orig_LoadNPCs orig, Il2CppSystem.IO.BinaryReader reader)
    {
        orig(reader);

        _keptNpcs = null;
        _keptRooms = null;
        _pendingRooms = [];

        var root = ReadFile();
        if (root.Count == 0) return;

        var rows = root.GetList<TagCompound>(NpcSection);
        for (var i = 0; i < rows.Count; i++)
        {
            if (Restore(rows[i])) continue;

            _keptNpcs ??= [];
            _keptNpcs.Add(rows[i]);
        }

        var rooms = root.GetList<TagCompound>(RoomSection);
        for (var i = 0; i < rooms.Count; i++)
        {
            _pendingRooms.Add(rooms[i]);
        }
    }

    private void LoadRooms(On.Terraria.IO.WorldFile.orig_LoadTownManager orig, Il2CppSystem.IO.BinaryReader reader)
    {
        orig(reader);

        var pending = _pendingRooms;
        _pendingRooms = null;
        if (pending is null) return;

        var manager = WorldGen.TownManager;
        for (var i = 0; i < pending.Count; i++)
        {
            var room = pending[i];
            var type = TypeOf(room.GetString("k"));
            if (type <= 0)
            {
                _keptRooms ??= [];
                _keptRooms.Add(room);
                continue;
            }

            try
            {
                manager?.SetRoom(type, room.GetInt("x"), room.GetInt("y"));
            }
            catch (Exception e)
            {
                Safe.Report("npc 房间恢复", e);
            }
        }
    }

    private bool Restore(TagCompound row)
    {
        var type = TypeOf(row.GetString("k"));
        if (type <= 0) return false;

        var npc = FreeNpc();
        if (npc is null)
        {
            Safe.Once("npc.save.nofree", "[npc] 存档恢复：没有空闲 NPC 槽位", true);
            return false;
        }

        try
        {
            npc.SetDefaults(type, new NPCSpawnParams());
            npc.position = new Vector2(row.GetInt("x"), row.GetInt("y"));
            npc.homeless = row.GetBool("h");
            npc.homeTileX = row.GetInt("hx");
            npc.homeTileY = row.GetInt("hy");
            npc.townNpcVariationIndex = row.GetInt("v");
            npc.active = true;

            var name = row.GetString("n");
            if (!string.IsNullOrEmpty(name)) npc.GivenName = name;

            if (row.ContainsKey("d")) LoadData(npc, row.GetCompound("d"));
            return true;
        }
        catch (Exception e)
        {
            Safe.Report("npc 恢复", e);
            return false;
        }
    }

    private static bool NeedSaving(NPC npc)
    {
        if (NpcPipeline.Instance.InstanceOf(npc) is { } mod && mod.HookEnabled(HookId.NeedSaving))
        {
            try
            {
                if (mod.NeedSaving()) return true;
            }
            catch (Exception e) { Safe.Report(mod.FullKey + ".NeedSaving", e); }
        }

        foreach (var global in GlobalNpcRegistry.For(npc, HookId.NeedSaving))
        {
            try
            {
                if (global.NeedSaving(npc)) return true;
            }
            catch (Exception e) { Safe.Report(global.FullKey + ".NeedSaving", e); }
        }

        return false;
    }

    private static TagCompound? SaveData(NPC npc)
    {
        if (!NeedSaving(npc)) return null;

        var saved = new TagCompound();
        var mod = NpcPipeline.Instance.InstanceOf(npc);

        if (mod is { } own && own.HookEnabled(HookId.SaveData))
        {
            var tag = new TagCompound();
            try { own.SaveData(tag); }
            catch (Exception e) { Safe.Report(own.FullKey + ".SaveData", e); }
            if (tag.Count > 0) saved["data"] = tag;
        }

        var globals = new List<object?>();
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.SaveData))
        {
            var tag = new TagCompound();
            try { global.SaveData(npc, tag); }
            catch (Exception e) { Safe.Report(global.FullKey + ".SaveData", e); }
            if (tag.Count == 0) continue;
            globals.Add(new TagCompound { ["m"] = global.Mod?.Name ?? "", ["n"] = global.Name, ["d"] = tag });
        }

        if (globals.Count > 0) saved["g"] = globals;
        return saved.Count == 0 ? null : saved;
    }

    private static void LoadData(NPC npc, TagCompound saved)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);

        if (mod is { } own && own.HookEnabled(HookId.LoadData) && saved.ContainsKey("data"))
        {
            try { own.LoadData(saved.GetCompound("data")); }
            catch (Exception e) { Safe.Report(own.FullKey + ".LoadData", e); }
        }

        if (!saved.ContainsKey("g")) return;
        var globals = saved.GetList<TagCompound>("g");

        foreach (var tag in globals)
        {
            if (!tag.ContainsKey("d")) continue;
            var key = tag.GetString("m") + "/" + tag.GetString("n");
            var globalData = tag.GetCompound("d");

            foreach (var global in GlobalNpcRegistry.For(npc, HookId.LoadData))
            {
                if (global.FullKey != key) continue;

                try { global.LoadData(npc, globalData); }
                catch (Exception e) { Safe.Report(global.FullKey + ".LoadData", e); }
            }
        }
    }

    private void ClearWorld(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        orig();
        _keptNpcs = null;
        _keptRooms = null;
        _pendingRooms = null;
    }

    private static NPC? FreeNpc()
    {
        var npcs = Main.npc;
        for (var i = 0; i < npcs.Length - 1; i++)
        {
            var npc = npcs[i];
            if (npc is { active: false }) return npc;
        }

        return null;
    }

    private static string KeyOf(int type) =>
        ModContent.Find(ContentKind.Npc, type) is { } content ? content.FullKey : "";

    private static int TypeOf(string key) =>
        string.IsNullOrEmpty(key) || !ModContent.TryFind<ModNpc>(key, out var npc) ? 0 : npc.Type;

    private static string? DataFile()
    {
        var path = Main.worldPathName;
        if (string.IsNullOrEmpty(path)) return null;

        if (path.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path + ".wld.npcs.json";
    }

    private static TagCompound ReadFile()
    {
        if (DataFile() is null) return new TagCompound();
        return WorldSidecar.Read();
    }
}
