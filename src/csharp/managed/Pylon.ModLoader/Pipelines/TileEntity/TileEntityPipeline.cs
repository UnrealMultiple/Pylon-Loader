using Terraria;
using Terraria.ObjectData;

namespace Pylon.ModLoader;

/// <summary>方块实体管线：每帧分派、放置 / 摘除、多人同步、侧车存档；类型表在 TileEntityTable，实例绑定在 TileEntityBinding。</summary>
internal sealed class TileEntityPipeline : ILoaderModule, ILoaderModuleRefresh
{
    private const string SaveSection = "ents";
    private const int FirstId = 0x40000000;

    private const byte ActionEntity = 0, ActionKilled = 1, ActionRequestPlace = 2, ActionRequestKill = 3;

    public static TileEntityPipeline Instance { get; } = new();

    private readonly HashSet<long> _reported = [];

    private NetPipeline _net = null!;
    private byte _kind;

    public string Name => "tileentity";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        _net = NetPipeline.Instance;
        _kind = _net.OpenChannel(Receive);

        On.Terraria.DataStructures.TileEntity.PerformUpdates += Tick;
        On.Terraria.WorldGen.clearWorld += ClearWorldPostfix;
        On.Terraria.IO.WorldFile.LoadWorld += LoadWorld;
        On.Terraria.IO.WorldFile.InternalSaveWorld += SaveWorld;
        On.Terraria.NetMessage.SendSection += SendSection;
        On.Terraria.WorldGen.PlaceTile += PlaceTile;
        On.Terraria.ObjectData.TileObjectData.CallPostPlacementPlayerHook += CallPostPlacement;
        On.Terraria.WorldGen.KillTile += KillTile;
        On.Terraria.Player.PlaceThing_Tiles += Placing;

