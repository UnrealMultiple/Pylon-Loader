using System;
using System.IO;
using System.Text.Json;

namespace Pylon.ModLoader.UI
{
    /// <summary>框架自己的 UI 设置（整体缩放）。存 Mods 根下 .pylon/ui.json，不进 Mod 目录、不被升级覆盖。</summary>
    internal static class UiSettings
    {
        internal const float MinScale = 0.75f;
        internal const float MaxScale = 1.5f;

        private static bool _loaded;
        private static float _uiScale = 1.0f;

        internal static float Scale
        {
            get { EnsureLoaded(); return _uiScale; }
            set { EnsureLoaded(); _uiScale = Math.Clamp(value, MinScale, MaxScale); }
        }

        internal static void Save()
        {
            try
            {
                var path = SettingsPath;
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, JsonSerializer.Serialize(new { uiScale = _uiScale }));
            }
            catch (Exception e)
            {
                Safe.Warn?.Invoke("保存 UI 设置失败：" + e.Message);
            }
        }

        private static string SettingsPath
        {
            get
            {
                var root = Framework.HostDirectory;
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, ".pylon", "ui.json");
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var path = SettingsPath;
                if (path == null || !File.Exists(path)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("uiScale", out var value) &&
                    value.ValueKind == JsonValueKind.Number)
                    _uiScale = Math.Clamp((float)value.GetDouble(), MinScale, MaxScale);
            }
            catch (Exception e)
            {
                Safe.Warn?.Invoke("读取 UI 设置失败（用默认值）：" + e.Message);
            }
        }
    }
}
