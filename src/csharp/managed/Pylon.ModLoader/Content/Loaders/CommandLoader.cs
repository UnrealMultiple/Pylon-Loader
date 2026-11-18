using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 命令表与分派：按名字（大小写不敏感）收集每条命令，按 CommandType 与调用方匹配；
/// 同名命令支持 "Mod:命令" 指定；/help 收集当前环境下可用的命令列表。
/// </summary>
public static class CommandLoader
{
    internal static readonly IDictionary<string, List<ModCommand>> Commands =
        new Dictionary<string, List<ModCommand>>(StringComparer.OrdinalIgnoreCase);

    static readonly Color ErrorColor = new(255, 25, 25, 255);

    static readonly Color AliasColor = new(124, 252, 0, 255);

    public static bool Matches(CommandType commandType, CommandType callerType)
    {
        if ((commandType & CommandType.World) != 0)
        {
            if ((Main.netMode & 2) != 0)
                commandType |= CommandType.Server;
            else if (Main.netMode == 0)
                commandType |= CommandType.Chat;
        }

        return (callerType & commandType) != 0;
    }

    internal static void Add(ModCommand cmd)
    {
        if (!Commands.TryGetValue(cmd.Command, out List<ModCommand>? list))
            Commands.Add(cmd.Command, list = new List<ModCommand>());
        list.Add(cmd);
    }

    internal static void Clear() => Commands.Clear();

    internal static bool GetCommand(CommandCaller caller, string name, out ModCommand? mc)
    {
        string? modName = null;
        if (name.Contains(':'))
        {
            string[] split = name.Split(':');
            modName = split[0];
            name = split[1];
        }

        mc = null;
        if (!Commands.TryGetValue(name, out List<ModCommand>? list))
            return false;
        list = list.Where(c => Matches(c.Type, caller.CommandType)).ToList();
        if (list.Count == 0)
            return false;
        if (modName != null)
        {
            Mod? mod = ModQuery.Mods.FirstOrDefault(m => string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase));
            if (mod == null && ModQuery.TryGetMod(modName, out Mod? found))
                mod = found;
            if (mod == null)
            {
                caller.Reply($"没有叫 {modName} 的 mod", ErrorColor);
            }
            else
            {
                mc = list.SingleOrDefault(c => c.Mod == mod);
                if (mc == null)
                    caller.Reply($"{modName} 里没有 /{name} 这条命令", ErrorColor);
            }
        }
        else if (list.Count > 1)
        {
            caller.Reply($"/{name} 有多条同名命令，请写成 mod:命令", ErrorColor);
            foreach (ModCommand c in list)
                caller.Reply(c.Mod.Name + ":" + c.Command, AliasColor);
        }
        else
        {
            mc = list[0];
        }

        return true;
    }

    internal static bool HandleCommand(string input, CommandCaller caller)
    {
        int sep = input.IndexOf(' ');
        string name = (sep >= 0 ? input[..sep] : input).ToLower();
        if (caller.CommandType != CommandType.Console)
        {
            if (name.Length == 0 || name[0] != '/')
                return false;
            name = name[1..];
        }

        if (!GetCommand(caller, name, out ModCommand? mc))
            return false;
        if (mc == null)
            return true;
        if (!mc.IsCaseSensitive)
            input = input.ToLower();
        string[] args = input.TrimEnd().Split(' ').Skip(1).ToArray();
        try
        {
            mc.Action(caller, input, args);
        }
        catch (Exception e)
        {
            if (e is UsageException { msg: not null } usage)
            {
                caller.Reply(usage.msg, usage.color);
            }
            else
            {
                if (e is not UsageException)
                    ContentErrors.Report($"[command] {mc.Name}.Action 异常：{Safe.Describe(e)}");
                caller.Reply("用法：" + mc.Usage, ErrorColor);
            }
        }

        return true;
    }

    public static List<Tuple<string, string>> GetHelp(CommandType type)
    {
        var list = new List<Tuple<string, string>>();
        foreach (KeyValuePair<string, List<ModCommand>> entry in Commands)
        {
            List<ModCommand> matching = entry.Value.Where(mc => Matches(mc.Type, type)).ToList();
            foreach (ModCommand mc in matching)
                list.Add(Tuple.Create(matching.Count > 1 ? mc.Mod.Name + ":" + mc.Command : mc.Command, mc.Description));
        }

        return list;
    }
}
