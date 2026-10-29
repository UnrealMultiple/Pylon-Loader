using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Pylon.ModLoader
{
    /// <summary>jvalue 的 8 字节联合（C# 侧与 C++ JNI 桥的 argv 数组同布局：int/float/bool/long/引用）。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    internal struct JValue
    {
        [FieldOffset(0)] public int Int;
        [FieldOffset(0)] public float Float;
        [FieldOffset(0)] public byte Byte;      // JNI jboolean
        [FieldOffset(0)] public long Long;
        [FieldOffset(0)] public IntPtr Obj;     // jobject（全局引用句柄）

        public static JValue Of(int value) => new() { Int = value };
        public static JValue Of(float value) => new() { Float = value };
        public static JValue Of(bool value) => new() { Byte = (byte)(value ? 1 : 0) };
        public static JValue Of(IntPtr value) => new() { Obj = value };

        // params JValue[] 调用点直接用字面量（C# 不会把 int 隐式转自定义 struct，得靠运算符）
        public static implicit operator JValue(int value) => new() { Int = value };
        public static implicit operator JValue(float value) => new() { Float = value };
        public static implicit operator JValue(bool value) => new() { Byte = (byte)(value ? 1 : 0) };
        public static implicit operator JValue(IntPtr value) => new() { Obj = value };
    }

    /// <summary>
    /// Java（JNI）反射桥的 C# 侧（P/Invoke 到 libpylon.so 的 PylonMod_Jni_*）。
    /// IL2CPP 没实例化 AndroidJavaObject 的泛型 Call&lt;T&gt;，C# 反射 Java 只能走这条：
    /// 内核做 JNI 原语，这边包成对象式封装（JniObject）。
    /// 约定：拿到的句柄都是全局引用，用完 Dispose；失败原因见 LastError。
    /// </summary>
    internal static class Jni
    {
        // ---------------- P/Invoke 原语 ----------------

        [DllImport("pylon")] internal static extern int PylonMod_Jni_EnsureAttached();
        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_LastError();

        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_FindClass(string name);
        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_GetObjectClass(IntPtr obj);
        [DllImport("pylon")] internal static extern void PylonMod_Jni_DeleteGlobalRef(IntPtr global);

        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_GetMethodID(IntPtr cls, string name, string sig);
        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_GetStaticMethodID(IntPtr cls, string name, string sig);

        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_NewObject(IntPtr cls, IntPtr ctor, int argc, JValue[] argv);
        [DllImport("pylon")] internal static extern int PylonMod_Jni_CallInt(IntPtr obj, IntPtr method, int argc, JValue[] argv);
        [DllImport("pylon")] internal static extern int PylonMod_Jni_CallBoolean(IntPtr obj, IntPtr method, int argc, JValue[] argv);
        [DllImport("pylon")] internal static extern void PylonMod_Jni_CallVoid(IntPtr obj, IntPtr method, int argc, JValue[] argv);
        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_CallObject(IntPtr obj, IntPtr method, int argc, JValue[] argv);

        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_NewStringUtf(string utf8);
        [DllImport("pylon")] internal static extern IntPtr PylonMod_Jni_GetStringUtf(IntPtr jstring);
        [DllImport("pylon")] internal static extern void PylonMod_Jni_ReleaseStringUtf(IntPtr copy);
        [DllImport("pylon")] internal static extern int PylonMod_Jni_ExceptionCheck();

        // 方法 ID 缓存（会话内稳定；键 = 类引用 + 名字 + 签名 + 静态标记）
        private static readonly ConcurrentDictionary<(IntPtr, string, string, bool), IntPtr> MethodCache = new();

        /// <summary>最近一次失败原因（内核线程内缓冲；用做去重日志）。</summary>
        internal static string LastError
        {
            get
            {
                try
                {
                    var pointer = PylonMod_Jni_LastError();
                    return pointer == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pointer) ?? "";
                }
                catch { return ""; }
            }
        }

        /// <summary>UTF-8 → Java String（全局引用，用完 .Dispose()）。失败返回空句柄对象。</summary>
        internal static JniObject NewString(string text)
        {
            var handle = PylonMod_Jni_NewStringUtf(text ?? "");
            if (handle == IntPtr.Zero) JniObject.Report(nameof(NewString));
            return new JniObject(handle);
        }

        internal static IntPtr Method(IntPtr cls, string name, string sig, bool isStatic)
        {
            var key = (cls, name, sig, isStatic);
            if (MethodCache.TryGetValue(key, out var cached)) return cached;
            var method = isStatic
                ? PylonMod_Jni_GetStaticMethodID(cls, name, sig)
                : PylonMod_Jni_GetMethodID(cls, name, sig);
            if (method != IntPtr.Zero) MethodCache[key] = method;
            return method;
        }
    }

    /// <summary>
    /// Java 对象/类的句柄包装（全局引用）。持有期跨帧安全；用完必须 Dispose。
    /// 签名规则：引用 = "Ljava/lang/String;"，int = "I"，bool = "Z"，float = "F"，
    /// long = "J"，void = "V"；构造器 name = "&lt;init&gt;"。
    /// </summary>
    internal sealed class JniObject : IDisposable
    {
        internal readonly IntPtr Handle;       // jobject 全局引用
        private readonly IntPtr _givenClass;   // 构造/类对象时指定的类引用（所有权在本对象）
        private IntPtr _lazyClass;             // GetObjectClass 缓存（所有权在本对象）

        /// <summary>包一个已有全局引用（handle 所有权转给本对象）。</summary>
        internal JniObject(IntPtr handle) : this(handle, IntPtr.Zero) { }

        private JniObject(IntPtr handle, IntPtr cls)
        {
            Handle = handle;
            _givenClass = cls;
        }

        /// <summary>Java 类对象（"android/media/SoundPool" 斜杠形式）。</summary>
        internal static JniObject Class(string jniName)
        {
            var cls = Jni.PylonMod_Jni_FindClass(jniName);
            if (cls == IntPtr.Zero) Report("FindClass " + jniName);
            return new JniObject(cls, cls);
        }

        /// <summary>new 一个对象；ctorSig 形如 "()V"/"(III)V"。类引用所有权转给返回对象。</summary>
        internal static JniObject New(string jniName, string ctorSig, params JValue[] args)
        {
            var cls = Class(jniName);
            if (cls == null || cls.Handle == IntPtr.Zero) return null;

            var ctor = Jni.Method(cls.Handle, "<init>", ctorSig, isStatic: false);
            if (ctor == IntPtr.Zero)
            {
                Report($"{jniName}.<init>{ctorSig}");
                cls.Dispose();
                return null;
            }

            var obj = Jni.PylonMod_Jni_NewObject(cls.Handle, ctor, args?.Length ?? 0, args);
            if (obj == IntPtr.Zero)
            {
                Report($"{jniName}.<init>{ctorSig}");
                cls.Dispose();
                return null;
            }
            return new JniObject(obj, cls.Handle);   // 旧的 cls 引用转给新对象，Dispose 时一起释放
        }

        private IntPtr ClassRef()
        {
            if (_givenClass != IntPtr.Zero) return _givenClass;
            if (_lazyClass == IntPtr.Zero && Handle != IntPtr.Zero)
            {
                _lazyClass = Jni.PylonMod_Jni_GetObjectClass(Handle);
                if (_lazyClass == IntPtr.Zero) Report("GetObjectClass");
            }
            return _lazyClass;
        }

        internal int CallInt(string name, string sig, params JValue[] args)
        {
            var method = Jni.Method(ClassRef(), name, sig, isStatic: false);
            if (method == IntPtr.Zero) return 0;
            return Jni.PylonMod_Jni_CallInt(Handle, method, args?.Length ?? 0, args);
        }

        internal bool CallBool(string name, string sig, params JValue[] args)
        {
            var method = Jni.Method(ClassRef(), name, sig, isStatic: false);
            if (method == IntPtr.Zero) return false;
            return Jni.PylonMod_Jni_CallBoolean(Handle, method, args?.Length ?? 0, args) != 0;
        }

        internal void Call(string name, string sig, params JValue[] args)
        {
            var method = Jni.Method(ClassRef(), name, sig, isStatic: false);
            if (method == IntPtr.Zero) return;
            Jni.PylonMod_Jni_CallVoid(Handle, method, args?.Length ?? 0, args);
        }

        /// <summary>返回一个新对象（全局引用，用完 Dispose）；Java null 返回 null。</summary>
        internal JniObject CallObject(string name, string sig, params JValue[] args)
        {
            var method = Jni.Method(ClassRef(), name, sig, isStatic: false);
            if (method == IntPtr.Zero) return null;
            var obj = Jni.PylonMod_Jni_CallObject(Handle, method, args?.Length ?? 0, args);
            return obj == IntPtr.Zero ? null : new JniObject(obj);
        }

        internal string GetString(string name, string sig, params JValue[] args)
        {
            using var str = CallObject(name, sig, args);
            if (str == null) return null;
            var pointer = Jni.PylonMod_Jni_GetStringUtf(str.Handle);
            if (pointer == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(pointer); }
            finally { Jni.PylonMod_Jni_ReleaseStringUtf(pointer); }
        }

        /// <summary>本对象作为参数时的 jvalue。</summary>
        internal JValue AsArg() => JValue.Of(Handle);

        /// <summary>失败诊断：去重记一条（含内核 LastError）。</summary>
        internal static void Report(string what)
        {
            var error = Jni.LastError;
            Safe.Once("jni:" + what, $"JNI {what} 失败：{(error.Length == 0 ? "无错误信息" : error)}", true);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) Jni.PylonMod_Jni_DeleteGlobalRef(Handle);
            if (_givenClass != IntPtr.Zero && _givenClass != Handle) Jni.PylonMod_Jni_DeleteGlobalRef(_givenClass);
            if (_lazyClass != IntPtr.Zero && _lazyClass != Handle) Jni.PylonMod_Jni_DeleteGlobalRef(_lazyClass);
        }
    }
}