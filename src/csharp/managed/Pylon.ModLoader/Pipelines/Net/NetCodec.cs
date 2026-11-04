using System.IO;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// Mod 网络数据的值序列化。一侧写标记 + 值，另一侧按标记读；
/// 未知标记读成 null，让上层按"缺值"处理，而不是让整包错位。
/// </summary>
internal static class NetCodec
{
    private const byte TagNull = 0, TagByte = 1, TagSByte = 2, TagShort = 3, TagUShort = 4,
        TagInt = 5, TagUInt = 6, TagLong = 7, TagULong = 8, TagFloat = 9, TagDouble = 10,
        TagBool = 11, TagString = 12, TagBoolArray = 13, TagVector2 = 14, TagList = 15;

    internal static void WriteValues(BinaryWriter writer, IReadOnlyList<object> values)
    {
        writer.Write((ushort)values.Count);
        for (var i = 0; i < values.Count; i++) Write(writer, values[i]);
    }

    internal static object[] ReadValues(Il2CppSystem.IO.BinaryReader reader)
    {
        var count = reader.ReadUInt16();
        var values = new object[count];
        for (var i = 0; i < count; i++) values[i] = Read(reader)!;
        return values;
    }

    private static void Write(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.Write(TagNull); break;
            case byte v: writer.Write(TagByte); writer.Write(v); break;
            case sbyte v: writer.Write(TagSByte); writer.Write(v); break;
            case short v: writer.Write(TagShort); writer.Write(v); break;
            case ushort v: writer.Write(TagUShort); writer.Write(v); break;
            case int v: writer.Write(TagInt); writer.Write(v); break;
            case uint v: writer.Write(TagUInt); writer.Write(v); break;
            case long v: writer.Write(TagLong); writer.Write(v); break;
            case ulong v: writer.Write(TagULong); writer.Write(v); break;
            case float v: writer.Write(TagFloat); writer.Write(v); break;
            case double v: writer.Write(TagDouble); writer.Write(v); break;
            case bool v: writer.Write(TagBool); writer.Write(v); break;
            case string v: writer.Write(TagString); writer.Write(v); break;
            case bool[] v:
                writer.Write(TagBoolArray);
                writer.Write((ushort)v.Length);
                for (var i = 0; i < v.Length; i++) writer.Write(v[i]);
                break;
            case Vector2 v:
                writer.Write(TagVector2);
                writer.Write(v.X);
                writer.Write(v.Y);
                break;
            case List<object> v:
                // 嵌套序列：附属载荷（每件物品的自定义数据）本身是一串值，不能退化成字符串。
                writer.Write(TagList);
                WriteValues(writer, v);
                break;
            case object[] v:
                writer.Write(TagList);
                WriteValues(writer, v);
                break;
            default:
                // 没登记的受控类型退化成字符串：宁可少信息，也不要让协议分叉。
                writer.Write(TagString);
                writer.Write(value.ToString() ?? "");
                break;
        }
    }

    private static object? Read(Il2CppSystem.IO.BinaryReader reader)
    {
        switch (reader.ReadByte())
        {
            case TagNull: return null;
            case TagByte: return reader.ReadByte();
            case TagSByte: return reader.ReadSByte();
            case TagShort: return reader.ReadInt16();
            case TagUShort: return reader.ReadUInt16();
            case TagInt: return reader.ReadInt32();
            case TagUInt: return reader.ReadUInt32();
            case TagLong: return reader.ReadInt64();
            case TagULong: return reader.ReadUInt64();
            case TagFloat: return reader.ReadSingle();
            case TagDouble: return reader.ReadDouble();
            case TagBool: return reader.ReadBoolean();
            case TagString: return reader.ReadString();
            case TagBoolArray:
                var count = reader.ReadUInt16();
                var flags = new bool[count];
                for (var i = 0; i < count; i++) flags[i] = reader.ReadBoolean();
                return flags;
            case TagVector2: return new Vector2(reader.ReadSingle(), reader.ReadSingle());
            case TagList: return ReadValues(reader);
            default: return null;
        }
    }
}
