using System;
using System.Text;

namespace Pylon.NaviteAPI.Effects;

/// <summary>
/// PylonFx_ShaderStatus 的托管侧包装：错误文本要调用方给缓冲区，
/// UTF-8 解码与容量规则留在绑定层，上层只拿 int / string。
/// </summary>
internal static unsafe class ScreenEffectStatus
{
    private const int ErrorCapacity = 2048;

    /// <summary>内核 kPylonFxShader*（0 pending / 1 ready / 2 failed），原样返回。</summary>
    internal static int State(int shader)
    {
        ScreenEffectsNative.ShaderStatus(shader, out var state, null, 0);
        return state;
    }

    /// <summary>编译错误文本；没有错误时为空串。</summary>
    internal static string Error(int shader)
    {
        var buffer = new byte[ErrorCapacity];
        fixed (byte* text = buffer)
        {
            ScreenEffectsNative.ShaderStatus(shader, out _, text, buffer.Length);
        }
        var length = Array.IndexOf(buffer, (byte)0);
        return length <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, length);
    }
}
