using System.Collections.Generic;
using System.IO;
using Pylon.ModLoader.Content;

namespace CheatMenu.Features;

/// <summary>
/// 一个作弊模块。开关关闭时所有回调必须立刻返回，让禁用功能不产生分派成本。
/// <see cref="Install"/> 只在晚阶段调用一次；<see cref="Tick"/>/<see cref="DrawMenu"/>
/// 在游戏线程调用。
/// </summary>
public abstract class CheatFeature
{
    /// <summary>稳定标识：日志与后续配置键都用它，不要随显示名改。</summary>
    public abstract string Id { get; }

    public abstract string Title { get; }

    /// <summary>页内分类；对应原版 MenuTab。</summary>
    public abstract CheatCategory Category { get; }

    /// <summary>分类内的子页标签；对应原版 Tool::GetSubTab。</summary>
    public virtual string SubPage => "主要";

    public virtual void Install() { }

    public virtual void Tick() { }

    /// <summary>收包拦截：返回 true 表示吞掉这个包（对应原版 ProcessData hook 的 return）。</summary>
    public virtual bool OnGetData(ref byte messageType, ref BinaryReader reader, int playerNumber) => false;

    /// <summary>在当前分类/子页里画自己的卡片。</summary>
    public virtual void DrawMenu() { }

    /// <summary>往界面层列表插入常驻绘制层（每帧都画，与菜单开合无关）。</summary>
    public virtual void AddInterfaceLayers(List<GameInterfaceLayer> layers) { }
}