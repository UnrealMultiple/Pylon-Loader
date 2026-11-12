using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Effects;

/// <summary>
/// 内核 PylonFx_* 导出（见 core/include/pylon/graphics/ScreenEffectsApi.h）。
/// 字符串一律 UTF-8，布尔用 0/1 int；错误文本走调用方给的裸缓冲区（char* + 容量）。
/// </summary>
internal static unsafe partial class ScreenEffectsNative
{
    [LibraryImport("pylon", EntryPoint = "PylonFx_Install")]
    internal static partial int Install();

    [LibraryImport("pylon", EntryPoint = "PylonFx_Available")]
    internal static partial int Available();

    [LibraryImport("pylon", EntryPoint = "PylonFx_ShaderRegister", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ShaderRegister(string name, string source);

    [LibraryImport("pylon", EntryPoint = "PylonFx_ShaderRegisterMesh", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ShaderRegisterMesh(string name, string source);

    /// <summary>查询编译状态；<paramref name="error"/> 可空，失败时内核写入 UTF-8 错误文本。</summary>
    [LibraryImport("pylon", EntryPoint = "PylonFx_ShaderStatus")]
    internal static partial int ShaderStatus(int id, out int state, byte* error, int errorCap);

    [LibraryImport("pylon", EntryPoint = "PylonFx_TextureLoad", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TextureLoad(string key, int width, int height,
        [In] byte[] rgba, int linear, int repeat, out int outWidth, out int outHeight);

    [LibraryImport("pylon", EntryPoint = "PylonFx_SubmitBegin")]
    internal static partial int SubmitBegin(int history);

    [LibraryImport("pylon", EntryPoint = "PylonFx_SubmitAddTarget", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SubmitAddTarget(int id, string name, float scale, int persistent);

    /// <summary><paramref name="box"/> / <paramref name="center"/> / <paramref name="color"/> 传 null = 取默认。</summary>
    [LibraryImport("pylon", EntryPoint = "PylonFx_SubmitAddDraw")]
    internal static partial int SubmitAddDraw(int shader, int layer, int dest, int source,
        [In] float[] box, [In] float[] center, float radius, float intensity, float progress, float age,
        [In] float[] color);

    [LibraryImport("pylon", EntryPoint = "PylonFx_SubmitAddMesh")]
    internal static partial int SubmitAddMesh(int shader, int layer, int dest, int blend,
        int count, [In] float[] verts);

    [LibraryImport("pylon", EntryPoint = "PylonFx_SubmitAddUniform", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SubmitAddUniform(int draw, string name, int components, [In] float[] values, int count);

    [LibraryImport("pylon", EntryPoint = "PylonFx_SubmitAddTexture", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SubmitAddTexture(int draw, string name, int texture);

    [LibraryImport("pylon", EntryPoint = "PylonFx_SubmitEnd")]
    internal static partial void SubmitEnd();

    [LibraryImport("pylon", EntryPoint = "PylonFx_Stats")]
    internal static partial void Stats(out PylonFxStatsNative stats);

    [StructLayout(LayoutKind.Sequential)]
    internal struct UploadStatsNative
    {
        public long BufferSubDataBytes, BufferStorageBytes, BufferDataCalls, BufferSubDataCalls;
    }

    [LibraryImport("pylon", EntryPoint = "PylonFx_UploadStats")]
    internal static partial void UploadStats(out UploadStatsNative stats);

    [LibraryImport("pylon", EntryPoint = "PylonFx_SetMeshStreaming")]
    internal static partial void SetMeshStreaming(int enabled);

    [LibraryImport("pylon", EntryPoint = "PylonFx_RequestTrace")]
    internal static partial void RequestTrace();
}
