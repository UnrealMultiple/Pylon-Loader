using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;

namespace CheatMenu.Infra;

/// <summary>
/// 原始包构造/发送。1:1 对应原版 <c>PacketBuilder</c>：
/// 包帧 = [ushort 长度（含自身与类型字节）][byte 消息类型][负载]，
/// 走 Netplay.Connection.Client（Telepathy.Client）同步入队发送，发前递增 buffer[256].spamCount。
/// 仅联机客户端（netMode==1）可发。
/// </summary>
public sealed class RawPacket
{
    private readonly MemoryStream _stream = new(64);
    private readonly BinaryWriter _writer;
    private int _packetHead = -1;

    public RawPacket()
    {
        _writer = new BinaryWriter(_stream);
    }

    public int Length => (int)_stream.Length;

    // ---------------- 包帧 ----------------

    /// <summary>开一个包：占 2 字节长度 + 1 字节类型。</summary>
    public RawPacket StartPacket(int msgType)
    {
        if (_packetHead >= 0) throw new InvalidOperationException("已有包在构建，先 EndPacket");
        _packetHead = (int)_stream.Length;
        _writer.Write((ushort)0);
        _writer.Write((byte)msgType);
        return this;
    }

    /// <summary>封包：回填长度。</summary>
    public RawPacket EndPacket()
    {
        if (_packetHead < 0) throw new InvalidOperationException("没有包在构建，先 StartPacket");
        var length = (ushort)(_stream.Length - _packetHead);
        var buffer = _stream.GetBuffer();
        buffer[_packetHead] = (byte)(length & 0xFF);
        buffer[_packetHead + 1] = (byte)(length >> 8);
        _packetHead = -1;
        return this;
    }

    /// <summary>便捷：开包 → 写负载 → 封包。</summary>
    public RawPacket Packet(int msgType, Action<BinaryWriter> content)
    {
        StartPacket(msgType);
        content(_writer);
        EndPacket();
        return this;
    }

    // ---------------- 写入（与 BinaryWriter 语义一致，负载部分不再写帧头） ----------------

    public RawPacket Write(short v) { _writer.Write(v); return this; }
    public RawPacket Write(int v) { _writer.Write(v); return this; }
    public RawPacket Write(long v) { _writer.Write(v); return this; }
    public RawPacket Write(ushort v) { _writer.Write(v); return this; }
    public RawPacket Write(uint v) { _writer.Write(v); return this; }
    public RawPacket Write(ulong v) { _writer.Write(v); return this; }
    public RawPacket Write(byte v) { _writer.Write(v); return this; }
    public RawPacket Write(sbyte v) { _writer.Write(v); return this; }
    public RawPacket Write(bool v) { _writer.Write(v); return this; }
    public RawPacket Write(float v) { _writer.Write(v); return this; }
    public RawPacket Write(double v) { _writer.Write(v); return this; }
    public RawPacket WriteBytes(byte[] bytes) { _writer.Write(bytes); return this; }

    /// <summary>发送到服务器。可含多个已封好的包，一次 Send 全部入队。</summary>
    public bool Send()
    {
        if (_packetHead >= 0) throw new InvalidOperationException("Send 前必须先 EndPacket");
        var bytes = _stream.ToArray();
        if (bytes.Length == 0) return false;

        try
        {
            var connection = Netplay.Connection;
            var client = connection?.Client;
            if (client is null || !client.Connected) return false;

            // 原版 IncSpamCount：防刷屏计数（buffer[256] 是客户端自己的发送缓冲）
            var buffers = NetMessage.buffer;
            if (buffers is not null && buffers.Length > 256)
            {
                var self = buffers[256];
                if (self is not null) self.spamCount++;
            }

            var payload = new Il2CppStructArray<byte>(bytes);
            return client.Send(payload, bytes.Length);
        }
        catch
        {
            return false;
        }
    }

    public void Clear()
    {
        _stream.SetLength(0);
        _packetHead = -1;
    }
}
