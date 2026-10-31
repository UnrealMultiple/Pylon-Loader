namespace Pylon.ModLoader;

/// <summary>
/// Java 对象句柄（全局引用）。持有期跨帧安全，用完必须 Dispose。
/// 签名规则：引用 "Ljava/lang/String;"，int "I"，bool "Z"，float "F"，long "J"，void "V"；
/// 构造器名字是 "&lt;init&gt;"。
/// </summary>
internal sealed class JniObject : IDisposable
{
    private readonly IntPtr _givenClass;
    private IntPtr _lazyClass;
    private bool _disposed;

    internal JniObject(IntPtr handle, IntPtr cls = default)
    {
        Handle = handle;
        _givenClass = cls;
    }

    internal IntPtr Handle { get; }

    /// <summary>Java 类对象（"android/media/SoundPool" 斜杠形式）。</summary>
    internal static JniObject Class(string jniName)
    {
        var cls = Jni.FindClass(jniName);
        if (cls == IntPtr.Zero) Report("FindClass " + jniName);
        return new JniObject(cls, cls);
    }

    /// <summary>new 一个对象；类引用所有权转给返回对象。失败返回 null。</summary>
    internal static JniObject? New(string jniName, string ctorSignature, params JValue[] args)
    {
        var cls = Jni.FindClass(jniName);
        if (cls == IntPtr.Zero)
        {
            Report("FindClass " + jniName);
            return null;
        }

        var ctor = Jni.Method(cls, "<init>", ctorSignature, isStatic: false);
        if (ctor == IntPtr.Zero)
        {
            Report($"{jniName}.<init>{ctorSignature}");
            Jni.DeleteGlobalRef(cls);
            return null;
        }

        var handle = Jni.NewObject(cls, ctor, args);
        if (handle == IntPtr.Zero)
        {
            Report($"{jniName}.<init>{ctorSignature}");
            Jni.DeleteGlobalRef(cls);
            return null;
        }

        return new JniObject(handle, cls);
    }

    private IntPtr ClassRef()
    {
        if (_givenClass != IntPtr.Zero) return _givenClass;
        if (_lazyClass == IntPtr.Zero && Handle != IntPtr.Zero)
        {
            _lazyClass = Jni.GetObjectClass(Handle);
            if (_lazyClass == IntPtr.Zero) Report("GetObjectClass");
        }
        return _lazyClass;
    }

    internal int CallInt(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(ClassRef(), name, signature, isStatic: false);
        return method == IntPtr.Zero ? 0 : Jni.CallInt(Handle, method, args);
    }

    internal bool CallBool(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(ClassRef(), name, signature, isStatic: false);
        return method != IntPtr.Zero && Jni.CallBoolean(Handle, method, args);
    }

    internal void Call(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(ClassRef(), name, signature, isStatic: false);
        if (method != IntPtr.Zero) Jni.CallVoid(Handle, method, args);
    }

    /// <summary>返回新对象（全局引用，用完 Dispose）；Java null 返回 null。</summary>
    internal JniObject? CallObject(string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(ClassRef(), name, signature, isStatic: false);
        if (method == IntPtr.Zero) return null;

        var handle = Jni.CallObject(Handle, method, args);
        return handle == IntPtr.Zero ? null : new JniObject(handle);
    }

    // 接口方法（如 SurfaceHolder.getSurface）用运行时类查不到 MethodID，必须按声明它的接口查。

    internal JniObject? CallObjectOn(JniClass declaring, string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(declaring.Handle, name, signature, isStatic: false);
        if (method == IntPtr.Zero)
        {
            Report(declaring.Name + "." + name);
            return null;
        }

        var handle = Jni.CallObject(Handle, method, args);
        return handle == IntPtr.Zero ? null : new JniObject(handle);
    }

    internal bool CallBoolOn(JniClass declaring, string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(declaring.Handle, name, signature, isStatic: false);
        if (method == IntPtr.Zero)
        {
            Report(declaring.Name + "." + name);
            return false;
        }
        return Jni.CallBoolean(Handle, method, args);
    }

    internal void CallOn(JniClass declaring, string name, string signature, params JValue[] args)
    {
        var method = Jni.Method(declaring.Handle, name, signature, isStatic: false);
        if (method == IntPtr.Zero)
        {
            Report(declaring.Name + "." + name);
            return;
        }
        Jni.CallVoid(Handle, method, args);
    }

    internal string? GetString(string name, string signature, params JValue[] args)
    {
        using var value = CallObject(name, signature, args);
        return value is null ? null : Jni.ReadString(value.Handle);
    }

    internal int GetIntField(string name, string signature)
    {
        var field = Jni.Field(ClassRef(), name, signature, isStatic: false);
        return field == IntPtr.Zero ? 0 : Jni.GetIntField(Handle, field);
    }

    internal void SetIntField(string name, string signature, int value)
    {
        var field = Jni.Field(ClassRef(), name, signature, isStatic: false);
        if (field != IntPtr.Zero) Jni.SetIntField(Handle, field, value);
    }

    internal void SetFieldObject(string name, string signature, JniObject? value)
    {
        var field = Jni.Field(ClassRef(), name, signature, isStatic: false);
        if (field != IntPtr.Zero) Jni.SetObjectField(Handle, field, value?.Handle ?? IntPtr.Zero);
    }

    /// <summary>读引用字段（新全局引用，用完 Dispose）；null 字段返回 null。</summary>
    internal JniObject? GetFieldObject(string name, string signature)
    {
        var field = Jni.Field(ClassRef(), name, signature, isStatic: false);
        if (field == IntPtr.Zero) return null;

        var handle = Jni.GetObjectField(Handle, field);
        return handle == IntPtr.Zero ? null : new JniObject(handle);
    }

    /// <summary>本对象作为参数时的 jvalue。</summary>
    internal JValue AsArg() => JValue.Of(Handle);

    /// <summary>失败诊断：按调用点去重记一条（含内核 LastError）。</summary>
    internal static void Report(string what)
    {
        var error = Jni.LastError;
        Safe.Once("jni:" + what, $"JNI {what} 失败：{(error.Length == 0 ? "无错误信息" : error)}", true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Jni.DeleteGlobalRef(Handle);
        if (_givenClass != Handle) Jni.DeleteGlobalRef(_givenClass);
        if (_lazyClass != Handle && _lazyClass != _givenClass) Jni.DeleteGlobalRef(_lazyClass);
    }
}
