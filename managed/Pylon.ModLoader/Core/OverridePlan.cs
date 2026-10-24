using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    /// <summary>覆写检测：只有被覆写的钩子才值得派发（注册时判一次）。</summary>
    internal sealed class OverridePlan
    {
        private readonly HashSet<string> _overridden;

        private OverridePlan(HashSet<string> overridden) => _overridden = overridden;

        public bool Has(string hook) => _overridden.Contains(hook);
        public int Count => _overridden.Count;

        /// <summary>覆写的全部方法名（Global 注册表分派用）。</summary>
        public IEnumerable<string> Names => _overridden;

        /// <summary>按"声明类型不是基类"判定覆写；继承来的空实现不算。</summary>
        public static OverridePlan Build(Type type, Type baseType)
        {
            var overridden = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public |
                                                    System.Reflection.BindingFlags.Instance))
            {
                if (method.IsAbstract || method.IsStatic) continue;
                if (method.DeclaringType != baseType) overridden.Add(method.Name);
            }
            return new OverridePlan(overridden);
        }
    }
}
