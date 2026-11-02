namespace Pylon.ModLoader;

/// <summary>一个 Java 全局引用的持有者：谁拿到谁 Dispose。</summary>
internal abstract class JniRef : IDisposable
{
    private IntPtr _handle;
    private IntPtr _class;

    protected JniRef(IntPtr handle, IntPtr cls = default)
    {
        _handle = handle;
        _class = cls;
    }

    internal IntPtr Handle => _handle;

    /// <summary>本对象的类引用；第一次用到时才查。</summary>
    protected IntPtr Class
    {
        get
        {
            if (_class == IntPtr.Zero && _handle != IntPtr.Zero) _class = Jni.ObjectClass(_handle);
            return _class;
        }
    }

    public void Dispose()
    {
        var handle = _handle;
        if (handle == IntPtr.Zero) return;

        _handle = IntPtr.Zero;
        Jni.Release(handle);
        if (_class != IntPtr.Zero && _class != handle) Jni.Release(_class);
        _class = IntPtr.Zero;
    }
}
