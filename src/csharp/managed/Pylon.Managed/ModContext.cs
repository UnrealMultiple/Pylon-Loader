using System;
using System.Reflection;
using Pylon.Mod;

namespace Pylon.Managed
{
    /// <summary>宿主给 Mod 的上下文实现。</summary>
    internal sealed class ModContext : IModContext
    {
        public ModContext(string id, string version, string directory)
        {
            Id = id;
            Version = version;
            Directory = directory;
        }

        public string Id { get; }
        public string Version { get; }
        public string Directory { get; }

        /// <summary>日志前缀带上 Mod id：多 Mod 时一眼看出是谁在说话。</summary>
        public void Log(string message) => Entrypoint.Log($"[{Id}] {message}");

        /// <summary>Mod 的 ImGui Tab 交进注册表（框架菜单每帧扫它绘侧边栏+内容区）。</summary>
        public void RegisterImGuiTab(Pylon.Mod.IModImGuiTab tab)
            => Pylon.Mod.ImGuiTabRegistry.Register(tab);

        /// <summary>反射发现 Mod 类型：实现了 <see cref="IPylonMod"/> 的公开具体类。</summary>
        public static Type FindModType(Assembly assembly)
        {
            var candidates = new System.Collections.Generic.List<Type>();
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type == null || !type.IsClass || type.IsAbstract) continue;
                if (!typeof(IPylonMod).IsAssignableFrom(type)) continue;
                candidates.Add(type);
            }

            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0)
            {
                Entrypoint.Log("程序集里没有 IPylonMod 的实现（Mod 必须实现 Pylon.Mod.IPylonMod）");
                return null;
            }

            var names = new string[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) names[i] = candidates[i].FullName;
            throw new InvalidOperationException(
                $"程序集里有 {candidates.Count} 个 IPylonMod 实现，无法判断加载哪个：{string.Join(", ", names)}");
        }

        /// <summary>GetTypes 的容错版：Mod 依赖缺失时 <see cref="ReflectionTypeLoadException"/> 会带走全部类型，…</summary>
        internal static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                Entrypoint.Log($"类型枚举不完整（依赖缺失？）：{Entrypoint.Flatten(e)}");
                var types = new System.Collections.Generic.List<Type>();
                foreach (var type in e.Types) if (type != null) types.Add(type);
                return types.ToArray();
            }
        }
    }
}