using System.Collections.Concurrent;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using UnityEngine;
using JArg = UnityEngine.jvalue;

namespace Pylon.ModLoader;

/// <summary>
/// JNI 原语层：直接调 UnityEngine.AndroidJNI，中间不再有 C++ 桥。
/// 约定：交出去的引用一律是全局引用（用完 Dispose）；失败原因见 <see cref="LastError"/>。
/// </summary>
internal static class Jni
{
    private static readonly ConcurrentDictionary<(IntPtr Class, string Name, string Sig, bool Static), IntPtr> MethodIds = new();
    private static readonly ConcurrentDictionary<(IntPtr Class, string Name, string Sig, bool Static), IntPtr> FieldIds = new();

    private static readonly JValue[] None = [];

    [ThreadStatic] private static string? _error;
    [ThreadStatic] private static bool _attached;
    [ThreadStatic] private static Il2CppStructArray<JArg>?[]? _args;

    internal static string LastError => _error ?? string.Empty;

    internal static void ClearError() => _error = null;

    // ---- 引用 ----

    internal static IntPtr FindClass(string name) => Track("FindClass " + name, AndroidJNI.FindClass(name));

    internal static IntPtr ObjectClass(IntPtr obj) => Track("GetObjectClass", AndroidJNI.GetObjectClass(obj));

    internal static void Release(IntPtr handle)
    {
        if (handle != IntPtr.Zero) AndroidJNI.DeleteGlobalRef(handle);
    }

    /// <summary>本地引用收口：先把异常记账，再换成本地引用释放后的全局引用。</summary>
    private static IntPtr Track(string op, IntPtr local)
    {
        if (!Check(op))
        {
            if (local != IntPtr.Zero) AndroidJNI.DeleteLocalRef(local);
            return IntPtr.Zero;
        }

        if (local == IntPtr.Zero)
        {
            _error = op + ": 返回空引用";
            return IntPtr.Zero;
        }

        var global = AndroidJNI.NewGlobalRef(local);
        AndroidJNI.DeleteLocalRef(local);
        return global;
    }

    /// <summary>每线程第一次碰 JNI 前接上 JavaVM；已 attach 时是 no-op。</summary>
    private static void Attach()
    {
        if (_attached) return;
        _attached = true;
        AndroidJNI.AttachCurrentThread();
    }

    // ---- 方法 / 字段 ID（会话内稳定，按类 + 名字 + 签名缓存） ----

    internal static IntPtr Method(IntPtr cls, string name, string sig, bool isStatic)
    {
        if (cls == IntPtr.Zero) return IntPtr.Zero;

        var key = (cls, name, sig, isStatic);
        if (MethodIds.TryGetValue(key, out var cached)) return cached;

        Attach();
        var id = isStatic ? AndroidJNI.GetStaticMethodID(cls, name, sig) : AndroidJNI.GetMethodID(cls, name, sig);
        if (id == IntPtr.Zero)
        {
            Check((isStatic ? "GetStaticMethodID " : "GetMethodID ") + name + sig);
            return IntPtr.Zero;
        }

        MethodIds[key] = id;
        return id;
    }

    internal static IntPtr Field(IntPtr cls, string name, string sig, bool isStatic)
    {
        if (cls == IntPtr.Zero) return IntPtr.Zero;

        var key = (cls, name, sig, isStatic);
        if (FieldIds.TryGetValue(key, out var cached)) return cached;

        Attach();
        var id = isStatic ? AndroidJNI.GetStaticFieldID(cls, name, sig) : AndroidJNI.GetFieldID(cls, name, sig);
        if (id == IntPtr.Zero)
        {
            Check((isStatic ? "GetStaticFieldID " : "GetFieldID ") + name + sig);
            return IntPtr.Zero;
        }

        FieldIds[key] = id;
        return id;
    }

    // ---- 构造 / 调用 ----

    internal static IntPtr NewObject(IntPtr cls, IntPtr ctor, string op, JValue[] args) =>
        Track(op, AndroidJNI.NewObject(cls, ctor, Args(args)));

    internal static int CallInt(IntPtr obj, IntPtr method, string op, JValue[] args)
    {
        var value = AndroidJNI.CallIntMethod(obj, method, Args(args));
        Check(op);
        return value;
    }

    internal static bool CallBoolean(IntPtr obj, IntPtr method, string op, JValue[] args)
    {
        var value = AndroidJNI.CallBooleanMethod(obj, method, Args(args));
        Check(op);
        return value;
    }

    internal static void CallVoid(IntPtr obj, IntPtr method, string op, JValue[] args)
    {
        AndroidJNI.CallVoidMethod(obj, method, Args(args));
        Check(op);
    }

