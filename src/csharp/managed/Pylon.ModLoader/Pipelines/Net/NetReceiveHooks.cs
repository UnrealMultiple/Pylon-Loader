using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>原版解码前共享消息类型与可替换读取视图，每个作者从同一载荷起点读取。</summary>
internal static class NetReceiveHooks
{
    internal static bool Hijack(MessageBuffer buffer, ref Il2CppStructArray<byte> data,
                                ref int length, ref int messageType)
    {
        var mods = LoaderHost.Current.Hooks.Channel(HookId.HijackGetData);
        if (mods.Length == 0 || data is null || length < 1 || length > data.Length || length > 65535) return false;

        // 托管数组隐式拷贝避免在 ARM64 上逐格调用 interop 数组写引用入口。
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = data[i];
        var owned = new List<Il2CppSystem.IO.BinaryReader>();
        Il2CppSystem.IO.BinaryReader View()
        {
            Il2CppStructArray<byte> copy = bytes;
            var stream = new Il2CppSystem.IO.MemoryStream(copy, false);
            var view = new Il2CppSystem.IO.BinaryReader(stream);
            owned.Add(view);
            stream.Position = 1;
            return view;
        }

        var original = View();
        var reader = original;
        var type = bytes[0];
        const long start = 1;
        var consumed = false;
        long largestPosition = start;
        var from = Main.netMode == 1 ? 256 : buffer.whoAmI;
        try
        {
            foreach (var mod in mods)
            {
                var previous = reader;
                var returned = false;
                var accepts = false;
                try
                {
                    accepts = mod.HijackGetData(ref type, ref reader, from);
                    returned = true;
                }
                catch (Exception e)
                {
                    Safe.Report(mod.Name + ".HijackGetData", e);
                    reader = previous;
                }

                // 真返回才消费；读取位置非法或替换视图损坏时仍让下一作者继续处理。
                if (returned && accepts)
                {
                    consumed = true;
                    try
                    {
                        if (reader is not null && reader.BaseStream.CanRead && reader.BaseStream.CanSeek)
                            largestPosition = Math.Max(largestPosition, reader.BaseStream.Position);
                    }
                    catch (Exception e) { Safe.Report(mod.Name + ".HijackGetData.Position", e); }
                }
                if (!Reset(reader))
                {
                    reader = previous;
                    if (!Reset(reader)) reader = View();
                }
                // Reset 的成功路径保证非空；保留显式兜底以让后续 ref API 有确定的非空视图。
                reader ??= View();
            }

            if (consumed)
            {
                try { reader.BaseStream.Position = largestPosition; }
                catch (Exception e) { Safe.Report("HijackGetData.Position", e); }
                messageType = type;
                return true;
            }

            try
            {
                if (reader.Pointer != original.Pointer)
                {
                    var payload = reader.ReadBytes(65534);
                    var replacement = new byte[payload.Length + 1];
                    replacement[0] = type;
                    for (var i = 0; i < payload.Length; i++) replacement[i + 1] = payload[i];
                    data = replacement;
                    length = replacement.Length;
                }
                else if (type != bytes[0])
                {
                    bytes[0] = type;
                    data = bytes;
                }
            }
            catch (Exception e)
            {
                // 作者的流即便声称可读可定位，实际 Read 仍可能失败；回退原载荷而保留类型修改。
                Safe.Report("HijackGetData.Payload", e);
                bytes[0] = type;
                data = bytes;
                length = bytes.Length;
            }
            return false;
        }
        finally
        {
            // 作者提供的替换流由作者持有；作者提前关闭框架视图也不能使接收线程抛出。
            foreach (var view in owned)
            {
                try { view.Dispose(); }
                catch (Exception e) { Safe.Report("HijackGetData.Dispose", e); }
            }
        }
    }

    private static bool Reset(Il2CppSystem.IO.BinaryReader? reader)
    {
        try
        {
            if (reader is null || !reader.BaseStream.CanRead || !reader.BaseStream.CanSeek) return false;
            reader.BaseStream.Position = 1;
            return true;
        }
        catch { return false; }
    }
}
