using Pylon.Mod;

namespace Pylon.ModLoader;

/// <summary>带 Mod 前缀的日志。等级只体现在前缀上，便于按等级 grep。</summary>
public sealed class ModLogger(IModContext context) : IModLog
{
    public void Info(string message) => context.Log(message);

    public void Warn(string message) => context.Log("警告：" + message);

    public void Error(string message) => context.Log("错误：" + message);
}
