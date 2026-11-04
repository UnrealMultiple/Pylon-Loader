namespace Pylon.ModLoader;

/// <summary>
/// 命令表：名字（含别名）→ 命令。查表是逐条比对而不是字典命中，
/// 因为每条命令自己的 <c>IsCaseSensitive</c> 决定比较方式（见 docs/pipeline/content-command.md 3.5）。
/// </summary>
internal static class CommandTable
{
    private static readonly List<ModCommand> Commands = [];

    internal static int Count => Commands.Count;

    internal static IReadOnlyList<ModCommand> All => Commands;

    internal static ModCommand At(int index) => Commands[index];

    /// <summary>收表；幂等。名字为空或重名直接抛（作者声明的错，安装期就该炸）。</summary>
    internal static void Rebuild()
    {
        Commands.Clear();

        foreach (var command in ModContent.GetContent<ModCommand>())
        {
            if (string.IsNullOrWhiteSpace(command.Command))
                throw new InvalidOperationException($"{command.Mod.Name}/{command.Name}: 命令名是空的");

            for (var index = 0; index < Commands.Count; index++)
            {
                if (!Same(Commands[index], command.Command, command.IsCaseSensitive)) continue;
                if (!Same(command, Commands[index].Command, Commands[index].IsCaseSensitive)) continue;

                throw new InvalidOperationException(
                    $"{command.Mod.Name}/{command.Name}: 命令 `{command.Command}` 与 {Commands[index].Mod.Name}/{Commands[index].Name} 重名");
            }

            WarnAliasConflicts(command);
            Commands.Add(command);
        }
    }

    /// <summary>别名撞车只告警不抛：mod 之间没法预知彼此的别名，先注册的赢。</summary>
    private static void WarnAliasConflicts(ModCommand command)
    {
        var aliases = command.Aliases;
        if (aliases is null || aliases.Length == 0) return;

        for (var alias = 0; alias < aliases.Length; alias++)
        {
            var name = aliases[alias];
            if (string.IsNullOrWhiteSpace(name)) continue;

            for (var index = 0; index < Commands.Count; index++)
            {
                var other = Commands[index];
                if (!Same(other, name, other.IsCaseSensitive)) continue;

                LoaderHost.Current.Log.Warn(
                    $"[command] 别名 {name} 与 {other.Mod.Name}/{other.Name} 的命令名撞了：{other.Name} 先注册，别名的调用会被它接走");
            }

            if (Same(command, name, command.IsCaseSensitive))
            {
                LoaderHost.Current.Log.Warn($"[command] {command.Mod.Name}/{command.Name} 的别名 {name} 与自己的命令名相同");
            }
        }
    }

    /// <summary>按名字（不含斜杠）找命令；<paramref name="exact"/> 是输入框里的原样文本。</summary>
    internal static ModCommand? Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        for (var index = 0; index < Commands.Count; index++)
        {
            var command = Commands[index];
            if (Same(command, name, command.IsCaseSensitive)) return command;

            var aliases = command.Aliases;
            if (aliases is null) continue;

            for (var alias = 0; alias < aliases.Length; alias++)
            {
                if (Same(command, name, command.IsCaseSensitive, aliases[alias])) return command;
            }
        }

        return null;
    }

    private static bool Same(ModCommand command, string a, bool caseSensitive, string? b = null)
    {
        b ??= command.Command;

        return caseSensitive
            ? string.Equals(a, b, StringComparison.Ordinal)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
