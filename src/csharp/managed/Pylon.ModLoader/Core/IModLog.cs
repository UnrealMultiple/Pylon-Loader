namespace Pylon.ModLoader;

/// <summary>框架的日志出口。宿主在启动时注入实现；没有实现时静默丢弃。</summary>
public interface IModLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}
