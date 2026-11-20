using System.Text.Json;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>框架自己的 UI 设置，持久化到 .pylon/ui.json。</summary>
internal static class UiSettings
{
    internal const float MinScale = 0.75f;
    internal const float MaxScale = 3f;
    internal const float MinWallpaper = 0f;
    internal const float MaxWallpaper = 1f;
    internal const float DefaultWallpaper = 0.85f;

    private static bool _loaded;
    private static float _uiScale = 1.0f;
    private static int _theme = UiTheme.Glacier;
    private static float _wallpaper = DefaultWallpaper;
    private static bool _animations = true;
    private static bool _showFps;
    private static int _touchMode;

    internal static float Scale
    {
        get { EnsureLoaded(); return _uiScale; }
        set { EnsureLoaded(); _uiScale = Math.Clamp(value, MinScale, MaxScale); }
    }

    internal static int Theme
    {
        get { EnsureLoaded(); return _theme; }
        set { EnsureLoaded(); _theme = value; }
    }

    internal static float Wallpaper
    {
        get { EnsureLoaded(); return _wallpaper; }
        set { EnsureLoaded(); _wallpaper = Math.Clamp(value, MinWallpaper, MaxWallpaper); }
    }

    internal static bool Animations
    {
        get { EnsureLoaded(); return _animations; }
        set { EnsureLoaded(); _animations = value; }
    }

    internal static bool ShowFps
    {
        get { EnsureLoaded(); return _showFps; }
        set { EnsureLoaded(); _showFps = value; }
    }

    internal static int TouchMode
    {
        get { EnsureLoaded(); return _touchMode; }
        set { EnsureLoaded(); _touchMode = Math.Clamp(value, 0, 2); }
    }

    internal static void Save()
    {
        try
        {
            var path = SettingsPath;
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                uiScale = _uiScale,
                theme = _theme,
                wallpaper = _wallpaper,
                animations = _animations,
                showFps = _showFps,
                touchMode = _touchMode,
            }));
        }
        catch (Exception e)
        {
            Safe.Warn("保存 UI 设置失败：" + e.Message);
        }
    }

    private static string? SettingsPath
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
            var rootElement = doc.RootElement;
            if (rootElement.TryGetProperty("uiScale", out var value) && value.ValueKind == JsonValueKind.Number)
                _uiScale = Math.Clamp((float)value.GetDouble(), MinScale, MaxScale);
            if (rootElement.TryGetProperty("theme", out value) && value.ValueKind == JsonValueKind.Number)
                _theme = Math.Clamp(value.GetInt32(), 0, UiTheme.PaletteCount - 1);
            if (rootElement.TryGetProperty("wallpaper", out value) && value.ValueKind == JsonValueKind.Number)
                _wallpaper = Math.Clamp((float)value.GetDouble(), MinWallpaper, MaxWallpaper);
            if (rootElement.TryGetProperty("animations", out value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                _animations = value.ValueKind == JsonValueKind.True;
            if (rootElement.TryGetProperty("showFps", out value) && value.ValueKind == JsonValueKind.True)
                _showFps = true;
            if (rootElement.TryGetProperty("touchMode", out value) && value.ValueKind == JsonValueKind.Number)
                _touchMode = Math.Clamp(value.GetInt32(), 0, 2);
        }
        catch (Exception e)
        {
            Safe.Warn("读取 UI 设置失败（用默认值）：" + e.Message);
        }
    }
}
