using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Textures;
using Pylon.ModLoader.UI;
using Pylon.NaviteAPI.Effects;
using UnityEngine;

namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果（全屏后处理 GLSL）。Mod 每帧构造 ScreenFrame 提交。</summary>
public static class ScreenGraphics
{
    private static bool _installTried;
    private static readonly float[] BoxBuf = new float[4];
    private static readonly float[] CenterBuf = new float[2];
    private static readonly float[] ColorBuf = new float[4];

    public static bool Installed
    {
        get
        {
            try { return ScreenEffectsNative.Available() != 0; }
            catch { return false; }
        }
    }

    public static void Install()
    {
        if (_installTried) return;
        _installTried = true;
        if (!PylonTexture.IsGameThread)
        {
            _installTried = false;
            throw new ScreenEffectsException("ScreenGraphics.Install 只能在游戏线程(UnityMain)上调用");
        }
        try
        {
            var ok = ScreenEffectsNative.Install() != 0;
            ModUI.Logger?.Info($"屏幕效果：hooks {(ok ? "已装" : "安装失败（见日志）")}");
        }
        catch (Exception e)
        {
            _installTried = false;
            ModUI.Logger?.Warn($"屏幕效果安装异常：{e.Message}");
        }
    }

    public static ScreenShader Shader(string name, string source) => new(name, source);

    public static ScreenTexture Texture(Mod owner, string path, bool linear = true, bool repeat = false)
    {
        var full = PylonTexture.Resolve(owner, path);
        byte[] fileBytes;
        try { fileBytes = File.ReadAllBytes(full); }
        catch (Exception e) { throw new ScreenEffectsException($"读取效果纹理失败：{full}（{e.Message}）"); }

        var bytes = new Il2CppStructArray<byte>(fileBytes);
        var unity = new Texture2D(2, 2);
        bool decoded;
        try { decoded = ImageConversion.LoadImage(unity, bytes, false); }
        catch (Exception e)
        {
            UnityEngine.Object.Destroy(unity);
            throw new ScreenEffectsException($"效果纹理解码抛异常：{full}（{e.Message}）");
        }
        if (!decoded)
        {
            UnityEngine.Object.Destroy(unity);
            throw new ScreenEffectsException($"'{full}' 不是能解码的 PNG/JPG");
        }

        var width = unity.width;
        var height = unity.height;
        var rgba = FlipRows(unity.GetRawTextureData<byte>().ToArray(), width, height);
        UnityEngine.Object.Destroy(unity);

        int id;
        try
        {
            id = ScreenEffectsNative.TextureLoad(full, width, height, rgba, linear ? 1 : 0, repeat ? 1 : 0, out _, out _);
        }
        catch (Exception e) { throw new ScreenEffectsException($"效果纹理登记失败：{full}（{e.Message}）"); }
        if (id == 0) throw new ScreenEffectsException($"效果纹理登记失败：{full}（参数无效）");
        return new ScreenTexture(id, width, height);
    }

    public static ScreenTexture Texture(string key, int width, int height, byte[] rgba,
        bool linear = true, bool repeat = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0)
            throw new ScreenEffectsException($"效果纹理 '{key}' 尺寸无效：{width}x{height}");
        if (rgba.LongLength < (long)width * height * 4)
            throw new ScreenEffectsException($"效果纹理 '{key}' 的 RGBA 数据不足 {width}x{height}");

