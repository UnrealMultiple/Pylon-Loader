// 层：框架 / 触屏按钮 —— tModLoader 的 KeybindLoader 兼容壳：
// RegisterKeybind 返回同一个 ModTouchButton；默认键收下但不生效（手机没键盘，玩家在控制编辑器里放它）。
using System;

namespace Pylon.ModLoader
{
    /// <summary>tModLoader 兼容的按键注册（拿到的是同一个 ModTouchButton，玩家在游戏控制编辑器里放它）。</summary>
    public static class KeybindLoader
    {
        /// <summary>注册一个"按键"；默认键接受但不生效（手机版按钮在控制编辑器里放置）。</summary>
        public static ModTouchButton RegisterKeybind(Mod mod, string name, string defaultKey = "") =>
            TouchButtonLoader.RegisterButton(mod, name, null);
    }
}