using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 城镇 NPC 与房间存档：原版 .wld 只认它自己的类型号，mod 城镇 NPC 与房间一读档就消失。
/// 存：SaveNPCs 窗口内摘 mod NPC 的 townNPC、InternalSaveWorld 前把 mod 房间从 TownManager 摘掉
///（.wld 里不出现 mod 类型号，避免读档双份/孤儿条目），
/// 后写 <c>&lt;world&gt;.wld.npcs.json</c>（NPC 行 + 房间行 + mod 缺席的等待行）。
/// 读：LoadNPCs 后缀按 FullKey 复活 NPC，LoadTownManager 后缀把房间行还给 TownManager（它先清空自己）。
/// mod 缺席的行进等待名单，随下次保存原样回写，mod 回来后下次读档恢复。
/// </summary>
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

    // ===================== 保存 =====================

    private void SaveWorld(On.Terraria.IO.WorldFile.orig_InternalSaveWorld orig, bool useCloudSaving, bool resetTime)
    {
        // 云存档不做房间摘除：侧车没有落点，摘了就没有恢复时机。
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

    /// <summary>
    /// 存档窗口内把 mod 城镇 NPC 的 townNPC 摘掉：原版 SaveNPCs 按 townNPC 把 NPC 行写进 .wld，
    /// mod 类型号进档 = 装着 mod 读档双份（侧车还会再复活一份）、卸 mod 留孤儿行。窗口就是 SaveNPCs 本身。
    /// 云存档不摘：侧车没有落点，摘了就没有恢复时机。
    /// </summary>
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

    /// <summary>mod 城镇 NPC 的房间摘出来进侧车；没有房间的 mod NPC 自然不产生房间行。</summary>
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
        if (DataFile() is not { } file) return;

        var root = new TagCompound();

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

            npcs.Add(row);
        }

        var roomRows = new List<object?>();
        if (rooms is not null) roomRows.AddRange(rooms);
        if (_keptRooms is not null) roomRows.AddRange(_keptRooms);

        // mod 缺席的等待行原样回写（mod 回来后下次读档恢复）。
        if (_keptNpcs is not null) npcs.AddRange(_keptNpcs);
        if (npcs.Count > 0) root.Set(NpcSection, npcs);
        if (roomRows.Count > 0) root.Set(RoomSection, roomRows);

        try
        {
            if (root.Count > 0) TagJson.Write(file, root);
            else if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception e)
        {
            Safe.Report("npc 存档", e);
        }

        // 等待行常驻内存（每次保存只读合并，同会话二次保存不丢），只在读档时重建。
    }

    // ===================== 读取 =====================

    private void LoadNpcs(On.Terraria.IO.WorldFile.orig_LoadNPCs orig, Il2CppSystem.IO.BinaryReader reader)
    {
        orig(reader);

        // 等待名单按次读档重建：跨世界不带走（行本体在各自世界的侧车里）。
        _keptNpcs = null;
        _keptRooms = null;
        _pendingRooms = [];

        var root = ReadFile();
        if (root.Count == 0) return;

        var rows = root.GetList(NpcSection);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not TagCompound row) continue;
            if (Restore(row)) continue;

            _keptNpcs ??= [];
            _keptNpcs.Add(row);
        }

        var rooms = root.GetList(RoomSection);
        for (var i = 0; i < rooms.Count; i++)
        {
            if (rooms[i] is TagCompound room) _pendingRooms.Add(room);
        }
    }

    /// <summary>LoadTownManager 会清空自己的房间表：房间行等它读完再还回去。</summary>
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

    /// <summary>找一个空闲 NPC 槽恢复；mod 缺席返回 false（行进等待名单）。</summary>
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
            return true;
        }
        catch (Exception e)
        {
            Safe.Report("npc 恢复", e);
            return false;
        }
    }

    // ===================== 公共件 =====================

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
        // 最后一位是游戏的保留槽，不占。
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

    /// <summary>NPC 侧车路径：与方块实体侧车同款命名（手机版 worldPathName 自带 .wld 后缀要剥掉）。</summary>
    private static string? DataFile()
    {
        var path = Main.worldPathName;
        if (string.IsNullOrEmpty(path)) return null;

        if (path.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path + ".wld.npcs.json";
    }

    private static TagCompound ReadFile()
    {
        if (DataFile() is not { } file || !File.Exists(file)) return new TagCompound();

        try
        {
            return TagJson.Read(file);
        }
        catch (Exception e)
        {
            Safe.Report("npc 读档", e);
            return new TagCompound();
        }
    }
}
