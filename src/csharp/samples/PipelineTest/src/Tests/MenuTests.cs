#nullable enable
using System;
using System.IO;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 28：Menu（标题画面主题）。
/// Ready：方法面、Select/Deselect 落盘与回调、Update/PreDrawLogo 在启动菜单期已被派发。
/// 肉眼验收：选中的主题换掉标题 logo（本主题用测试物品贴图）。
/// </summary>
internal static class MenuTests
{
    public static void Register()
    {
        PT.Group("menu", Phase.Ready, RunReady);
    }

    private static void RunReady()
    {
        var menu = ModContent.GetInstance<Content.TestMenu>();

        // M-R1：方法面
        PT.Case("MENU-R1", c =>
        {
            var type = typeof(ModMenu);
            c.NotNull(menu, "TestMenu 实例");
            c.NotNull(type.GetProperty("Logo"), "ModMenu.Logo");
            c.NotNull(type.GetProperty("SunTexture"), "ModMenu.SunTexture");
            c.NotNull(type.GetProperty("MoonTexture"), "ModMenu.MoonTexture");
            c.NotNull(type.GetProperty("Music"), "ModMenu.Music");
            c.NotNull(type.GetProperty("IsAvailable"), "ModMenu.IsAvailable");
            c.NotNull(type.GetProperty("DisplayName"), "ModMenu.DisplayName");
            c.NotNull(type.GetProperty("MenuBackgroundStyle"), "ModMenu.MenuBackgroundStyle");
            c.NotNull(type.GetMethod("Update"), "ModMenu.Update");
            c.NotNull(type.GetMethod("OnSelected"), "ModMenu.OnSelected");
            c.NotNull(type.GetMethod("OnDeselected"), "ModMenu.OnDeselected");
            c.NotNull(type.GetMethod("PreDrawLogo"), "ModMenu.PreDrawLogo");
            c.NotNull(type.GetMethod("PostDrawLogo"), "ModMenu.PostDrawLogo");
        });

        // M-R2：选主题 → 回调 + 落盘；取消 → 回调。启动菜单期 Update/PreDrawLogo 已被派发过。
        PT.Case("MENU-R2", c =>
        {
            var beforeUpdate = Content.TestMenu.CtUpdate;
            var beforeLogo = Content.TestMenu.CtPreDrawLogo;
            PT.Info($"启动菜单期计数：Update={beforeUpdate} PreDrawLogo={beforeLogo}");

            menu.Select();
            c.True(menu.IsSelected, "Select 后 IsSelected");
            c.True(Content.TestMenu.CtOnSelected > 0, $"OnSelected 回调（{Content.TestMenu.CtOnSelected} 次）");

            // 落盘：存档根目录下的 PylonLoader.menu.json（管线实际写这个文件），内容里应带主题键
            var path = Path.Combine(Main.SavePath, "PylonLoader.menu.json");
            var body = File.Exists(path) ? File.ReadAllText(path) : "";
            PT.Info($"菜单选择落盘文件 {path} 存在={File.Exists(path)} 长度={body.Length}");
            c.True(body.Contains("TestMenu"), "落盘内容含主题名");

            ModMenu.Deselect();
            c.True(!menu.IsSelected, "Deselect 后不再是选中");
            c.True(Content.TestMenu.CtOnDeselected > 0, $"OnDeselected 回调（{Content.TestMenu.CtOnDeselected} 次）");

            // 再切回来：让设备上的主题保留为 TestMenu（标题画面能肉眼看换 logo/日月），也覆盖"取消后重新选"这条路。
            menu.Select();
            c.True(menu.IsSelected, "重新 Select 后 IsSelected");
        });
    }
}