using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Pylon.ModLoader.Patch
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class GamePatchAttribute(Type type, string method) : Attribute
    {
        public Type Type { get; } = type ?? throw new ArgumentNullException(nameof(type));
        public string Method { get; } = method ?? throw new ArgumentNullException(nameof(method));
        public int ParamCount { get; set; } = -1;
        public bool? Static { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PreAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PostAttribute : Attribute { }

    public static class GamePatcher
    {
        private static readonly List<Type> Installed = [];

        public static void Install(Harmony harmony, Action<string> log, params Assembly[] assemblies)
        {
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                if (types == null) continue;

                foreach (var type in types)
                {
                    if (type == null || type.GetCustomAttribute<GamePatchAttribute>() == null) continue;
                    InstallOne(harmony, log, type);
                }
            }
        }

        private static void InstallOne(Harmony harmony, Action<string> log, Type patchType)
        {
            if (Installed.Contains(patchType)) return;
            Installed.Add(patchType);

            var attr = patchType.GetCustomAttribute<GamePatchAttribute>();
            var target = Find(attr.Type, attr.Method, attr.ParamCount, attr.Static);
            if (target == null)
            {
                log?.Invoke($"补丁：找不到 {attr.Type.Name}.{attr.Method}" +
                            (attr.ParamCount >= 0 ? $"（{attr.ParamCount} 参）" : ""));
                return;
            }

            var pre = FindHook(patchType, typeof(PreAttribute));
            var post = FindHook(patchType, typeof(PostAttribute));
            if (pre == null && post == null)
            {
                log?.Invoke($"补丁：{patchType.Name} 里没有 [Pre]/[Post] 方法");
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix: pre != null ? new HarmonyMethod(pre) : null,
                    postfix: post != null ? new HarmonyMethod(post) : null);
                log?.Invoke($"补丁：已挂 {attr.Type.Name}.{target.Name} ← {patchType.Name}");
            }
            catch (Exception e)
            {
                var inner = e.InnerException;
                var detail = inner != null ? inner.ToString() : e.ToString();
                var lines = detail.Split('\n');
                log?.Invoke($"补丁：挂 {attr.Type.Name}.{target.Name} 失败：{lines[0].Trim()}");
                foreach (var l in lines.Skip(1).Take(6)) log?.Invoke($"  {l.Trim()}");
            }
        }

        private static MethodInfo Find(Type type, string name, int paramCount, bool? isStatic)
        {
            foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Static | BindingFlags.Instance))
            {
                if (candidate.Name != name) continue;
                if (paramCount >= 0 && candidate.GetParameters().Length != paramCount) continue;
                if (isStatic.HasValue && candidate.IsStatic != isStatic.Value) continue;
                return candidate;
            }
            return null;
        }

        private static MethodInfo FindHook(Type patchType, Type attrType)
        {
            foreach (var m in patchType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic |
                                                   BindingFlags.Public))
            {
                if (m.GetCustomAttribute(attrType) != null) return m;
            }
            return null;
        }
    }
}