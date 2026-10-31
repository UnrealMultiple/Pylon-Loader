using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace Pylon.ModLoader;

/// <summary>内容基类：一个实例 = 一个自定义内容。阶段：Register（占号）→ SetStaticDefaults → SetDefaults。</summary>
public abstract class ModType
{
    public Mod Mod { get; internal set; } = null!;

    /// <summary>类型名，默认取类名。</summary>
    public virtual string Name => GetType().Name;

    /// <summary>游戏内 ID（注册时分配）。</summary>
    public int Type { get; internal set; }

    public bool Registered { get; internal set; }

    /// <summary>内建贴图：晚阶段按 <see cref="TexturePath"/> 上传（失败或没写路径时为 null）。</summary>
    public Asset<Texture2D> Texture { get; internal set; } = null!;

    /// <summary>贴图相对 Mod 目录的路径；null = 不要贴图。</summary>
    public virtual string TexturePath => null!;

    internal OverridePlan Plan { get; set; } = null!;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public virtual void SetStaticDefaults() { }
}
