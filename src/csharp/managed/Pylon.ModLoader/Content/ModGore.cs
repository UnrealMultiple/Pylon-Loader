using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 自定义血腥粒子（gore）：一个类型一粒模板，类型号接在原版表后面。
/// 贴图是单张 png（`assets/gores/&lt;类名&gt;.png`），由安装管线写进 TextureAssets.Gore。
/// </summary>
public abstract class ModGore : ModType
{
    /// <summary>贴图相对 Mod 目录的路径（默认 assets/gores/&lt;类名&gt;.png）。</summary>
    public override string TexturePath => $"assets/gores/{Name}.png";

    /// <summary>粒子生成之后一次（位置与速度已经定好）。</summary>
    public virtual void OnSpawn(Gore gore) { }

    /// <summary>每帧；返回 false = 作者接管这一粒，游戏不再跑原版更新。</summary>
    public virtual bool Update(Gore gore) => true;

    /// <summary>颜色；返回 null = 用原版算出来的颜色。</summary>
    public virtual Color? GetAlpha(Gore gore, Color lightColor) => null;


    /// <summary>
    /// 借用某个原版 gore 的每帧动作：作者自己写 Update 太麻烦时，填一个原版编号（例如 <c>GoreID.WaterDrip</c>）。
    /// 只在 <see cref="Update"/> 返回 true（没接管）时生效；0 = 不借。
    /// </summary>
    public virtual int UpdateType => 0;

    /// <summary>
    /// 按名字取 gore 编号：支持 <c>Mod名/类名</c> 与"全工程唯一"的裸类名；找不到或重名返回 0（不抛）。
    /// </summary>
    public static int GetTypeByName(string name) => GoreTable.Find(name);
}

// Gores/*.png 自动发现（只放图片不会产生类型）、网络同步（粒子是纯表现）。