// 层：Mod 框架 / Core —— Hook 安装的幂等与覆写检测。
//
// Once(key, install)：同一 key 只装一次（全进程去重），多个 mod 共用同一批 hook 时不重复挂。
// Overrides(cls, base, name)：声明类型不是基类 = 覆写检测（HasOverride 最小判定），
// 是"某方法要不要分派给这个类"的唯一依据；Pylon 用 OverridePlan 注册时算好集合。
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Pylon.ModLoader
{
    /// <summary>Hook 安装的幂等门：Once 去重装 detour，Overrides 判覆写。</summary>
    internal static class Hooks
    {
        private static readonly HashSet<string> Installed = new(StringComparer.Ordinal);

        /// <summary>同一 key 的安装只执行一次（首次注册立即装，之后跳过）。</summary>
        internal static void Once(string key, Action install)
        {
            if (install == null || !Installed.Add(key)) return;
            install();
        }

        /// <summary>这个方法是不是这个类自己声明的（相对 baseType，注册期判一次）。</summary>
        internal static bool Overrides(Type cls, Type baseType, string method) =>
            cls.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)?.DeclaringType != baseType;
    }
}