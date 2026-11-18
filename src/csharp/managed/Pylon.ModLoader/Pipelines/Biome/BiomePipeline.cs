using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>生物群系管线：场景扫描后算"在不在每个群落里"，边沿触发进/出回调。</summary>
internal sealed class BiomePipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static BiomePipeline Instance { get; } = new();

    private const byte ActionFlag = 0, ActionState = 1;

    private const int WorldData = 7;

    private bool[][] _flags = [];

    private NetPipeline _net = null!;
    private byte _kind;

    public string Name => "biome";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        _net = NetPipeline.Instance;
        _kind = _net.OpenChannel(Receive);

        On.Terraria.SceneMetrics.Scan += Scan;
        On.Terraria.Player.Update += UpdateOther;
        On.Terraria.IO.WorldFile.LoadWorld += LoadWorld;
        On.Terraria.WorldGen.SaveAndQuit += SaveAndQuit;
        On.Terraria.WorldGen.clearWorld += ClearWorld;
        On.Terraria.NetMessage.SendData += SendData;

        Log.Info("[biome] 已挂（扫描后判定 / 进-在-出回调 / 别人的群落走网络）；音乐与背景归场景效果管线");
        return true;
    }

    public void Refresh()
    {
        BiomeTable.Assign();
        EnsureFlags();
    }

    internal void Build()
    {
        foreach (var biome in ModContent.GetContent<ModBiome>())
        {
            if (!biome.Registered) continue;
            biome.SetStaticDefaults();
            biome.BestiaryElement = BiomeBestiaryHooks.Create(biome);
        }

        EnsureFlags();

        if (BiomeTable.Count > 0) Log.Info($"[biome] 安装：{BiomeTable.Count} 个群落");
    }

    internal bool IsActive(Player player, ModBiome biome)
    {
        var who = player.whoAmI;
        if ((uint)who >= (uint)_flags.Length) return false;

        var index = biome.Type;
        var flags = _flags[who];
        return (uint)index < (uint)flags.Length && flags[index];
    }

    private void EnsureFlags()
    {
        var total = BiomeTable.Count;

        if (_flags.Length != Main.maxPlayers) _flags = new bool[Main.maxPlayers][];

        for (var who = 0; who < _flags.Length; who++)
        {
            if (_flags[who] is { } row && row.Length == total) continue;
            _flags[who] = new bool[total];
        }
    }

    private void Scan(On.Terraria.SceneMetrics.orig_Scan orig, SceneMetrics self, SceneMetricsScanSettings settings)
    {
        orig(self, settings);

        var total = BiomeTable.Count;
        if (total == 0) return;

        var who = Main.myPlayer;
        if ((uint)who >= (uint)_flags.Length) return;

        var players = Main.player;
        if (players is null || (uint)who >= (uint)players.Length) return;

        if (players[who] is not { } player || !player.active) return;

        Update(player, who, total);
    }

    private void UpdateOther(On.Terraria.Player.orig_Update orig, Player self, int i)
    {
        orig(self, i);

        if (self.active) return;
        if ((uint)i < (uint)_flags.Length) Array.Clear(_flags[i]);
    }

    private void Update(Player player, int who, int total)
    {
        var flags = _flags[who];

        for (var index = 0; index < total; index++)
        {
            if (BiomeTable.At(index) is not { } biome) continue;

            var before = index < flags.Length && flags[index];

            bool now;
            try
            {
                now = biome.IsBiomeActive(player);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[biome] {biome.Name}.IsBiomeActive 异常：{Safe.Describe(e)}");
                now = false;
            }

            if (index < flags.Length) flags[index] = now;

            if (now != before && who == Main.myPlayer && Main.netMode == 1)
                _net.SendValues(_kind, [ActionFlag, who, index, now], -1, -1);

            if (now && !before) Enter(biome, player);
            if (!now && before) Leave(biome, player);
            if (now) Inside(biome, player);
        }
    }

    private static void Enter(ModBiome biome, Player player)
    {
        try
        {
            biome.OnEnter(player);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[biome] {biome.Name}.OnEnter 异常：{Safe.Describe(e)}");
        }
    }

    private static void Inside(ModBiome biome, Player player)
    {
        try
        {
            biome.OnInBiome(player);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[biome] {biome.Name}.OnInBiome 异常：{Safe.Describe(e)}");
        }
    }

    private static void Leave(ModBiome biome, Player player)
    {
        try
        {
            biome.OnLeave(player);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[biome] {biome.Name}.OnLeave 异常：{Safe.Describe(e)}");
        }
    }

    private void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var action = reader.ReadByte();
        var who = reader.ReadInt32();
        var index = reader.ReadInt32();
        var enter = reader.ReadBoolean();

        if (action is not (ActionFlag or ActionState)) return;
        if ((uint)who >= (uint)Main.player.Length) return;
        if ((uint)index >= (uint)BiomeTable.Count) return;

        if (Main.netMode == 2)
        {
            if (who != from) return;
            _net.SendValues(_kind, [action, who, index, enter], -1, from);
        }

        Apply(who, index, enter, action == ActionFlag);
    }

    private void Apply(int who, int index, bool enter, bool fire)
    {
        if ((uint)who >= (uint)_flags.Length) return;

        var flags = _flags[who];
        if ((uint)index >= (uint)flags.Length) return;
        if (flags[index] == enter) return;

        flags[index] = enter;
        if (!fire) return;

        if (BiomeTable.At(index) is not { } biome) return;

        var player = Main.player[who];
        if (player is null || !player.active) return;

        if (enter) Enter(biome, player);
        else Leave(biome, player);
    }

    private void SendData(On.Terraria.NetMessage.orig_SendData orig, int msgType, int remoteClient, int ignoreClient,
                          Terraria.Localization.NetworkText text, int number, float number2, float number3,
                          float number4, int number5, int number6, int number7)
    {
        orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);

        if (msgType != WorldData) return;
        if (Main.netMode != 2 || remoteClient < 0 || remoteClient >= 255) return;

        for (var who = 0; who < _flags.Length; who++)
        {
            if (who == remoteClient) continue;

            var flags = _flags[who];
            for (var index = 0; index < flags.Length; index++)
            {
                if (!flags[index]) continue;
                _net.SendValues(_kind, [ActionState, who, index, true], remoteClient, -1);
            }
        }
    }

    private void SaveAndQuit(On.Terraria.WorldGen.orig_SaveAndQuit orig)
    {
        LeaveAll();
        orig();
    }

    private void ClearWorld(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        orig();

        for (var who = 0; who < _flags.Length; who++) Array.Clear(_flags[who]);
    }

    private void LeaveAll()
    {
        var who = Main.myPlayer;
        if ((uint)who >= (uint)_flags.Length) return;

        var players = Main.player;
        if (players is null || (uint)who >= (uint)players.Length) return;
        if (players[who] is not { } player) return;

        var flags = _flags[who];
        for (var index = 0; index < flags.Length; index++)
        {
            if (!flags[index]) continue;

            flags[index] = false;
            if (BiomeTable.At(index) is { } biome) Leave(biome, player);
        }

        for (var other = 0; other < _flags.Length; other++)
        {
            if (other != who) Array.Clear(_flags[other]);
        }
    }

    private void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        orig(canCheckFileState);

        var who = Main.myPlayer;
        if ((uint)who >= (uint)_flags.Length) return;

        var players = Main.player;
        if (players is null || (uint)who >= (uint)players.Length) return;
        if (players[who] is not { } player) return;

        var flags = _flags[who];
        for (var index = 0; index < flags.Length; index++)
        {
            if (!flags[index]) continue;

            flags[index] = false;
            if (BiomeTable.At(index) is { } biome) Leave(biome, player);
        }

        for (var other = 0; other < _flags.Length; other++)
        {
            if (other != who) Array.Clear(_flags[other]);
        }
    }
}
