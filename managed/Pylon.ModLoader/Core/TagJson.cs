using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pylon.ModLoader
{
    /// <summary>TagCompound 的 JSON 落盘（侧车存档）。读回来是 int/long/double/bool/string/List/TagCompound。</summary>
    internal static class TagJson
    {
        internal static TagCompound Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return new TagCompound();
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return Convert(document.RootElement) as TagCompound ?? new TagCompound();
            }
            catch (Exception e)
            {
                Safe.Once("tagjson.read:" + path, $"侧车存档损坏，已忽略：{path}（{e.Message}）", true);
                return new TagCompound();
            }
        }

        internal static void Write(string path, TagCompound tag)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(tag);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static object Convert(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var tag = new TagCompound();
                    foreach (var property in element.EnumerateObject())
                        tag[property.Name] = Convert(property.Value);
                    return tag;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray()) list.Add(Convert(item));
                    return list;
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var i)) return i;
                    if (element.TryGetInt64(out var l)) return l;
                    return element.GetDouble();
                default:
                    return null;
            }
        }
    }
}
