using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// 城镇 NPC：搬进空房、名字表、房间条件、聊天按钮与商店。
/// 与敌对 NPC 共享内容基类，但游戏时机完全不重叠（一天一次入住 / 一次点击），所以单独成文件。
/// </summary>
internal sealed class NpcTown
{
    private const int Merchant = 17;

    private NPC? _talking;
    private int _talkingType;

    public void Install()
    {
        On.Terraria.Main.UpdateTime_SpawnTownNPCs += Spawn;
        On.Terraria.WorldGen.CheckSpecialTownNPCSpawningConditions += CheckConditions;
        On.Terraria.NPC.getNewNPCName += NewName;
        On.GUINPCDialogue.SetupButtonText += Buttons;
        On.GUINPCDialogue.Option1Clicked += Option1;
        On.GUINPCDialogue.Option2Clicked += Option2;
    }

    /// <summary>安装期一次：作者的 AddShops 写进 NpcShop 的静态表。</summary>
    public void SetupShops(IModLog log)
    {
        foreach (var npc in ModContent.GetContent<ModNpc>())
        {
            if (!npc.HookEnabled(HookId.AddShops)) continue;

            var shops = new List<NpcShop>();
            try
            {
                npc.AddShops(shops);
            }
            catch (Exception e)
            {
                log.Warn($"{npc.Name} 的 AddShops 失败：{e.Message}");
            }
        }
    }

#region 入住

    private void Spawn(On.Terraria.Main.orig_UpdateTime_SpawnTownNPCs orig)
    {
        orig();

        // 世界加载后 NPCID.Sets 的字典会重建：每次刷怪检查时补齐（已存在就跳过）。
        NpcPipeline.EnsureNpcTypeTables();

        if (Main.netMode == 1 || Main.checkForSpawns != 0) return;

        var townCount = -1;
        foreach (var npc in ModContent.GetContent<ModNpc>())
        {
            if (!npc.HookEnabled(HookId.CanTownNPCSpawn)) continue;
            if (NPC.AnyNPCs(npc.Type)) continue;

            if (townCount < 0) townCount = CountTownNpcs();

            var can = false;
            try
            {
                can = npc.CanTownNPCSpawn(townCount);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"{npc.Name}.CanTownNPCSpawn 异常：{Safe.Describe(e)}");
            }

            if (!can) continue;

            Main.townNPCCanSpawn[npc.Type] = true;
            if (WorldGen.prioritizedTownNPCType == 0) WorldGen.prioritizedTownNPCType = npc.Type;
        }
    }

    private static int CountTownNpcs()
    {
        var count = 0;
        var npcs = Main.npc;
        if (npcs is null) return count;

        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is not null && npc.active && npc.townNPC && npc.type != NPCID.TravellingMerchant) count++;
        }

        return count;
    }

    private bool CheckConditions(On.Terraria.WorldGen.orig_CheckSpecialTownNPCSpawningConditions orig, int type)
    {
        if (ModContent.Find(ContentKind.Npc, type) is not ModNpc npc || !npc.HookEnabled(HookId.CheckConditions))
            return orig(type);

        try
        {
            var can = npc.CheckConditions(WorldGen.roomX1, WorldGen.roomX2, WorldGen.roomY1, WorldGen.roomY2);
            return can && orig(type);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"{npc.Name}.CheckConditions 异常：{Safe.Describe(e)}");
            return orig(type);
        }
    }

    private string NewName(On.Terraria.NPC.orig_getNewNPCName orig, int npcType)
    {
        if (ModContent.Find(ContentKind.Npc, npcType) is not ModNpc npc || !npc.HookEnabled(HookId.SetNPCNameList))
            return orig(npcType);

        try
        {
            var names = npc.SetNPCNameList();
            if (names is null || names.Length == 0) return orig(npcType);

            return names[Main.rand.Next(names.Length)];
        }
        catch (Exception e)
        {
            ContentErrors.Report($"{npc.Name}.SetNPCNameList 异常：{Safe.Describe(e)}");
            return orig(npcType);
        }
    }

