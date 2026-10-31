using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Pylon.ModLoader;

/// <summary>
/// JNI 反射桥的 C# 侧。IL2CPP 没实例化 AndroidJavaObject 的泛型 Call&lt;T&gt;，
/// 所以 Java 反射走这条：内核做 JNI 原语，这里包成对象式封装。
/// 约定位：拿到的引用都是全局引用，用完 Dispose；失败原因见 <see cref="LastError"/>。
/// </summary>
internal static partial class Jni
{
    private static readonly ConcurrentDictionary<(IntPtr Class, string Name, string Signature, bool Static), IntPtr>
        MethodCache = new();

    private static readonly ConcurrentDictionary<(IntPtr Class, string Name, string Signature, bool Static), IntPtr>
        FieldCache = new();

    /// <summary>最近一次失败原因；没有错误时为空串。</summary>
    internal static string LastError
    {
        get
        {
            var pointer = LastErrorPointer();
            return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
    }

    /// <summary>关键步骤前清一次，之后看 <see cref="LastError"/> 是否为空。</summary>
    internal static void ClearError() => ClearErrorNative();

    internal static IntPtr FindClass(string name) => FindClassNative(name);

    internal static IntPtr GetObjectClass(IntPtr obj) => GetObjectClassNative(obj);

    internal static void DeleteGlobalRef(IntPtr global)
    {
        if (global != IntPtr.Zero) DeleteGlobalRefNative(global);
    }

    /// <summary>MethodID 会话内稳定，按类引用 + 名字 + 签名缓存。</summary>
    internal static IntPtr Method(IntPtr cls, string name, string signature, bool isStatic)
    {
        if (cls == IntPtr.Zero) return IntPtr.Zero;

        var key = (cls, name, signature, isStatic);
        if (MethodCache.TryGetValue(key, out var cached)) return cached;

        var method = isStatic
            ? GetStaticMethodIdNative(cls, name, signature)
            : GetMethodIdNative(cls, name, signature);
        if (method != IntPtr.Zero) MethodCache[key] = method;
        return method;
    }

    internal static IntPtr Field(IntPtr cls, string name, string signature, bool isStatic)
    {
        if (cls == IntPtr.Zero) return IntPtr.Zero;

        var key = (cls, name, signature, isStatic);
        if (FieldCache.TryGetValue(key, out var cached)) return cached;

        var field = isStatic
            ? GetStaticFieldIdNative(cls, name, signature)
            : GetFieldIdNative(cls, name, signature);
        if (field != IntPtr.Zero) FieldCache[key] = field;
        return field;
    }

    /// <summary>UTF-8 → Java String（全局引用，用完 Dispose）。失败返回空句柄对象。</summary>
    internal static JniObject NewString(string text)
    {
        var handle = NewStringUtfNative(text);
        if (handle == IntPtr.Zero) JniObject.Report(nameof(NewString));
        return new JniObject(handle);
    }

    internal static string? ReadString(IntPtr jstring)
    {
        if (jstring == IntPtr.Zero) return null;

        var pointer = GetStringUtfNative(jstring);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            ReleaseStringUtfNative(pointer);
        }
    }

    internal static bool HasException() => ExceptionCheckNative() != 0;

    // ---- 调用原语（argv 统一在这里归一化，避免每处判空） ----

    internal static IntPtr NewObject(IntPtr cls, IntPtr ctor, JValue[]? argv)
    {
        argv ??= [];
        return NewObjectNative(cls, ctor, argv.Length, argv);
    }

    internal static int CallInt(IntPtr obj, IntPtr method, JValue[]? argv)
    {
        argv ??= [];
        return CallIntNative(obj, method, argv.Length, argv);
    }

    internal static bool CallBoolean(IntPtr obj, IntPtr method, JValue[]? argv)
    {
        argv ??= [];
        return CallBooleanNative(obj, method, argv.Length, argv) != 0;
    }

    internal static void CallVoid(IntPtr obj, IntPtr method, JValue[]? argv)
    {
        argv ??= [];
        CallVoidNative(obj, method, argv.Length, argv);
    }

