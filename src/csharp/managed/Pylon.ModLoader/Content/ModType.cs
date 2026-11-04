using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace Pylon.ModLoader;

/// <summary>
/// 内容基类：一个实例 = 一个自定义内容。
/// 生命线：注册占号 → SetStaticDefaults（表定型后）→ SetDefaults（每次实例化）。
/// </summary>
public abstract class ModType
{
    public Mod Mod { get; internal set; } = null!;

    public virtual string Name => GetType().Name;

    /// <summary>"Mod名/类型名"：注册表与网络包都按这个定位内容（跨设备唯一，和发号无关）。</summary>
    public string FullKey => Mod is null || Mod.Name.Length == 0 ? Name : Mod.Name + "/" + Name;

    /// <summary>游戏内 ID，注册时分配。</summary>
    public int Type { get; internal set; }

    public bool Registered { get; internal set; }

    /// <summary>内建贴图，安装期按 <see cref="TexturePath"/> 上传。</summary>
    public Asset<Texture2D> Texture { get; internal set; } = null!;

    /// <summary>贴图相对 Mod 目录的路径；null = 不要贴图。</summary>
    public virtual string TexturePath => null!;

    /// <summary>相对 ModType 覆写了哪些钩子（位集，注册时算一次）。</summary>
    internal HookMask Plan { get; set; }

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public virtual void SetStaticDefaults() { }
}
