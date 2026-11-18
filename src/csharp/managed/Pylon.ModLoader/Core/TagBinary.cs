using System.Collections;

namespace Pylon.ModLoader;

/// <summary>TagCompound 的二进制编解码：类型字节 + 7bit 长度 + UTF8 字符串，与世界侧车的 GZip 流配套。</summary>
internal static class TagBinary
{
    private const byte TagNull = 0;
    private const byte TagCompound = 1;
    private const byte TagBool = 2;
    private const byte TagByte = 3;
    private const byte TagSByte = 4;
    private const byte TagShort = 5;
    private const byte TagUShort = 6;
    private const byte TagInt = 7;
    private const byte TagUInt = 8;
    private const byte TagLong = 9;
    private const byte TagULong = 10;
    private const byte TagFloat = 11;
    private const byte TagDouble = 12;
    private const byte TagDecimal = 13;
    private const byte TagString = 14;
    private const byte TagList = 15;

    internal static void Write(BinaryWriter writer, TagCompound tag)
    {
        writer.Write(tag.Count);
        foreach (var (key, value) in tag)
        {
            writer.Write(key);
            WriteValue(writer, value);
        }
    }

    internal static TagCompound Read(BinaryReader reader)
    {
        var tag = new TagCompound();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadString();
            tag.Set(key, ReadValue(reader));
        }
        return tag;
    }

    private static void WriteValue(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.Write(TagNull);
                break;
            case TagCompound tag:
                writer.Write(TagCompound);
                Write(writer, tag);
                break;
            case bool flag:
                writer.Write(TagBool);
                writer.Write(flag);
                break;
            case byte number:
                writer.Write(TagByte);
                writer.Write(number);
                break;
            case sbyte number:
                writer.Write(TagSByte);
                writer.Write(number);
                break;
            case short number:
                writer.Write(TagShort);
                writer.Write(number);
                break;
            case ushort number:
                writer.Write(TagUShort);
                writer.Write(number);
                break;
            case int number:
                writer.Write(TagInt);
                writer.Write(number);
                break;
            case uint number:
                writer.Write(TagUInt);
                writer.Write(number);
                break;
            case long number:
                writer.Write(TagLong);
                writer.Write(number);
                break;
            case ulong number:
                writer.Write(TagULong);
                writer.Write(number);
                break;
            case float number:
                writer.Write(TagFloat);
                writer.Write(number);
                break;
            case double number:
                writer.Write(TagDouble);
                writer.Write(number);
                break;
            case decimal number:
                writer.Write(TagDecimal);
                writer.Write(number);
                break;
            case string text:
                writer.Write(TagString);
                writer.Write(text);
                break;
            case IEnumerable list:
                var items = new List<object?>();
                foreach (var item in list) items.Add(item);
                writer.Write(TagList);
                writer.Write(items.Count);
                foreach (var item in items) WriteValue(writer, item);
                break;
            default:
                writer.Write(TagString);
                writer.Write(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                break;
        }
    }

    private static object? ReadValue(BinaryReader reader) => reader.ReadByte() switch
    {
        TagNull => null,
        TagCompound => Read(reader),
        TagBool => reader.ReadBoolean(),
        TagByte => reader.ReadByte(),
        TagSByte => reader.ReadSByte(),
        TagShort => reader.ReadInt16(),
        TagUShort => reader.ReadUInt16(),
        TagInt => reader.ReadInt32(),
        TagUInt => reader.ReadUInt32(),
        TagLong => reader.ReadInt64(),
        TagULong => reader.ReadUInt64(),
        TagFloat => reader.ReadSingle(),
        TagDouble => reader.ReadDouble(),
        TagDecimal => reader.ReadDecimal(),
        TagString => reader.ReadString(),
        TagList => ReadList(reader),
        _ => null,
    };

    private static List<object?> ReadList(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var list = new List<object?>(count);
        for (var i = 0; i < count; i++) list.Add(ReadValue(reader));
        return list;
    }
}