    internal static IntPtr CallObject(IntPtr obj, IntPtr method, JValue[]? argv)
    {
        argv ??= [];
        return CallObjectNative(obj, method, argv.Length, argv);
    }

    internal static int CallStaticInt(IntPtr cls, IntPtr method, JValue[]? argv)
    {
        argv ??= [];
        return CallStaticIntNative(cls, method, argv.Length, argv);
    }

    internal static IntPtr CallStaticObject(IntPtr cls, IntPtr method, JValue[]? argv)
    {
        argv ??= [];
        return CallStaticObjectNative(cls, method, argv.Length, argv);
    }

    internal static void CallStaticVoid(IntPtr cls, IntPtr method, JValue[]? argv)
    {
        argv ??= [];
        CallStaticVoidNative(cls, method, argv.Length, argv);
    }

    internal static int GetIntField(IntPtr obj, IntPtr field) => GetIntFieldNative(obj, field);

    internal static void SetIntField(IntPtr obj, IntPtr field, int value) => SetIntFieldNative(obj, field, value);

    internal static void SetObjectField(IntPtr obj, IntPtr field, IntPtr value) => SetObjectFieldNative(obj, field, value);

    internal static IntPtr GetObjectField(IntPtr obj, IntPtr field) => GetObjectFieldNative(obj, field);

    internal static IntPtr GetStaticObjectField(IntPtr cls, IntPtr field) => GetStaticObjectFieldNative(cls, field);

    // ---- 原生导出 ----

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_LastError")]
    private static partial IntPtr LastErrorPointer();

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_ClearError")]
    private static partial void ClearErrorNative();

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_FindClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr FindClassNative(string name);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetObjectClass")]
    private static partial IntPtr GetObjectClassNative(IntPtr obj);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_DeleteGlobalRef")]
    private static partial void DeleteGlobalRefNative(IntPtr global);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetMethodID", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetMethodIdNative(IntPtr cls, string name, string signature);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetStaticMethodID", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetStaticMethodIdNative(IntPtr cls, string name, string signature);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_NewObject")]
    private static partial IntPtr NewObjectNative(IntPtr cls, IntPtr ctor, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_CallInt")]
    private static partial int CallIntNative(IntPtr obj, IntPtr method, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_CallBoolean")]
    private static partial int CallBooleanNative(IntPtr obj, IntPtr method, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_CallVoid")]
    private static partial void CallVoidNative(IntPtr obj, IntPtr method, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_CallObject")]
    private static partial IntPtr CallObjectNative(IntPtr obj, IntPtr method, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_CallStaticInt")]
    private static partial int CallStaticIntNative(IntPtr cls, IntPtr method, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_CallStaticObject")]
    private static partial IntPtr CallStaticObjectNative(IntPtr cls, IntPtr method, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_CallStaticVoid")]
    private static partial void CallStaticVoidNative(IntPtr cls, IntPtr method, int argc, JValue[] argv);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetFieldID", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetFieldIdNative(IntPtr cls, string name, string signature);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetStaticFieldID", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetStaticFieldIdNative(IntPtr cls, string name, string signature);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetObjectField")]
    private static partial IntPtr GetObjectFieldNative(IntPtr obj, IntPtr field);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetStaticObjectField")]
    private static partial IntPtr GetStaticObjectFieldNative(IntPtr cls, IntPtr field);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetIntField")]
    private static partial int GetIntFieldNative(IntPtr obj, IntPtr field);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_SetIntField")]
    private static partial void SetIntFieldNative(IntPtr obj, IntPtr field, int value);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_SetObjectField")]
    private static partial void SetObjectFieldNative(IntPtr obj, IntPtr field, IntPtr value);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_NewStringUtf", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr NewStringUtfNative(string text);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_GetStringUtf")]
    private static partial IntPtr GetStringUtfNative(IntPtr jstring);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_ReleaseStringUtf")]
    private static partial void ReleaseStringUtfNative(IntPtr copy);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Jni_ExceptionCheck")]
    private static partial int ExceptionCheckNative();
}