#endregion

#region 聊天

    private static NPC? TalkingTo()
    {
        var player = Main.player[Main.myPlayer];
        var index = player?.talkNPC ?? -1;
        if (index < 0 || Main.npc is null || index >= Main.npc.Length) return null;

        var npc = Main.npc[index];
        if (npc is null || !npc.active) return null;

        return ModContent.Find(ContentKind.Npc, npc.type) is ModNpc ? npc : null;
    }

    /// <summary>按钮文字在 orig 之后覆盖；游戏按类型 switch 选按钮图，所以先按商人跑一遍拿图。</summary>
    private void Buttons(On.GUINPCDialogue.orig_SetupButtonText orig, GUINPCDialogue self,
                         ref string focusText, ref Texture2D option1Tex, ref string focusText3,
                         ref Texture2D option2Tex, ref int cost, ref bool showHappiness)
    {
        _talking = TalkingTo();


        if (_talking is not null)
        {
            _talkingType = _talking.type;
            _talking.type = Merchant;
        }

        orig(self, ref focusText, ref option1Tex, ref focusText3, ref option2Tex, ref cost, ref showHappiness);

        var npc = _talking;
        _talking = null;
        if (npc is null) return;

        npc.type = _talkingType;

        if (ModContent.Find(ContentKind.Npc, npc.type) is not ModNpc modNpc || !modNpc.HookEnabled(HookId.SetChatButtons))
            return;

        var buttons = new ModNpc.ChatButtons();
        try
        {
            modNpc.SetChatButtons(npc, buttons);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"{modNpc.Name}.SetChatButtons 异常：{Safe.Describe(e)}");
        }

        var icon = option1Tex;
        focusText = buttons.Button ?? "";
        focusText3 = buttons.Button2 ?? "";
        // 空按钮要显式传 null 让游戏不画图标：签名不可为 null，但游戏本身接受 null。
        // CS8601 在这里是误报（ref 参数的可空标注），按项目"警告即错误"策略就地抑制。
#pragma warning disable CS8601
        option1Tex = string.IsNullOrEmpty(buttons.Button) ? Nothing : icon;
        option2Tex = string.IsNullOrEmpty(buttons.Button2) ? Nothing : icon;
#pragma warning restore CS8601
        cost = 0;
    }

    private void Option1(On.GUINPCDialogue.orig_Option1Clicked orig, GUINPCDialogue self, int healCost)
    {
        if (!Clicked(true)) orig(self, healCost);
    }

    private void Option2(On.GUINPCDialogue.orig_Option2Clicked orig, GUINPCDialogue self)
    {
        if (!Clicked(false)) orig(self);
    }

    /// <summary>签名不可为 null，但游戏本身接受 null；用属性绕开 CS8601（警告即错误）。</summary>
    private static Texture2D Nothing => null!;

    /// <summary>返回 true = 这次点击被 mod 接走（不再走原版）。</summary>
    private static bool Clicked(bool first)
    {
        var npc = TalkingTo();
        if (npc is null) return false;

        if (ModContent.Find(ContentKind.Npc, npc.type) is not ModNpc modNpc) return false;
        if (!modNpc.HookEnabled(HookId.OnChatButtonClicked)) return false;

        string shopName;
        try
        {
            shopName = modNpc.OnChatButtonClicked(npc, first);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"{modNpc.Name}.OnChatButtonClicked 异常：{Safe.Describe(e)}");
            return true;
        }

        if (shopName is null) return true;

        var shop = NpcShop.Find(npc.type, shopName);
        if (shop is null)
        {
            Safe.Once("shop." + npc.type + "." + shopName,
                $"{modNpc.Name} 的聊天按钮要开商店 '{shopName}'，但没有注册", true);
            return true;
        }

        shop.Open();
        return true;
    }
#endregion

}