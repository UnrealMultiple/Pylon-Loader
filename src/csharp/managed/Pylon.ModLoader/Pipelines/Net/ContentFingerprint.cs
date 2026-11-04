using System.Text;

namespace Pylon.ModLoader;

/// <summary>
/// 内容发号指纹：注册顺序决定 ID，两端 mod 集合或顺序不同就会错位。
/// 指纹不参与游戏逻辑，只在启动与联机时给出一条"两端不一致"的证据。
/// </summary>
internal static class ContentFingerprint
{
    private static ulong _value;
    private static string _text = "0";

    /// <summary>注册期最后一次算出的指纹（十六进制）。</summary>
    internal static string Text => _text;

    internal static ulong Value => _value;

    /// <summary>
    /// 按注册顺序把「种类 + 编号 + Mod名/类型名」折成一个 64 位哈希。
    /// FNV-1a：只为了发现"两端不一样"，不做安全用途，也不需要抗碰撞。
    /// </summary>
    internal static void Refresh()
    {
        var registry = LoaderHost.Current.Content;
        var hash = 14695981039346656037UL;

        for (var i = 0; i < registry.Count; i++)
        {
            var descriptor = registry.DescriptorAt(i);
            var instance = registry.InstanceAt(i);

            hash = Mix(hash, (int)descriptor.Kind);
            hash = Mix(hash, descriptor.Type);
            hash = Mix(hash, instance.FullKey);
        }

        _value = hash;
        _text = hash.ToString("X16");
    }

    /// <summary>两端指纹不一致时给出可直接对账的一行文本。</summary>
    internal static string Describe(ulong remote) =>
        $"mod 内容指纹不一致：本端 {_text}，对端 {remote:X16}；两端的 mod 集合或装载顺序不同，配方 / 商店 / 掉落 / 物品编号可能错位";

    private static ulong Mix(ulong hash, int value)
    {
        hash ^= (uint)value;
        hash *= 1099511628211UL;
        return hash;
    }

    private static ulong Mix(ulong hash, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 1099511628211UL;
        }

        return Mix(hash, -1);
    }

    /// <summary>给日志用的一行摘要。</summary>
    internal static string Summary()
    {
        var registry = LoaderHost.Current.Content;
        return new StringBuilder(64).Append("内容 ").Append(registry.Count).Append(" 项，指纹 ").Append(_text).ToString();
    }
}