        int id, outWidth, outHeight;
        try
        {
            id = ScreenEffectsNative.TextureLoad(key, width, height, rgba, linear ? 1 : 0, repeat ? 1 : 0,
                out outWidth, out outHeight);
        }
        catch (Exception e) { throw new ScreenEffectsException($"效果纹理登记失败：{key}（{e.Message}）"); }
        if (id == 0) throw new ScreenEffectsException($"效果纹理登记失败：{key}（参数无效）");
        return new ScreenTexture(id, outWidth > 0 ? outWidth : width, outHeight > 0 ? outHeight : height);
    }

    public static void Submit(ScreenFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Draws.Count == 0) return;
        Install();

        ScreenEffectsNative.SubmitBegin(frame.History ? 1 : 0);
        try
        {
            foreach (var target in frame.Targets)
                ScreenEffectsNative.SubmitAddTarget(target.Id, target.Name, target.Scale, target.Persistent ? 1 : 0);

            foreach (var draw in frame.Draws)
            {
                int handle;
                if (draw.Mesh)
                {
                    var verts = draw.MeshVertices;
                    if (verts is null || verts.Length == 0 || verts.Length % 12 != 0) continue;
                    var count = verts.Length / 12;
                    if (count % 3 != 0) continue;
                    handle = ScreenEffectsNative.SubmitAddMesh(
                        draw.Shader?.Id ?? 0,
                        (int)draw.Layer,
                        draw.Dest,
                        (int)draw.MeshBlend,
                        count,
                        verts);
                    if (handle <= 0) continue;
                }
                else
                {
                    float[]? box = null;
                    if (draw.Box is { Length: 4 } candidate)
                    {
                        box = candidate;
                        Array.Copy(candidate, BoxBuf, 4);
                    }
                    CenterBuf[0] = draw.Center.x;
                    CenterBuf[1] = draw.Center.y;
                    ColorBuf[0] = draw.Color.r;
                    ColorBuf[1] = draw.Color.g;
                    ColorBuf[2] = draw.Color.b;
                    ColorBuf[3] = draw.Color.a;
                    handle = ScreenEffectsNative.SubmitAddDraw(
                        draw.Shader?.Id ?? 0,
                        (int)draw.Layer,
                        draw.Dest,
                        draw.Source?.Id ?? ScreenTexture.kTexNone,
                        box is null ? null : BoxBuf,
                        CenterBuf,
                        draw.Radius,
                        draw.Intensity,
                        draw.Progress,
                        draw.Age,
                        ColorBuf);
                    if (handle <= 0) continue;
                }
                draw.Submit(handle);
            }
        }
        finally
        {
            ScreenEffectsNative.SubmitEnd();
        }
    }

    public static void SaveNeutralLut(string path, int size = 16)
    {
        if (size < 2) throw new ArgumentException("LUT 尺寸必须 >= 2", nameof(size));
        var inv = 1f / (size - 1f);
        var w = size * size;
        var rgba = new byte[w * size * 4];
        var idx = 0;
        for (var s = 0; s < size; s++)
        {
            var b = (byte)Mathf.RoundToInt(s * inv * 255f);
            for (var y = 0; y < size; y++)
            {
                var g = (byte)Mathf.RoundToInt(y * inv * 255f);
                for (var x = 0; x < size; x++)
                {
                    rgba[idx++] = (byte)Mathf.RoundToInt(x * inv * 255f);  // R
                    rgba[idx++] = g;
                    rgba[idx++] = b;
                    rgba[idx++] = 255;
                }
            }
        }
        File.WriteAllBytes(path, PngEncoder.Encode(w, size, rgba));
    }

    public static ScreenStats Stats()
    {
        ScreenEffectsNative.Stats(out var native);
        var stats = ScreenStats.From(native);
        ScreenEffectsNative.UploadStats(out var upload);
        stats.BufferSubDataBytes = upload.BufferSubDataBytes;
        stats.BufferStorageBytes = upload.BufferStorageBytes;
        stats.BufferDataCalls = upload.BufferDataCalls;
        stats.BufferSubDataCalls = upload.BufferSubDataCalls;
        return stats;
    }

    public static void RequestTrace() => ScreenEffectsNative.RequestTrace();

    public static void SetMeshStreaming(bool enabled) => ScreenEffectsNative.SetMeshStreaming(enabled ? 1 : 0);

    private static byte[] FlipRows(byte[] raw, int width, int height)
    {
        var row = width * 4;
        var outBytes = new byte[row * height];
        for (var y = 0; y < height; y++)
        {
            var src = y * row;
            var dst = (height - 1 - y) * row;
            for (var i = 0; i < row; i++) outBytes[dst + i] = raw[src + i];
        }
        return outBytes;
    }
}
