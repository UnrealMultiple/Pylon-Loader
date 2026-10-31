namespace Pylon.ModLoader;

/// <summary>Java 类句柄的薄封装（全局 jclass 引用 + 静态成员访问）。命名规则同 <see cref="JniObject"/>。</summary>
internal sealed class JniClass : IDisposable
{
    private bool _disposed;

    private JniClass(IntPtr handle, string name)
    {
        Handle = handle;
        Name = name;
    }

    internal IntPtr Handle { get; }

    internal string Name { get; }

    /// <summary>按 "android/view/SurfaceView" 形式取类；失败返回 null。</summary>
    internal static JniClass? Find(string jniName)
    {
        var cls = Jni.FindClass(jniName);
        if (cls == IntPtr.Zero)
        {
            JniObject.Report("FindClass " + jniName);
            return null;
        }
        return new JniClass(cls, jniName);
    }

    internal int CallStaticInt(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Handle, name, signature, isStatic: true);
        return method == IntPtr.Zero ? 0 : Jni.CallStaticInt(Handle, method, args);
    }

    internal JniObject? CallStaticObject(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Handle, name, signature, isStatic: true);
        if (method == IntPtr.Zero) return null;

        var handle = Jni.CallStaticObject(Handle, method, args);
        return handle == IntPtr.Zero ? null : new JniObject(handle);
    }

    internal void CallStaticVoid(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(Handle, name, signature, isStatic: true);
        if (method != IntPtr.Zero) Jni.CallStaticVoid(Handle, method, args);
    }

    internal JniObject? GetStaticObject(string name, string signature)
    {
        var field = Jni.Field(Handle, name, signature, isStatic: true);
        if (field == IntPtr.Zero) return null;

        var handle = Jni.GetStaticObjectField(Handle, field);
        return handle == IntPtr.Zero ? null : new JniObject(handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Jni.DeleteGlobalRef(Handle);
    }
}
