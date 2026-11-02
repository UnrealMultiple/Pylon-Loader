namespace Pylon.ModLoader;

/// <summary>Java 类的静态边（全局引用），用完 Dispose。</summary>
internal sealed class JniClass : JniRef
{
    private readonly string _name;

    private JniClass(IntPtr handle, string name) : base(handle, handle) => _name = name;

    internal static JniClass? Find(string jniName)
    {
        var cls = Jni.FindClass(jniName);
        return cls == IntPtr.Zero ? null : new JniClass(cls, jniName);
    }

    internal void CallStaticVoid(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Handle, name, signature, isStatic: true);
        if (method != IntPtr.Zero) Jni.CallStaticVoid(Handle, method, _name + "." + name + signature, args);
    }

    internal int CallStaticInt(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Handle, name, signature, isStatic: true);
        return method == IntPtr.Zero ? 0 : Jni.CallStaticInt(Handle, method, _name + "." + name + signature, args);
    }

    internal JniObject? CallStaticObject(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Handle, name, signature, isStatic: true);
        if (method == IntPtr.Zero) return null;

        var handle = Jni.CallStaticObject(Handle, method, _name + "." + name + signature, args);
        return handle == IntPtr.Zero ? null : JniObject.Wrap(handle);
    }

    internal JniObject? GetStaticObject(string name, string signature)
    {
        var field = Jni.Field(Handle, name, signature, isStatic: true);
        if (field == IntPtr.Zero) return null;

        var handle = Jni.GetStaticObjectField(Handle, field, _name + "." + name);
        return handle == IntPtr.Zero ? null : JniObject.Wrap(handle);
    }
}
