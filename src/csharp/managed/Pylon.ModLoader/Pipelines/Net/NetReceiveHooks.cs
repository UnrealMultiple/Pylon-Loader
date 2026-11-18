using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>原版解码前共享消息类型与可替换读取视图，每个作者从同一载荷起点读取。</summary>
internal static class NetReceiveHooks
{
    internal static bool Hijack(MessageBuffer buffer, ref Il2CppStructArray<byte> data,
                                ref int length, ref int messageType)
    {
        var systems = SystemLoader.With(HookId.HijackGetData);
        if (systems.Length == 0) return false;
        if (data is null || length < 1 || length > data.Length || length > 65535) return false;

        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = data[i];
        var type = bytes[0];
        var from = Main.netMode == 1 ? 256 : buffer.whoAmI;
        var managed = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes, false));
        try
        {
            managed.BaseStream.Position = 1;
            var managedOriginal = managed;
            var sysType = type;
            var hijacked = false;
            try
            {
                hijacked = SystemLoader.HijackGetData(ref sysType, ref managed, from);
            }
            catch (Exception e)
            {
                Safe.Report("system.HijackGetData", e);
            }

            if (hijacked)
            {
                messageType = sysType;
                return true;
            }

            if (!ReferenceEquals(managed, managedOriginal))
            {
                try
                {
                    var payload = managed.ReadBytes(65534);
                    var replacement = new byte[payload.Length + 1];
                    replacement[0] = sysType;
                    for (var i = 0; i < payload.Length; i++) replacement[i + 1] = payload[i];
                    data = replacement;
                    length = replacement.Length;
                    return false;
                }
                catch (Exception e)
                {
                    Safe.Report("system.HijackGetData.Payload", e);
                    bytes[0] = sysType;
                    data = bytes;
                    length = bytes.Length;
                    return false;
                }
            }

            if (sysType != bytes[0])
            {
                bytes[0] = sysType;
                data = bytes;
            }
        }
        finally
        {
            try { managed.Dispose(); }
            catch (Exception e) { Safe.Report("system.HijackGetData.Dispose", e); }
        }
        return false;
    }
}
