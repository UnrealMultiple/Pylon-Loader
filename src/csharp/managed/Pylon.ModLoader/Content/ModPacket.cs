using Pylon.ModLoader.Pipelines;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>Mod 发的自定义网络包（Mod.GetPacket() 拿，本身是 BinaryWriter）；对端由同一个 mod 的 HandlePacket 接收。</summary>
public sealed class ModPacket : BinaryWriter
{
    public const int MaxLength = 65000;

    private readonly Mod mod;

    internal ModPacket(Mod mod, int capacity) : base(new MemoryStream(Math.Max(capacity, 16)))
    {
        this.mod = mod;
        Write(NetPipeline.KindPacket);
        Write(mod.Name);
    }

    public void Send(int toClient = -1, int ignoreClient = -1)
    {
        if (Main.netMode == 0)
            throw new InvalidOperationException($"{mod.Name}: 单人游戏不能发包");

        Flush();
        var stream = (MemoryStream)OutStream;
        if (stream.Length > MaxLength)
            throw new InvalidOperationException($"{mod.Name}: 包长 {stream.Length} 字节，上限 {MaxLength}");

        NetPipeline.Instance.SendRawPacket(stream.GetBuffer(), (int)stream.Length, toClient, ignoreClient);
    }
}
