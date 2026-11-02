namespace Pylon.ModLoader;

/// <summary>
/// Java 对象句柄（全局引用），持有期跨帧安全，用完 Dispose。
/// 签名：引用 "Ljava/lang/String;"，int "I"，bool "Z"，float "F"，long "J"，void "V"；构造器名是 "&lt;init&gt;"。
/// </summary>
internal sealed class JniObject : JniRef
{
    private JniObject(IntPtr handle, IntPtr cls = default) : base(handle, cls) { }

    /// <summary>接管一个全局引用（不查类，用到再查）。</summary>
    internal static JniObject Wrap(IntPtr handle, IntPtr cls = default) => new(handle, cls);

    /// <summary>new 一个 Java 对象；类引用所有权交给返回值。失败返回 null。</summary>
    internal static JniObject? New(string jniName, string ctorSignature, params JValue[] args)
    {
        var cls = Jni.FindClass(jniName);
        if (cls == IntPtr.Zero) return null;

        var op = jniName + ".<init>" + ctorSignature;
        var ctor = Jni.Method(cls, "<init>", ctorSignature, isStatic: false);
        var handle = ctor == IntPtr.Zero ? IntPtr.Zero : Jni.NewObject(cls, ctor, op, args);
        if (handle == IntPtr.Zero)
        {
            Jni.Release(cls);
            return null;
        }

        return new JniObject(handle, cls);
    }

    internal void Call(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Class, name, signature, isStatic: false);
        if (method != IntPtr.Zero) Jni.CallVoid(Handle, method, name + signature, args);
    }

    internal int CallInt(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Class, name, signature, isStatic: false);
        return method == IntPtr.Zero ? 0 : Jni.CallInt(Handle, method, name + signature, args);
    }

    internal bool CallBool(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Class, name, signature, isStatic: false);
        return method != IntPtr.Zero && Jni.CallBoolean(Handle, method, name + signature, args);
    }

    internal JniObject? CallObject(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Class, name, signature, isStatic: false);
        if (method == IntPtr.Zero) return null;

        var handle = Jni.CallObject(Handle, method, name + signature, args);
        return handle == IntPtr.Zero ? null : Wrap(handle);
    }

    /// <summary>builder 风格的方法：返回的就是自己，丢掉即可。</summary>
    internal void Chain(string name, string signature, params JValue[] args) =>
        CallObject(name, signature, args)?.Dispose();

    internal string? GetString(string name, string signature)
    {
        using var value = CallObject(name, signature);
        return value == null ? null : Jni.ReadString(value.Handle);
    }

    internal int GetInt(string name, string signature)
    {
        var field = Jni.Field(Class, name, signature, isStatic: false);
        return field == IntPtr.Zero ? 0 : Jni.GetIntField(Handle, field, name);
    }

    internal void SetInt(string name, string signature, int value)
    {
        var field = Jni.Field(Class, name, signature, isStatic: false);
        if (field != IntPtr.Zero) Jni.SetIntField(Handle, field, value, name);
    }

    internal JniObject? GetObject(string name, string signature)
    {
        var field = Jni.Field(Class, name, signature, isStatic: false);
        if (field == IntPtr.Zero) return null;

        var handle = Jni.GetObjectField(Handle, field, name);
        return handle == IntPtr.Zero ? null : Wrap(handle);
    }

    internal void SetObject(string name, string signature, JniObject? value)
    {
        var field = Jni.Field(Class, name, signature, isStatic: false);
        if (field != IntPtr.Zero) Jni.SetObjectField(Handle, field, value?.Handle ?? IntPtr.Zero, name);
    }

    /// <summary>本对象作为参数时的 jvalue。</summary>
    internal JValue AsArg() => Handle;
}
