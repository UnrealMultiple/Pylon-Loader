using System;
using System.IO;
using System.Linq;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace PipelineTest.Tests;

/// <summary>Install 管线：Mod / ModContent / Main.TML 的作者 API 与安装时序。</summary>
internal static class InstallTests
{
    public static void Register()
    {
        PT.Group("install", Phase.Load, RunReady);
        PT.Group("install-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("INSTALL-API-1", c =>
        {
            var mod = PipelineTestMod.Instance;
            c.True(mod.Uid.Length > 0, "Uid=" + mod.Uid);
            c.True(mod.Name.Length > 0, "Name=" + mod.Name);
            c.True(mod.DisplayName.Length > 0, "DisplayName=" + mod.DisplayName);
            c.True(mod.Version >= new Version(0, 0), "Version=" + mod.Version);
            c.True(mod.Folder.Length > 0 && File.Exists(Path.Combine(mod.Folder, "assets/items/TestItem.png")),
                "Folder=" + mod.Folder);
            c.True(mod.ContentAutoloadingEnabled && mod.BackgroundAutoloadingEnabled && mod.CloudAutoloadingEnabled,
                "autoload 默认值");
            c.NotNull(mod.Assets.Request<Texture2D>("assets/items/TestItem.png"), "Assets.Request");
            c.True(mod.Assets.HasAsset<Texture2D>("assets/items/TestItem.png"), "Assets.HasAsset");
            c.Eq(mod.GetLocalization("InstallProbe", () => "安装探针").Value, "安装探针", "GetLocalization");
            c.Eq(mod.GetLocalizationKey("InstallProbe"), "Mods." + mod.Name + ".InstallProbe", "GetLocalizationKey");
            c.True(mod.GetFileNames().Contains("assets/items/TestItem.png"), "GetFileNames");
            using (var stream = mod.GetFileStream("assets/items/TestItem.png"))
                c.True(stream.Length > 0, "GetFileStream");
            c.NotNull(mod.GetPacket(1024), "GetPacket(capacity)");
            var contents = mod.GetContent().ToList();
            c.True(contents.Count > 0 && contents.All(instance => instance.Mod == mod), "GetContent()");
            c.True(mod.GetContent<ModPlayer>().Any(), "GetContent<ModPlayer>");
        });

        PT.Case("INSTALL-API-2", c =>
        {
            var mod = PipelineTestMod.Instance;
            var item = ModContent.GetInstance<Content.TestItem>();
            c.NotNull(item, "TestItem 实例");
            c.Eq(ModContent.ItemType<Content.TestItem>(), item!.Type, "ItemType");
            var npcType = ModContent.NPCType<Content.TestTownNpc>();
            c.True(ModContent.GetModNPC(npcType) is { } npc && npc.Type == npcType, "NPCType/GetModNPC");
            c.True(ModContent.Find<ModItem>(mod.Name, "TestItem") is { } found && found.Type == item.Type,
                "Find(mod,name)");
            c.True(!ModContent.TryFind<ModItem>(mod.Name, "NotExist", out _), "TryFind 不存在");
            var (modName, path) = ModContent.SplitName(mod.Name + "/assets/items/TestItem.png");
            c.Eq(modName, mod.Name, "SplitName.mod");
            c.Eq(path, "assets/items/TestItem.png", "SplitName.path");
            c.True(ModContent.FileExists(mod.Name + "/assets/items/TestItem.png"), "FileExists");
            c.True(ModContent.GetFileBytes(mod.Name + "/assets/items/TestItem.png").Length > 0, "GetFileBytes");
            c.True(ModContent.HasAsset(mod.Name + "/assets/items/TestItem.png"), "HasAsset");
            c.NotNull(ModContent.Request<Texture2D>(mod.Name + "/assets/items/TestItem.png"), "Request<T>");
            c.True(ModContent.RequestIfExists<Texture2D>(mod.Name + "/assets/items/TestItem.png", out var existing),
                "RequestIfExists 存在");
            c.NotNull(existing, "RequestIfExists asset");
            c.True(!ModContent.RequestIfExists<Texture2D>(mod.Name + "/assets/items/NoSuch.png", out _),
                "RequestIfExists 不存在");
        });

        PT.Case("INSTALL-API-3", c =>
        {
            var manual = PipelineTestMod.ManualContent;
            c.NotNull(manual, "AddContent 实例");
            c.True(PipelineTestMod.Instance.GetContent<PipelineTestMod.ManualInstallContent>().Contains(manual!),
                "AddContent 进内容表");
            c.True(ReferenceEquals(manual!.Mod, PipelineTestMod.Instance), "AddContent 绑定 Mod");
            c.Eq(manual.Marker, 7, "构造参数保留");
        });

        PT.Case("INSTALL-API-4", c =>
        {
            c.True(PipelineTestMod.TownHeadSlot >= 0, "AddNPCHeadTexture 槽=" + PipelineTestMod.TownHeadSlot);
            c.True(PipelineTestMod.BossHeadSlot >= 0, "AddBossHeadTexture 槽=" + PipelineTestMod.BossHeadSlot);
        });
    }

    private static void RunWorld()
    {
        PT.Case("INSTALL-W1", c =>
        {
            c.True(GameContent.Installed, "GameContent.Installed");
            c.True(GameContent.UnavailableReason is null, "UnavailableReason=" + GameContent.UnavailableReason);

            var town = ModContent.NPCType<Content.TestTownNpc>();
            c.Eq(NPC.TypeToDefaultHeadIndex(town), PipelineTestMod.TownHeadSlot, "城镇头像槽生效");
            var boss = ModContent.NPCType<Content.TestBossNpc>();
            c.Eq(NPCID.Sets.BossHeadTextures[boss], PipelineTestMod.BossHeadSlot, "Boss 头像槽生效");

            c.Eq(Main.DiscoColor, new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB), "Main.DiscoColor");
            c.NotNull(Main.ActiveProjectiles, "Main.ActiveProjectiles");
            foreach (var projectile in Main.ActiveProjectiles) _ = projectile.active;
            c.NoThrow(() => Main.NewText("Main.TML 字符串探针", new Color(1, 2, 3)), "Main.NewText(string)");
            c.NoThrow(() => Main.NewText((object)"Main.TML 对象探针"), "Main.NewText(object)");
        });
    }
}
