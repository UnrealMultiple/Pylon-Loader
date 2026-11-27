using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.ImGuiHost;
using Pylon.NaviteAPI.ImGui;

namespace CheatMenu.Features;

/// <summary>
/// 一个作弊模块。开关关闭时所有回调必须立刻返回，让禁用功能不产生分派成本。
/// <see cref="Install"/> 只在晚阶段调用一次；<see cref="Tick"/>/<see cref="DrawMenu"/>
/// 在游戏线程调用。
/// </summary>
public abstract class CheatFeature
{
    /// <summary>本功能注册过的常驻绘制 key，卸载时逐一撤销（只在安装期分配）。</summary>
    private readonly List<string> _overlayKeys = new();

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

    public virtual void DrawWorldTiles(SpriteBatch batch) { }

    // ==================== 常驻绘制 ====================

    protected void RegisterOverlay(string key, Action<ImDrawListPtr> draw, bool background = true)
    {
        ModOverlay.Register(key, draw, background);
        if (!_overlayKeys.Contains(key)) _overlayKeys.Add(key);
    }

    public virtual void Uninstall()
    {
        for (var i = 0; i < _overlayKeys.Count; i++) ModOverlay.Unregister(_overlayKeys[i]);
        _overlayKeys.Clear();
    }

    public static void UninstallAll()
    {
        foreach (var feature in CheatFeatureHost.All)
        {
            try { feature.Uninstall(); }
            catch (Exception e) { CheatMenuMod.Log?.Warn("[cheat] " + feature.Id + " 反注册常驻绘制失败：" + e.Message); }
        }
    }

    // ==================== 共用换算（热路径：只做位运算 / 结构体拷贝，不分配） ====================

    /// <summary>
    /// 配置色是 <c>0xAARRGGBB</c>；ImGui 的 <c>ImU32</c> 内存布局是 <c>0xAABBGGRR</c>
    /// （等价 <c>IM_COL32(r, g, b, a)</c>），这里按通道重排后可直接交给 AddRect/AddLine。
    /// </summary>
    protected static uint ToImU32(uint argb) =>
        (argb & 0xFF000000u)                       // A 原位
        | ((argb & 0x000000FFu) << 16)             // B → 16 位
        | (argb & 0x0000FF00u)                      // G 原位
        | ((argb & 0x00FF0000u) >> 16);             // R → 0 位

    /// <summary>Il2Cpp 的 XNA <c>Vector2</c> → ImGui 用的 <see cref="System.Numerics.Vector2"/>（值拷贝）。</summary>
    protected static Vector2 ImVec(Il2CppMicrosoft.Xna.Framework.Vector2 v) => new(v.X, v.Y);
}

/// <summary>
/// 卸载收口：Mod 卸载 / 热重载时撤销全部常驻绘制注册。
/// 框架按 ModType 自动发现本类（见 RegistrationPipeline），所以反注册不依赖任何外部调用点；
/// 若将来在 CheatMenuMod.Unload / CheatRuntime.OnModUnload 里显式调用
/// <see cref="CheatFeature.UninstallAll"/>，两边都幂等，不会重复或冲突。
/// </summary>
public sealed class OverlayUnloadSystem : ModSystem
{
    // 显式无参构造：注册管线用 Activator.CreateInstance 实例化，要求 public 无参构造。
    public OverlayUnloadSystem() { }

    public override void OnModUnload() => CheatFeature.UninstallAll();
}