        Log.Info("[tileentity] 已挂（每帧 / 清世界 / 读档 / 存档 / 同步 / 放置即建实体 / 拆除即摘实体）");
        return true;
    }

    [ThreadStatic] private static int _placing;

    private static void Placing(On.Terraria.Player.orig_PlaceThing_Tiles orig, Player self, bool action)
    {
        _placing++;
        try
        {
            orig(self, action);
        }
        finally
        {
            _placing--;
        }
    }

    private static void Placed(int i, int j, int type)
    {
        var item = Main.LocalPlayer.HeldItem;

        if (TileTableOf(type) is { } tile && tile.HookEnabled(HookId.PlaceInWorld))
        {
            try
            {
                tile.PlaceInWorld(i, j, item);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tile] {tile.Name}.PlaceInWorld 异常：{Safe.Describe(e)}");
            }
        }

        foreach (var global in GlobalTileRegistry.For(HookId.PlaceInWorld))
        {
            try
            {
                global.PlaceInWorld(i, j, type, item);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tile] {global.FullKey}.PlaceInWorld 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static bool PlaceTile(On.Terraria.WorldGen.orig_PlaceTile orig, int i, int j, int Type, bool mute,
                                  bool forced, int plr, int style)
    {
        var placed = orig(i, j, Type, mute, forced, plr, style);

        if (!placed) return placed;

        if (_placing > 0 && (uint)Type < (uint)TileTable.Shapes.Length && TileTable.Shapes[Type] is null)
            Placed(i, j, Type);

        var direction = plr >= 0 && plr < Main.player.Length && Main.player[plr] is { } player
            ? player.direction
            : 0;

        if (TileEntityTable.Count == 0 || Main.netMode == 1) return placed;

        var count = TileEntityTable.Count;
        for (var index = 0; index < count; index++)
        {
            var template = TileEntityTable.Template(index);
            if (template is null) continue;

            if (TileEntityBinding.Find(template, i, j) >= 0) continue;

            bool valid;
            try
            {
                valid = template.IsTileValidForEntity(i, j);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tileentity] {template.Name}.IsTileValidForEntity 异常：{Safe.Describe(e)}");
                continue;
            }

            if (!valid) continue;

            var id = TileEntityBinding.Place(template, i, j);
            if (id >= 0) EntityPlaced(id, i, j, Type, style, direction, -1);
        }

        return placed;
    }

    private static void EntityPlaced(int id, int i, int j, int type, int style, int direction, int alternate)
    {
        if (TileEntityBinding.ByID(id) is not { } entity || !entity.Plan.Has(HookId.AfterPlacement)) return;

        try
        {
            _ = entity.Hook_AfterPlacement(i, j, type, style, direction, alternate);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tileentity] {entity.FullName}.Hook_AfterPlacement 异常：{Safe.Describe(e)}");
        }
    }

    private static bool CallPostPlacement(On.Terraria.ObjectData.TileObjectData.orig_CallPostPlacementPlayerHook orig,
                                          int i, int j, int type, int style, int direction, int alternate, TileObject data)
    {
        var placed = orig(i, j, type, style, direction, alternate, data);

        Placed(i, j, type);

        return placed;
    }

    private static ModTile? TileTableOf(int type) =>
        ModContent.Find(ContentKind.Tile, type) as ModTile;

    private static void KillTile(On.Terraria.WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly, bool noItem)
    {
        orig(i, j, fail, effectOnly, noItem);

        if (fail || effectOnly || TileEntityTable.Count == 0) return;
        if (TileEntityBinding.At(i, j) is not { } entity) return;

        Instance.KillEntity(entity);
    }

    public void Refresh()
    {
        TileEntityTable.Assign();
    }

    internal void Build()
    {
        TileEntityTable.Assign();

        var count = 0;
        for (var type = 0; type < TileEntityTable.Count; type++)
        {
            var template = TileEntityTable.Template(type);
            if (template is null) continue;

            Safe.Run(template.Name + ".SetStaticDefaults", template.SetStaticDefaults);
            Safe.Run(template.Name + ".Load", template.Load);
            count++;
        }

        if (count > 0) Log.Info($"[tileentity] 安装：{count} 个类型（ID 从 {FirstId} 起）");
    }

    internal int Place(ModTileEntity template, int i, int j)
    {
        if (!IsValid(template, i, j)) return -1;

        if (Main.netMode == 1)
        {
            Request(template, i, j, ActionRequestPlace);
            return -1;
        }

        var id = TileEntityBinding.Place(template, i, j);
        if (id < 0) return -1;

        if (Main.netMode == 2) SendEntity(id, -1);
        return id;
    }

    internal void Kill(ModTileEntity template, int i, int j)
    {
        if (Main.netMode == 1)
        {
            Request(template, i, j, ActionRequestKill);
            return;
        }

        var entity = TileEntityBinding.At(i, j);
        if (entity is null || entity.Type != template.Type) return;

        KillEntity(entity);
        if (Main.netMode == 2) SendKilled(entity.ID);
    }

    internal static int Find(ModTileEntity template, int i, int j) => TileEntityBinding.Find(template, i, j);

    private void KillEntity(ModTileEntity entity)
    {
        Call(entity, HookId.OnKill, static e => e.OnKill());
        TileEntityBinding.Remove(entity);
    }

    private void Tick(On.Terraria.DataStructures.TileEntity.orig_PerformUpdates orig)
    {
        var list = TileEntityBinding.All;
        if (list.Count == 0)
        {
            orig();
            return;
        }

        var count = list.Count;
        for (var i = 0; i < count && i < list.Count; i++)
            Call(list[i], HookId.PreGlobalUpdate, static e => e.PreGlobalUpdate());

        orig();

        count = list.Count;
        for (var i = 0; i < count && i < list.Count; i++)
        {
            var entity = list[i];
            if (!TileEntityTable.NeedsUpdate(entity.Type)) continue;
            if (!TileEntityBinding.IsLive(entity)) continue;

            Call(entity, HookId.Update, static e => e.Update());
        }

        count = list.Count;
        for (var i = 0; i < count && i < list.Count; i++)
            Call(list[i], HookId.PostGlobalUpdate, static e => e.PostGlobalUpdate());
    }

    private void Call(ModTileEntity entity, HookId hook, Action<ModTileEntity> call)
    {
        if (!TileEntityTable.MasksOf(entity.Type).Has(hook)) return;

        try
        {
            call(entity);
        }
        catch (Exception e)
        {
            Report(entity, hook.ToString(), e);
        }
    }

    private void Report(ModTileEntity entity, string what, Exception exception)
    {
        if (!_reported.Add(((long)entity.Type << 32) | (uint)what.GetHashCode())) return;
        ContentErrors.Report($"[tileentity] {entity.FullName}.{what} 异常：{Safe.Describe(exception)}");
    }

    private void SendEntity(int id, int toClient)
    {
        if (_net is null || Main.netMode == 0) return;

        var entity = TileEntityBinding.ByID(id);
        if (entity is null) return;

        var values = new NetWriter();
        Safe.Run(entity.FullName + ".NetSend", () => entity.NetSend(values));

        var key = TileEntityTable.KeyOf(entity.Type);
        var x = (short)entity.Position.X;
        var y = (short)entity.Position.Y;

        _net.SendChannel(_kind, writer =>
        {
            writer.Write(ActionEntity);
            writer.Write(id);
            writer.Write(x);
            writer.Write(y);
            writer.Write(key);
            NetCodec.WriteValues(writer, values.Values);
        }, toClient, -1);
    }

    private void SendKilled(int id)
    {
        if (_net is null) return;
        _net.SendChannel(_kind, writer =>
        {
            writer.Write(ActionKilled);
            writer.Write(id);
        }, -1, -1);
    }

    private void Request(ModTileEntity template, int i, int j, byte action)
    {
        if (_net is null) return;

        var key = TileEntityTable.KeyOf(template);
        var x = (short)i;
        var y = (short)j;

        _net.SendChannel(_kind, writer =>
        {
            writer.Write(action);
            writer.Write(key);
            writer.Write(x);
            writer.Write(y);
        }, -1, -1);
    }

    private void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        _ = from;

        switch (reader.ReadByte())
        {
            case ActionEntity: ApplyEntity(reader); break;
            case ActionKilled: ApplyKilled(reader); break;
            case ActionRequestPlace: ServerPlace(reader); break;
            case ActionRequestKill: ServerKill(reader); break;
        }
    }

    private void ApplyEntity(Il2CppSystem.IO.BinaryReader reader)
    {
        var id = reader.ReadInt32();
        var x = (int)reader.ReadInt16();
        var y = (int)reader.ReadInt16();
        var key = reader.ReadString();
        var values = NetCodec.ReadValues(reader);

        if (Main.netMode != 1) return;

        var entity = TileEntityBinding.ByID(id);
        if (entity is null)
        {
            var template = TileEntityTable.ByName(key);
            if (template is null) return;

            entity = TileEntityBinding.PlaceWithId(template, x, y, id);
        }

        if (entity is null) return;

        var payload = new NetReader(values);
        Safe.Run(entity.FullName + ".NetReceive", () => entity.NetReceive(payload));
    }

    private static void ApplyKilled(Il2CppSystem.IO.BinaryReader reader)
    {
        var id = reader.ReadInt32();
        if (Main.netMode != 1) return;

        var entity = TileEntityBinding.ByID(id);
        if (entity is not null) TileEntityBinding.Remove(entity);
    }

    private void ServerPlace(Il2CppSystem.IO.BinaryReader reader)
    {
        var key = reader.ReadString();
        var x = (int)reader.ReadInt16();
        var y = (int)reader.ReadInt16();

        if (Main.netMode != 2) return;

        var template = TileEntityTable.ByName(key);
        if (template is null || !IsValid(template, x, y)) return;

        var id = TileEntityBinding.Place(template, x, y);
        if (id < 0) return;

        var entity = TileEntityBinding.ByID(id);
        if (entity is not null) Call(entity, HookId.OnNetPlace, static e => e.OnNetPlace());

        SendEntity(id, -1);
    }

    private void ServerKill(Il2CppSystem.IO.BinaryReader reader)
    {
        var key = reader.ReadString();
        var x = (int)reader.ReadInt16();
        var y = (int)reader.ReadInt16();

        if (Main.netMode != 2) return;

        var template = TileEntityTable.ByName(key);
        if (template is null) return;

        var entity = TileEntityBinding.At(x, y);
        if (entity is null || entity.Type != template.Type) return;

        var id = entity.ID;
        KillEntity(entity);
        SendKilled(id);
    }

    private void SendSection(On.Terraria.NetMessage.orig_SendSection orig, int whoAmi, int sectionX, int sectionY)
    {
        orig(whoAmi, sectionX, sectionY);

        if (Main.netMode != 2 || TileEntityBinding.Count == 0) return;

        var left = sectionX * 200;
        var top = sectionY * 150;
        var all = TileEntityBinding.All;

        for (var i = 0; i < all.Count; i++)
        {
            var x = all[i].Position.X;
            var y = all[i].Position.Y;
            if (x < left || x >= left + 200 || y < top || y >= top + 150) continue;

            SendEntity(all[i].ID, whoAmi);
        }
    }

    private static void ClearWorldPostfix(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        orig();
        TileEntityBinding.Clear();
    }

    private static void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        orig(canCheckFileState);
        TileEntityBinding.Clear();
        Load();
    }

    private static void Load()
    {
        if (DataFile() is not { } file) return;

        var entries = TagJson.Read(file).GetList(SaveSection);
        var loaded = 0;
        var dropped = 0;

        foreach (var node in entries)
        {
            if (node is not TagCompound entry) continue;

            var template = TileEntityTable.ByName(entry.GetString("t"));
            if (template is null)
            {
                dropped++;
                continue;
            }

            var x = entry.GetInt("x");
            var y = entry.GetInt("y");
            if (!IsValid(template, x, y))
            {
                dropped++;
                continue;
            }

            var id = TileEntityBinding.Place(template, x, y);
            var entity = id < 0 ? null : TileEntityBinding.ByID(id);
            if (entity is null)
            {
                dropped++;
                continue;
            }

            var data = entry.Get("d");
            var tag = data as TagCompound ?? TagCompound.From(data);
            Safe.Run(entity.FullName + ".LoadData", () => entity.LoadData(tag));
            loaded++;
        }

        if (loaded > 0 || dropped > 0) Log.Info($"[tileentity] 读档：{loaded} 个，丢弃 {dropped} 个");
    }

    private static bool IsValid(ModTileEntity template, int x, int y)
    {
        if (!WorldGen.InWorld(x, y)) return false;

        try
        {
            return template.IsTileValidForEntity(x, y);
        }
        catch (Exception e)
        {
            Safe.Report(template.Name + ".IsTileValidForEntity", e);
            return false;
        }
    }

    private static void SaveWorld(On.Terraria.IO.WorldFile.orig_InternalSaveWorld orig, bool useCloudSaving,
                                  bool resetTime)
    {
        orig(useCloudSaving, resetTime);
        if (useCloudSaving) return;
        Save();
    }

    private static void Save()
    {
        if (DataFile() is not { } file) return;

        var root = TagJson.Read(file);
        var all = TileEntityBinding.All;
        if (all.Count == 0 && !root.ContainsKey(SaveSection)) return;

        var entries = new List<object?>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            var entity = all[i];
            var entry = new TagCompound
            {
                ["t"] = TileEntityTable.KeyOf(entity.Type),
                ["x"] = (int)entity.Position.X,
                ["y"] = (int)entity.Position.Y,
            };

            var data = new TagCompound();
            Safe.Run(entity.FullName + ".SaveData", () => entity.SaveData(data));
            if (data.Count > 0) entry["d"] = data;

            entries.Add(entry);
        }

        if (entries.Count > 0) root.Set(SaveSection, entries);
        else root.Remove(SaveSection);

        try
        {
            TagJson.Write(file, root);
        }
        catch (Exception e)
        {
            Safe.Report("tileentity.save", e);
        }
    }

    private static string? DataFile()
    {
        var path = Main.worldPathName;
        if (string.IsNullOrEmpty(path)) return null;

        if (path.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path + ".wld.tileentities.json";
    }
}
