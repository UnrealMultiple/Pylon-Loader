// 层：Mod 框架 / Core —— 统一 Hook 安装（Once 幂等 + 安装分发）。
//
// 收拢各 Loader 的反射脚手架：声明表 → 找目标方法（参数类型或参数个数）→ 幂等挂 prefix/postfix，
// 找不到/挂失败只记一条日志，行为与原实现一致。
//
// 幂等键 = 目标方法 + 前后置 + 处理器名：同方法可被多个 Loader 各挂补丁，同键才去重。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Pylon.ModLoader
{
    /// <summary>统一 Hook 安装：声明表 → 找方法 → 幂等挂 prefix/postfix，失败只记一条日志。</summary>
    internal static class GameHooks
    {
        /// <summary>一条补丁声明：找 Type.Method，按 Args（或 ParamCount）匹配，挂 Handler 作为前缀/后缀。</summary>
        internal sealed class Hook
        {
            public Type Type;
            public string Method;

            /// <summary>精确参数类型；为 null 时按 ParamCount 找。</summary>
            public Type[] Args;

            /// <summary>Args 为 null 时按参数个数找（重载同名方法时最省事）。</summary>
            public int ParamCount = -1;

            /// <summary>true = 挂 prefix，false = 挂 postfix。</summary>
            public bool Prefix;

            /// <summary>补丁方法名（ownerType 里的 NonPublic|Static 方法）。</summary>
            public string Handler;

            public string Label => $"{Type?.Name}.{Method}({(Args != null ? Args.Length : ParamCount)} 参)";
        }

        private static readonly HashSet<string> Installed = new(StringComparer.Ordinal);

        /// <summary>按声明表安装（幂等：同一目标方法 + 前后置 + 处理器的组合只挂一次）。</summary>
        internal static void Install(Harmony harmony, ModLogger logger, Type ownerType, IEnumerable<Hook> hooks)
        {
            if (harmony == null || ownerType == null || hooks == null) return;
            foreach (var hook in hooks) InstallOne(harmony, logger, ownerType, hook);
        }

        private static void InstallOne(Harmony harmony, ModLogger logger, Type ownerType, Hook hook)
        {
            if (hook == null || hook.Type == null || string.IsNullOrEmpty(hook.Method)) return;

            var target = hook.Args != null
                ? Find(hook.Type, hook.Method, hook.Args)
                : Find(hook.Type, hook.Method, hook.ParamCount);
            if (target == null)
            {
                logger?.Warn($"[{ownerType.Name}] 找不到 {hook.Label}，对应钩子不可用");
                return;
            }

            var handler = ownerType.GetMethod(hook.Handler, BindingFlags.Static | BindingFlags.NonPublic);
            if (handler == null)
            {
                logger?.Warn($"[{ownerType.Name}] 找不到补丁方法 {hook.Handler}");
                return;
            }

            var key = (target.DeclaringType?.FullName ?? target.Name) + "::" + target.Name + "/" +
                      target.GetParameters().Length + (hook.Prefix ? "+pre" : "+post") +
                      ":" + ownerType.Name + "." + hook.Handler;
            if (!Installed.Add(key)) return;

            try
            {
                var hm = new HarmonyMethod(handler);
                if (hook.Prefix) harmony.Patch(target, prefix: hm);
                else harmony.Patch(target, postfix: hm);
            }
            catch (Exception e)
            {
                logger?.Warn($"[{ownerType.Name}] 挂 {hook.Type.Name}.{target.Name} 失败：{e.Message}");
            }
        }

        /// <summary>给一个已经找到的方法挂补丁（特殊签名 / GamePatcher 用）。</summary>
        internal static void Patch(Harmony harmony, ModLogger logger, Type ownerType, MethodInfo target,
                                   string prefixHandler = null, string postfixHandler = null)
        {
            if (harmony == null || target == null || ownerType == null) return;
            PatchByReflection(harmony, logger, ownerType, target, prefixHandler, postfixHandler);
        }

        /// <summary>按类型/名字/参数找目标并挂补丁（等价于声明表的单条版）。</summary>
        internal static void Patch(Harmony harmony, ModLogger logger, Type ownerType,
                                   Type type, string method, Type[] args,
                                   string prefixHandler = null, string postfixHandler = null)
        {
            var target = Find(type, method, args);
            if (target == null)
            {
                logger?.Warn($"[{ownerType.Name}] 找不到 {type.Name}.{method}({(args?.Length ?? 0)} 参)，对应钩子不可用");
                return;
            }
            PatchByReflection(harmony, logger, ownerType, target, prefixHandler, postfixHandler);
        }

        private static void PatchByReflection(Harmony harmony, ModLogger logger, Type ownerType, MethodInfo target,
                                              string prefixHandler, string postfixHandler)
        {
            try
            {
                var prefix = prefixHandler != null
                    ? new HarmonyMethod(ownerType.GetMethod(prefixHandler, BindingFlags.Static | BindingFlags.NonPublic))
                    : null;
                var postfix = postfixHandler != null
                    ? new HarmonyMethod(ownerType.GetMethod(postfixHandler, BindingFlags.Static | BindingFlags.NonPublic))
                    : null;
                if (prefix?.method == null && postfix?.method == null)
                {
                    logger?.Warn($"[{ownerType.Name}] 找不到补丁方法 {prefixHandler ?? postfixHandler}");
                    return;
                }
                harmony.Patch(target, prefix: prefix, postfix: postfix);
            }
            catch (Exception e)
            {
                logger?.Warn($"[{ownerType.Name}] 挂 {target.DeclaringType?.Name}.{target.Name} 失败：{e.Message}");
            }
        }

        /// <summary>按精确参数类型找（重载同名方法时最可靠）。</summary>
        internal static MethodInfo Find(Type type, string name, Type[] args)
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                var ok = true;
                for (var i = 0; i < args.Length; i++)
                    if (ps[i].ParameterType != args[i]) { ok = false; break; }
                if (ok) return m;
            }
            return null;
        }

        /// <summary>按参数个数找（默认静态 + 实例都找；staticOnly 时只找静态）。</summary>
        internal static MethodInfo Find(Type type, string name, int paramCount, bool staticOnly = false)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            if (staticOnly) flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var m in type.GetMethods(flags))
            {
                if (m.Name != name) continue;
                if (m.GetParameters().Length != paramCount) continue;
                return m;
            }
            return null;
        }
    }
}
