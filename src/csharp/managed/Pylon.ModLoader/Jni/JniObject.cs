namespace Pylon.ModLoader.Jni;

/// <summary>
/// Java 对象句柄（全局引用），持有期跨帧安全，用完 Dispose
/// </summary>
internal sealed class JniObject : JniRef
{
    private JniObject(IntPtr handle, IntPtr cls = default) : base(handle, cls) { }

    internal static JniObject Wrap(IntPtr handle, IntPtr cls = default) => new(handle, cls);
    internal static JniObject? New(string jniName, string ctorSignature, params JValue[] args)
    {
        var cls = JniBridge.FindClass(jniName);
        if (cls == IntPtr.Zero) return null;

        var op = jniName + ".<init>" + ctorSignature;
        var ctor = JniBridge.Method(cls, "<init>", ctorSignature, isStatic: false);
        var handle = ctor == IntPtr.Zero ? IntPtr.Zero : JniBridge.NewObject(cls, ctor, op, args);
        if (handle == IntPtr.Zero)
        {
            JniBridge.Release(cls);
            return null;
        }

        return new JniObject(handle, cls);
    }

    internal void Call(string name, string signature, params JValue[] args)
    {
        var method = JniBridge.Method(Class, name, signature, isStatic: false);
        if (method != IntPtr.Zero) JniBridge.CallVoid(Handle, method, name + signature, args);
    }

    internal int CallInt(string name, string signature, params JValue[] args)
    {
        var method = JniBridge.Method(Class, name, signature, isStatic: false);
        return method == IntPtr.Zero ? 0 : JniBridge.CallInt(Handle, method, name + signature, args);
    }

    internal bool CallBool(string name, string signature, params JValue[] args)
    {
        var method = JniBridge.Method(Class, name, signature, isStatic: false);
        return method != IntPtr.Zero && JniBridge.CallBoolean(Handle, method, name + signature, args);
    }

    internal JniObject? CallObject(string name, string signature, params JValue[] args)
    {
        var method = JniBridge.Method(Class, name, signature, isStatic: false);
        if (method == IntPtr.Zero) return null;

        var handle = JniBridge.CallObject(Handle, method, name + signature, args);
        return handle == IntPtr.Zero ? null : Wrap(handle);
    }

    internal void Chain(string name, string signature, params JValue[] args) =>
        CallObject(name, signature, args)?.Dispose();

    internal string? GetString(string name, string signature)
    {
        using var value = CallObject(name, signature);
        return value == null ? null : JniBridge.ReadString(value.Handle);
    }

    internal int GetInt(string name, string signature)
    {
        var field = JniBridge.Field(Class, name, signature, isStatic: false);
        return field == IntPtr.Zero ? 0 : JniBridge.GetIntField(Handle, field, name);
    }

    internal void SetInt(string name, string signature, int value)
    {
        var field = JniBridge.Field(Class, name, signature, isStatic: false);
        if (field != IntPtr.Zero) JniBridge.SetIntField(Handle, field, value, name);
    }

    internal JniObject? GetObject(string name, string signature)
    {
        var field = JniBridge.Field(Class, name, signature, isStatic: false);
        if (field == IntPtr.Zero) return null;

        var handle = JniBridge.GetObjectField(Handle, field, name);
        return handle == IntPtr.Zero ? null : Wrap(handle);
    }

    internal void SetObject(string name, string signature, JniObject? value)
    {
        var field = JniBridge.Field(Class, name, signature, isStatic: false);
        if (field != IntPtr.Zero) JniBridge.SetObjectField(Handle, field, value?.Handle ?? IntPtr.Zero, name);
    }

    internal JValue AsArg() => Handle;
}
