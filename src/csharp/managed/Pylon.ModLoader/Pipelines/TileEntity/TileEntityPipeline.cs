using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 方块实体管线：每帧分派、放置 / 摘除、多人同步、侧车存档。
/// 类型表在 <see cref="TileEntityTable"/>，实例绑定在 <see cref="TileEntityBinding"/>。
/// </summary>
internal sealed class TileEntityPipeline : ILoaderModule, ILoaderModuleRefresh
{
    private const string SaveSection = "ents";
    private const int FirstId = 0x40000000;

    private const byte ActionEntity = 0, ActionKilled = 1, ActionRequestPlace = 2, ActionRequestKill = 3;

    public static TileEntityPipeline Instance { get; } = new();

    private readonly HashSet<long> _reported = [];

    /// <summary>每个类型的定时同步间隔（帧，0 = 不定时）；索引 = 类型号。</summary>
    private int[] _intervals = [];

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
        On.Terraria.WorldGen.KillTile += KillTile;

        Log.Info("[tileentity] 已挂（每帧 / 清世界 / 读档 / 存档 / 同步 / 放置即建实体 / 拆除即摘实体）");
        return true;
    }

    // ===================== 放置 / 拆除 =====================

    /// <summary>
    /// 方块放下之后：<see cref="ModTile.TileEntity"/> 指了实体名的就建那个实体（一块一类，作者的声明说了算）；
    /// 没指的回落到老规则——凡是 <c>IsTileValidForEntity</c> 认这块位置的模板，就地建实体。
    /// 客户端不建：权威在服务器，等广播（与 <c>Place</c> 的语义一致）。
    /// </summary>
    private static bool PlaceTile(On.Terraria.WorldGen.orig_PlaceTile orig, int i, int j, int Type, bool mute,
                                  bool forced, int plr, int style)
    {
        var placed = orig(i, j, Type, mute, forced, plr, style);

        if (!placed) return placed;

        // 作者的放置回调：不管建不建实体都要跑（作者可能只是想记一笔）。
        if (TileTableOf(Type) is { } placer && placer.HookEnabled(HookId.AfterPlacement))
        {
            var direction = plr >= 0 && plr < Main.player.Length && Main.player[plr] is { } player
                ? player.direction
                : 0;

            try
            {
                _ = placer.Hook_AfterPlacement(i, j, Type, style, direction, -1);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tileentity] {placer.Name}.Hook_AfterPlacement 异常：{Safe.Describe(e)}");
            }
        }

        if (TileEntityBinding.Count == 0 || Main.netMode == 1) return placed;

        // 声明优先：ModTile.TileEntity 指了名字，就只建那一个。
        if (TileTableOf(Type) is { } tile && tile.TileEntity is { } wanted)
        {
            var named = TileEntityBinding.ByName(tile.Mod?.Name ?? "", wanted);
            if (named is null)
            {
                ContentErrors.Report($"[tileentity] 方块 {tile.Name} 声明的实体 \"{wanted}\" 不存在（要写同类名）");
            }
            else if (TileEntityBinding.Find(named, i, j) < 0)
            {
                TileEntityBinding.Place(named, i, j);
            }

            return placed;
        }

        var all = TileEntityBinding.All;
        for (var index = 0; index < all.Count; index++)
        {
            var template = all[index];

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

            TileEntityBinding.Place(template, i, j);
        }

        return placed;
    }

    /// <summary>类型号 → 我们的 ModTile（不是我们的就 null）。</summary>
    private static ModTile? TileTableOf(int type) =>
        ModContent.Find(ContentKind.Tile, type) as ModTile;

    /// <summary>方块被拆掉之后：那块位置上的实体一并摘掉（<c>fail</c> = 只是敲了一下，没拆）。</summary>
    private static void KillTile(On.Terraria.WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly, bool noItem)
    {
        orig(i, j, fail, effectOnly, noItem);

        if (fail || effectOnly || TileEntityBinding.Count == 0) return;
        if (TileEntityBinding.At(i, j) is not { } entity) return;

        TileEntityBinding.Remove(entity);
    }

    /// <summary>新内容发号：每次 Mod 注册后都要跟上，否则作者的 SetupContent 读不到 ID。</summary>
    public void Refresh()
    {
        TileEntityTable.Assign();
        RebuildIntervals();
    }

    /// <summary>把每个类型声明的同步间隔抄进侧表：每帧循环里就只是一次数组读，不再问作者。</summary>
    private void RebuildIntervals()
    {
        var count = TileEntityTable.Count;
        if (_intervals.Length < count) _intervals = new int[Math.Max(count, 8)];

        for (var type = 0; type < count; type++)
        {
            var template = TileEntityTable.Template(type);
            var interval = template is null ? 0 : template.NetUpdateInterval;
            _intervals[type] = interval > 0 ? interval : 0;
        }
    }

    /// <summary>安装期：每个模板的 SetStaticDefaults 与 Load 各一次。</summary>
    internal void Build()
    {
        TileEntityTable.Assign();
        RebuildIntervals();

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

    // ===================== 作者 API 的落点 =====================

    /// <summary>
    /// 放进世界。客户端只发请求（合法性由服务器判），服务器放完广播给所有人。
    /// 返回新实体的 ID；客户端与越界 / 已被占用时 -1。
    /// </summary>
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

    /// <summary>摘掉 (i, j) 上本类型的实体；客户端只发请求，等服务器广播。</summary>
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

    // ===================== 每帧 =====================

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

        // 这一帧新放的实体也要收 PostGlobalUpdate，所以这里重取长度。
        count = list.Count;
        for (var i = 0; i < count && i < list.Count; i++)
            Call(list[i], HookId.PostGlobalUpdate, static e => e.PostGlobalUpdate());

        if (Main.netMode == 2) PeriodicSync();
    }

    /// <summary>
    /// 定时把声明了 <see cref="ModTileEntity.NetUpdateInterval"/> 的实体重新发一遍（服务器是唯一权威）。
    /// 按"帧号 % 间隔 == 实体 ID % 间隔"错开发送：同一帧不会把所有实体压成一串包，也不需要每个实体记计时器。
    /// </summary>
    private void PeriodicSync()
    {
        var all = TileEntityBinding.All;
        if (all.Count == 0) return;

        var frame = Main.GameUpdateCount;

        for (var i = 0; i < all.Count; i++)
        {
            var entity = all[i];

            var type = entity.Type;
            if ((uint)type >= (uint)_intervals.Length) continue;

            var interval = _intervals[type];
            if (interval <= 0) continue;

            if (frame % (ulong)interval != (ulong)(entity.ID & 0x7FFFFFFF) % (ulong)interval) continue;

            SendEntity(entity.ID, -1);
        }
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

    // ===================== 网络 =====================

    /// <summary>服务器 → 所有人（或某个人）发一个实体：ID + 左上角 + 类型键 + 作者的载荷。</summary>
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

    /// <summary>客户端 → 服务器：请放下 / 请摘掉（连键一起带，服务器用同一套表反查模板）。</summary>
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

    /// <summary>客户端收实体：已有同 ID 就只补数据，没有就按服务器给的 ID 新建。</summary>
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

    /// <summary>服务器收放置请求：格子合法性以服务器为准，没人能靠伪造包在这里放东西。</summary>
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

    /// <summary>新玩家进到某一段世界时，把这一段里的实体补发给他（否则他要等实体下次发数据才知道有这东西）。</summary>
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

    // ===================== 存档 =====================

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

    /// <summary>方块实体的侧车存档：与 Mod 的世界数据分文件（那是按 Mod 分键的私有数据，这是跨 Mod 的位置表）。</summary>
    private static string? DataFile()
    {
        var path = Main.worldPathName;
        return string.IsNullOrEmpty(path) ? null : path + ".wld.tileentities.json";
    }
}
