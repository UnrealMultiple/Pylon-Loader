using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>自然生成的选点上下文、权重池与生成通知;客户端不参与作者抽选。</summary>
internal static class NpcSpawn
{
    private static bool _installed;
    private sealed class State { internal NPCSpawnInfo? Info; }
    private static readonly Dictionary<long, State> States = [];
    [ThreadStatic] private static NPCSpawnInfo? _current;
    [ThreadStatic] private static HashSet<long>? _notified;
    private static int _globalCount = -1, _npcCount = -1;
    private static bool _wantsRange, _wantsRate, _wantsPool;

    private static void Refresh()
    {
        var globals = GlobalNpcRegistry.All;
        var npcs = ModContent.GetContent<ModNpc>();
        if (_globalCount == globals.Count && _npcCount == npcs.Count) return;
        _globalCount = globals.Count;
        _npcCount = npcs.Count;
        _wantsRange = globals.Any(g => Hooks.Overrides(g.GetType(), typeof(GlobalNpc), nameof(GlobalNpc.EditSpawnRange)));
        _wantsRate = globals.Any(g => Hooks.Overrides(g.GetType(), typeof(GlobalNpc), nameof(GlobalNpc.EditSpawnRate)));
        _wantsPool = globals.Any(g => Hooks.Overrides(g.GetType(), typeof(GlobalNpc), nameof(GlobalNpc.EditSpawnPool))) ||
            npcs.Any(n => n.GetType().GetMethods().Any(m => m.Name == nameof(ModNpc.SpawnChance) && m.DeclaringType != typeof(ModNpc)));
    }

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.WorldGen.clearWorld += ClearWorld;
        On.Terraria.NPC.Spawner.SetSpawnFlags += SetFlags;
        On.Terraria.NPC.Spawner.SetSpawnFlagsForChosenTile += ChosenTile;
        On.Terraria.NPC.Spawner.GetSpawnRate += Rate;
        On.Terraria.NPC.Spawner.GetSpawnArea += Area;
        On.Terraria.NPC.Spawner.SpawnAnNPC += Spawn;
        On.Terraria.NPC.Spawner.SpawnNPC_int_int_int_int_float_float_float_float_int += Spawned;
    }

    private static void ClearWorld(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        States.Clear();
        _current = null;
        _notified = null;
        orig();
    }

    internal static NPCSpawnInfo InfoOf(NPC.Spawner spawner)
    {
        ArgumentNullException.ThrowIfNull(spawner);
        if (_current is { } current && current.Spawner.Pointer == spawner.Pointer) return current;
        if (States.TryGetValue((long)spawner.Pointer, out var state) && state.Info is { } info) return info;
        return new NPCSpawnInfo(spawner, Target(spawner.defaultTarget), -1, -1, -1, -1, -1);
    }

    private static Player Target(int target)
    {
        var players = Main.player;
        var index = target >= 0 && target < players.Length ? target : Main.myPlayer;
        if (index < 0 || index >= players.Length) index = 0;
        return players[index];
    }

    private static void Store(NPCSpawnInfo info)
    {
        var key = (long)info.Spawner.Pointer;
        if (!States.TryGetValue(key, out var state)) States[key] = state = new State();
        state.Info = info;
    }

    private static void SetFlags(On.Terraria.NPC.Spawner.orig_SetSpawnFlags orig, NPC.Spawner self, Player player)
    {
        orig(self, player);
        if (Main.netMode == 1) return;
        var outer = _current;
        var info = new NPCSpawnInfo(self, player, -1, -1, -1, -1, -1);
        _current = info;
        Store(info);
        try
        {
            foreach (var global in GlobalNpcRegistry.All)
            {
                try { global.EditSpawnFlags(self); }
                catch (Exception e) { Report(global, nameof(GlobalNpc.EditSpawnFlags), e); }
            }
        }
        finally
        {
            _current = outer;
            if (outer is not null && outer.Spawner.Pointer == self.Pointer) Store(outer);
        }
    }

    private static void ChosenTile(On.Terraria.NPC.Spawner.orig_SetSpawnFlagsForChosenTile orig, NPC.Spawner self,
                                   int x, int y, int groundY, int tileType, int wallType)
    {
        orig(self, x, y, groundY, tileType, wallType);
        if (Main.netMode == 1) return;
        var outer = _current;
        var info = new NPCSpawnInfo(self, Target(self.defaultTarget), x, y, groundY, tileType, wallType);
        _current = info;
        Store(info);
        try
        {
            foreach (var global in GlobalNpcRegistry.All)
            {
                try { global.EditSpawnInfo(self); }
                catch (Exception e) { Report(global, nameof(GlobalNpc.EditSpawnInfo), e); }
            }
        }
        finally
        {
            _current = outer;
            if (outer is not null && outer.Spawner.Pointer == self.Pointer) Store(outer);
        }
    }

    private static void Rate(On.Terraria.NPC.Spawner.orig_GetSpawnRate orig, NPC.Spawner self, Player player,
                             ref int spawnRate, ref int maxSpawns)
    {
        orig(self, player, ref spawnRate, ref maxSpawns);
        if (Main.netMode == 1) return;
        Refresh();
        if (!_wantsRate) return;
        foreach (var global in GlobalNpcRegistry.All)
        {
            try { global.EditSpawnRate(player, ref spawnRate, ref maxSpawns); }
            catch (Exception e) { Report(global, nameof(GlobalNpc.EditSpawnRate), e); }
        }
        spawnRate = Math.Max(1, spawnRate);
    }

    private static void Area(On.Terraria.NPC.Spawner.orig_GetSpawnArea orig, Player player,
                             ref Rectangle spawnArea, ref Rectangle safeArea)
    {
        orig(player, ref spawnArea, ref safeArea);
        if (Main.netMode == 1) return;
        Refresh();
        if (!_wantsRange) return;
        var x = (int)(NPC.sWidth / 16 * 0.7);
        var y = (int)(NPC.sHeight / 16 * 0.7);
        var held = player.inventory[player.selectedItem].type;
        if (held == 1254 || held == 1299 || player.scope)
        {
            var divisor = held == 1254 && player.scope ? 1.25 : held == 1254 || held == 1299 ? 1.5 : 2;
            x += (int)(NPC.sWidth / 16 * 0.5 / divisor);
            y += (int)(NPC.sHeight / 16 * 0.5 / divisor);
        }
        var sx = NPC.safeRangeX;
        var sy = NPC.safeRangeY;
        foreach (var global in GlobalNpcRegistry.All)
        {
            try { global.EditSpawnRange(player, ref x, ref y, ref sx, ref sy); }
            catch (Exception e) { Report(global, nameof(GlobalNpc.EditSpawnRange), e); }
        }
        x = Math.Clamp(x, 1, Main.maxTilesX);
        y = Math.Clamp(y, 1, Main.maxTilesY);
        NPC.safeRangeX = sx = Math.Clamp(sx, 0, Main.maxTilesX);
        NPC.safeRangeY = sy = Math.Clamp(sy, 0, Main.maxTilesY);
        var cx = (int)Math.Floor(player.position.X / 16f);
        var cy = (int)Math.Floor(player.position.Y / 16f);
        var left = Math.Clamp(cx - x, 0, Main.maxTilesX);
        var right = Math.Clamp(cx + x, 0, Main.maxTilesX);
        var top = Math.Clamp(cy - y, 0, Main.maxTilesY);
        var bottom = Math.Clamp(cy + y, 0, Main.maxTilesY);
        spawnArea = new Rectangle(left, top, right - left, bottom - top);
        var half = Main.dualDungeonsSeed && !player.ZoneOverworldHeight && !player.ZoneSkyHeight;
        safeArea = new Rectangle(cx - (half ? sx / 2 : sx), cy - (half ? sy / 2 : sy),
                                 sx * (half ? 1 : 2), sy * (half ? 1 : 2));
    }

    private static void Spawn(On.Terraria.NPC.Spawner.orig_SpawnAnNPC orig, NPC.Spawner self,
                              int x, int y, int tileType, int wallType, bool xRange, int target)
    {
        if (Main.netMode == 1) { orig(self, x, y, tileType, wallType, xRange, target); return; }
        var outer = _current;
        var outerNotified = _notified;
        var priorState = States.TryGetValue((long)self.Pointer, out var previous) ? previous.Info : null;
        NPC.Spawner.FindGroundTile(x, y, out var groundY);
        var info = new NPCSpawnInfo(self, Target(target), x, y, groundY, tileType, wallType, xRange);
        _current = info;
        _notified = [];
        Store(info);
        var conditions = SpawnCondition.Capture();
        try
        {
            Refresh();
            if (!_wantsPool) { orig(self, x, y, tileType, wallType, xRange, target); return; }
            var chosen = Choose(info);
            if (chosen == 0) orig(self, x, y, tileType, wallType, xRange, target);
            else if (chosen.HasValue) SpawnChosen(chosen.Value, info);
        }
        finally
        {
            SpawnCondition.Restore(conditions);
            _current = outer;
            _notified = outerNotified;
            // 同一生成器可能在作者回调中重入;嵌套结束后不能覆盖外层选点信息。
            if (outer is not null && outer.Spawner.Pointer == self.Pointer) Store(outer);
            else if (priorState is not null) Store(priorState);
        }
    }

    private static int? Choose(NPCSpawnInfo info)
    {
        if (info.SpawnTileX < 0 || info.SpawnTileY < 0) return null;
        SpawnCondition.Begin(info);
        var pool = new SpawnPool { [0] = 1f };
        foreach (var item in ModContent.GetContent<ModNpc>())
        {
            try
            {
                var weight = item.SpawnChance(info);
                if (float.IsFinite(weight) && weight > 0) pool[item.Type] = weight;
            }
            catch (Exception e) { Report(item, nameof(ModNpc.SpawnChance), e); }
        }
        foreach (var global in GlobalNpcRegistry.All)
        {
            try { global.EditSpawnPool(pool, info); }
            catch (Exception e) { Report(global, nameof(GlobalNpc.EditSpawnPool), e); }
        }
        return pool.Choose();
    }

    private static void SpawnChosen(int type, NPCSpawnInfo info)
    {
        int index;
        try
        {
            index = ModContent.Find(ContentKind.Npc, type) is ModNpc item
                ? item.SpawnNPC(info.SpawnTileX, info.SpawnTileY)
                : NPC.NewNPC(NPC.GetSpawnSourceForNaturalSpawn(), info.SpawnTileX * 16 + 8,
                             info.SpawnTileY * 16, type, 0, 0f, 0f, 0f, 0f, 255);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[npcspawn] NPC {type}.SpawnNPC 异常:{Safe.Describe(e)}");
            return;
        }
        var npcs = Main.npc;
        if (index >= 0 && index < npcs.Length && npcs[index] is { active: true } npc)
            Notify(npc, info.SpawnTileX, info.SpawnTileY);
    }

    private static NPC Spawned(On.Terraria.NPC.Spawner.orig_SpawnNPC_int_int_int_int_float_float_float_float_int orig,
                               NPC.Spawner self, int x, int y, int type, int start,
                               float ai0, float ai1, float ai2, float ai3, int target)
    {
        var npc = orig(self, x, y, type, start, ai0, ai1, ai2, ai3, target);
        if (Main.netMode != 1 && npc is { active: true })
        {
            var info = InfoOf(self);
            Notify(npc, info.SpawnTileX >= 0 ? info.SpawnTileX : x / 16,
                   info.SpawnTileY >= 0 ? info.SpawnTileY : y / 16);
        }
        return npc;
    }

    private static void Notify(NPC npc, int x, int y)
    {
        // 自定义生成器既可能经过 Spawner.SpawnNPC 又返回同一实体,只通知一次。
        if (_notified is not null && !_notified.Add((long)npc.Pointer)) return;
        foreach (var global in GlobalNpcRegistry.Of(npc))
        {
            try { global.SpawnNPC(npc.whoAmI, x, y); }
            catch (Exception e) { Report(global, nameof(GlobalNpc.SpawnNPC), e); }
        }
    }

    private static void Report(ModType author, string hook, Exception e) =>
        ContentErrors.Report($"[npcspawn] {author.Name}.{hook} 异常:{Safe.Describe(e)}");
}