    internal static IntPtr CallObject(IntPtr obj, IntPtr method, string op, JValue[] args) =>
        Track(op, AndroidJNI.CallObjectMethod(obj, method, Args(args)));

    internal static int CallStaticInt(IntPtr cls, IntPtr method, string op, JValue[] args)
    {
        var value = AndroidJNI.CallStaticIntMethod(cls, method, Args(args));
        Check(op);
        return value;
    }

    internal static void CallStaticVoid(IntPtr cls, IntPtr method, string op, JValue[] args)
    {
        AndroidJNI.CallStaticVoidMethod(cls, method, Args(args));
        Check(op);
    }

    internal static IntPtr CallStaticObject(IntPtr cls, IntPtr method, string op, JValue[] args) =>
        Track(op, AndroidJNI.CallStaticObjectMethod(cls, method, Args(args)));

    // ---- 字段读写 ----

    internal static int GetIntField(IntPtr obj, IntPtr field, string op)
    {
        var value = AndroidJNI.GetIntField(obj, field);
        Check(op);
        return value;
    }

    internal static void SetIntField(IntPtr obj, IntPtr field, int value, string op)
    {
        AndroidJNI.SetIntField(obj, field, value);
        Check(op);
    }

    internal static IntPtr GetObjectField(IntPtr obj, IntPtr field, string op) =>
        Track(op, AndroidJNI.GetObjectField(obj, field));

    internal static void SetObjectField(IntPtr obj, IntPtr field, IntPtr value, string op)
    {
        AndroidJNI.SetObjectField(obj, field, value);
        Check(op);
    }

    internal static IntPtr GetStaticObjectField(IntPtr cls, IntPtr field, string op) =>
        Track(op, AndroidJNI.GetStaticObjectField(cls, field));

    // ---- 字符串 ----

    internal static JniObject NewString(string text) =>
        JniObject.Wrap(Track("NewStringUTF", AndroidJNI.NewStringUTF(text)));

    internal static string? ReadString(IntPtr jstring)
    {
        if (jstring == IntPtr.Zero) return null;

        var text = AndroidJNI.GetStringUTFChars(jstring);
        Check("GetStringUTFChars");
        return text;
    }

    // ---- 错误 ----

    /// <summary>调用收口：有 pending 异常先清掉（不清的话下一次 JNI 调用会中止进程），再记 Java 侧文本。</summary>
    private static bool Check(string op)
    {
        var throwable = AndroidJNI.ExceptionOccurred();
        if (throwable == IntPtr.Zero) return true;

        AndroidJNI.ExceptionClear();
        _error = op + ": " + Describe(throwable);
        AndroidJNI.DeleteLocalRef(throwable);
        return false;
    }

    private static string Describe(IntPtr throwable)
    {
        var cls = AndroidJNI.GetObjectClass(throwable);
        var text = IntPtr.Zero;
        if (cls != IntPtr.Zero)
        {
            var toString = AndroidJNI.GetMethodID(cls, "toString", "()Ljava/lang/String;");
            if (toString != IntPtr.Zero) text = AndroidJNI.CallObjectMethod(throwable, toString, Args(None));
        }

        var message = text == IntPtr.Zero ? null : AndroidJNI.GetStringUTFChars(text);
        if (text != IntPtr.Zero) AndroidJNI.DeleteLocalRef(text);
        if (cls != IntPtr.Zero) AndroidJNI.DeleteLocalRef(cls);
        AndroidJNI.ExceptionClear();
        return message ?? "(Java 异常无法读取)";
    }

    // ---- 参数数组 ----

    /// <summary>jvalue 数组按长度在线程本地槽里复用；零参数也要给真数组，icall 会读数组头。</summary>
    private static Il2CppStructArray<JArg> Args(JValue[]? values)
    {
        values ??= None;

        var slots = _args;
        if (slots == null || values.Length >= slots.Length)
        {
            slots = new Il2CppStructArray<JArg>?[Math.Max(values.Length + 1, (slots?.Length ?? 0) * 2)];
            _args?.CopyTo(slots, 0);
            _args = slots;
        }

        var array = slots[values.Length] ??= NewArray(values.Length);
        for (var i = 0; i < values.Length; i++) array[i] = values[i].ToArg();
        return array;
    }

    /// <summary>Il2CppStructArray 自带的分配走了 il2cpp_array_new，在这台 il2cpp 上会崩；改走 array_new_specific。</summary>
    private static Il2CppStructArray<JArg> NewArray(int length)
    {
        var arrayClass = IL2CPP.il2cpp_array_class_get(Il2CppClassPointerStore<JArg>.NativeClassPtr, 1);
        return new Il2CppStructArray<JArg>(IL2CPP.il2cpp_array_new_specific(arrayClass, (ulong)length));
    }
}
