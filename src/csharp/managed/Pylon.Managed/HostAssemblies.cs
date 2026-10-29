using System;
using System.Collections.Generic;
using System.IO;

namespace Pylon.Managed
{
    /// <summary>"哪些程序集由宿主提供" —— 判据是 **TPA 本身**（TRUSTED_PLATFORM_ASSEMBLIES），不是硬编码的前缀表。</summary>
    internal static class HostAssemblies
    {
        private static readonly HashSet<string> Names = Build();

        public static int Count => Names.Count;

        public static bool Contains(string simpleName) =>
            !string.IsNullOrEmpty(simpleName) && Names.Contains(simpleName);

        private static HashSet<string> Build()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string tpa) return set;
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(name)) set.Add(name);
            }
            return set;
        }
    }
}
