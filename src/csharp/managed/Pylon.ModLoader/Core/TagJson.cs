using System.Text.Json;

namespace Pylon.ModLoader;

/// <summary>TagCompound 的 JSON 落盘（侧车存档）。</summary>
internal static class TagJson
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    internal static TagCompound Read(string path)
    {
        if (!File.Exists(path)) return new TagCompound();

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return ReadValue(document.RootElement) as TagCompound ?? new TagCompound();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Safe.Once("tagjson.read:" + path, $"侧车存档损坏，已忽略：{path}（{e.Message}）", true);
            return new TagCompound();
        }
    }

    internal static void Write(string path, TagCompound tag)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // 先写临时文件再原子替换：写到一半被杀也不会留下半个存档。
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteValue(writer, tag);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static object? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ReadObject(element),
        JsonValueKind.Array => ReadArray(element),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt32(out var i) => i,
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number => element.GetDouble(),
        _ => null,
    };

    private static TagCompound ReadObject(JsonElement element)
    {
        var tag = new TagCompound();
        foreach (var property in element.EnumerateObject()) tag.Set(property.Name, ReadValue(property.Value));
        return tag;
    }

    private static List<object?> ReadArray(JsonElement element)
    {
        var list = new List<object?>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray()) list.Add(ReadValue(item));
        return list;
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case TagCompound tag:
                writer.WriteStartObject();
                foreach (var (key, item) in tag)
                {
                    writer.WritePropertyName(key);
                    WriteValue(writer, item);
                }
                writer.WriteEndObject();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                writer.WriteNumberValue(System.Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case float number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case IEnumerable<object?> list:
                writer.WriteStartArray();
                foreach (var item in list) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }
}
