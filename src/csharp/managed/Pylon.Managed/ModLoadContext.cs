using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Pylon.Managed
{
    /// <summary>一个 Mod 一个加载上下文，依赖交给官方的 <see cref="AssemblyDependencyResolver"/> 解析。</summary>
    internal sealed class ModLoadContext : AssemblyLoadContext
    {
        private readonly string _modId;
        private readonly string _dir;
        private readonly AssemblyDependencyResolver _resolver;

        public ModLoadContext(string modId, string modDllPath)
            : base($"pylon-mod:{modId}", isCollectible: false)  // 不可回收：补丁与 interop 的 native 侧都被钉住
        {
            _modId = modId;
            _dir = Path.GetDirectoryName(modDllPath) ?? ".";
            var deps = Path.ChangeExtension(modDllPath, ".deps.json");
            _resolver = File.Exists(deps) ? new AssemblyDependencyResolver(modDllPath) : null;
        }

        protected override Assembly Load(AssemblyName name)
        {
            if (name?.Name == null) return null;
            if (HostAssemblies.Contains(name.Name)) return null;  // 宿主优先：Mod 不许遮蔽

            var path = _resolver?.ResolveAssemblyToPath(name) ?? Probe(name.Name + ".dll");
            if (path == null)
            {
                Entrypoint.Log($"[{_modId}] 依赖未找到：{name.Name}（找过 {_dir} 与 lib/）");
                return null;  // 交回默认上下文报错，运行时的报错信息更全
            }
            Entrypoint.Log($"[{_modId}] 依赖解析：{name.Name} ← {path}");
            return LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            if (HostNatives.TryResolve(unmanagedDllName, out var host)) return host;

            var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName)
                       ?? Probe("lib" + unmanagedDllName + ".so")
                       ?? Probe(unmanagedDllName + ".so");
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }

        /// <summary>Mod 目录 → `Mod/lib/`。</summary>
        private string Probe(string fileName)
        {
            var direct = Path.Combine(_dir, fileName);
            if (File.Exists(direct)) return direct;
            var inLib = Path.Combine(_dir, "lib", fileName);
            return File.Exists(inLib) ? inLib : null;
        }
    }
}
