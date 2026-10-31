namespace Pylon.ModLoader;

/// <summary>Mod 发的自定义网络包（Mod.GetPacket() 拿）；对端由同一个 mod 的 HandlePacket(reader, whoAmI) 接收。</summary>
public class ModPacket : NetWriter
{
    public ModPacket(Mod mod)
    {
        Mod = mod;
    }

    /// <summary>发出这个包的 Mod（构造时记下）。</summary>
    public Mod Mod { get; }

    /// <summary>发出去：客户端发给服务器；服务器发给 toClient（-1 = 广播给除 ignoreClient 外所有人）；单人游戏哪儿也不去。</summary>
    public void Send(int toClient = -1, int ignoreClient = -1) =>
        ModNet.SendPacket(this, toClient, ignoreClient);
}
