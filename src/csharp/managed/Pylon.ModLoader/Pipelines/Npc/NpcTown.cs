using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.ID;
using NPCInteraction = Pylon.ModLoader.Contracts.NPCInteraction;

namespace Pylon.ModLoader.Pipelines;
/// <summary>城镇 NPC：搬进空房、名字表、房间条件、聊天按钮与商店；与敌对 NPC 共享基类但游戏时机不重叠。</summary>
internal sealed class NpcTown
{
    private const int Merchant = 17;

    private NPC? _talking;
    private int _talkingType;
    private readonly Dictionary<int, NPCInteractionList> _interactions = [];

    public void Install()
    {
        On.Terraria.Main.UpdateTime_SpawnTownNPCs += Spawn;
        On.Terraria.WorldGen.CheckSpecialTownNPCSpawningConditions += CheckConditions;
        On.Terraria.NPC.getNewNPCName += NewName;
        On.GUINPCDialogue.SetupButtonText += Buttons;
        On.GUINPCDialogue.Option1Clicked += Option1;
        On.GUINPCDialogue.Option2Clicked += Option2;
    }

    public void SetupShops(IModLog log)
    {
        foreach (var npc in ModContent.GetContent<ModNpc>())
        {
            if (!npc.HookEnabled(HookId.AddShops)) continue;

            try
            {
                npc.AddShops();
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
                ContentErrors.Line($"{npc.Name}.CanTownNPCSpawn 异常：{Safe.Dump(e)}");
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
            ContentErrors.Line($"{npc.Name}.CheckConditions 异常：{Safe.Dump(e)}");
            return orig(type);
        }
    }

    private string NewName(On.Terraria.NPC.orig_getNewNPCName orig, int npcType)
    {
        var npc = ModContent.Find(ContentKind.Npc, npcType) as ModNpc;
        var globalNames = GlobalNpcRegistry.AnyWith(HookId.ModifyNPCNameList);
        if ((npc is null || !npc.HookEnabled(HookId.SetNPCNameList)) && !globalNames) return orig(npcType);

        List<string> names = [];
        if (npc is not null && npc.HookEnabled(HookId.SetNPCNameList))
        {
            try
            {
                names = npc.SetNPCNameList() ?? [];
            }
            catch (Exception e)
            {
                ContentErrors.Line($"{npc.Name}.SetNPCNameList 异常：{Safe.Dump(e)}");
            }
        }

        var fallback = names.Count == 0 ? orig(npcType) : null;
        if (fallback is not null) names.Add(fallback);

        if (globalNames)
        {
            NPC? sample = null;
            if (ContentSamples.NpcsByNetId is { } samples && samples.TryGetValue(npcType, out var found)) sample = found;
            if (sample is null)
            {
                sample = new NPC();
                sample.SetDefaults(npcType, default);
            }

            foreach (var global in GlobalNpcRegistry.For(sample, HookId.ModifyNPCNameList))
            {
                try { global.ModifyNPCNameList(sample, names); }
                catch (Exception e) { ContentErrors.Line($"{global.Name}.ModifyNPCNameList 异常：{Safe.Dump(e)}"); }
            }
        }

        names.RemoveAll(string.IsNullOrEmpty);
        return names.Count == 0 ? fallback ?? orig(npcType) : names[Main.rand.Next(names.Count)];
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

        var list = InteractionsOf(npc);
        var first = Choice(npc, list, true);
        var second = Choice(npc, list, false);
        if (first is null && second is null) return;

        var icon = option1Tex;
        focusText = TextOf(first);
        focusText3 = TextOf(second);
#pragma warning disable CS8601
        option1Tex = first is null ? Nothing : icon;
        option2Tex = second is null ? Nothing : icon;
#pragma warning restore CS8601
        cost = 0;
    }

    private NPCInteractionList InteractionsOf(NPC npc)
    {
        if (_interactions.TryGetValue(npc.type, out var cached)) return cached;

        var list = new NPCInteractionList(npc.type);
        var mod = ModOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.SetChatButtons))
        {
            list.Append(new LegacyButton(first: true));
            list.Append(new LegacyButton(first: false));
        }

        if (mod is not null && mod.HookEnabled(HookId.RegisterChatButtons))
        {
            try { mod.RegisterChatButtons(list); }
            catch (Exception e) { ContentErrors.Line($"{mod.Name}.RegisterChatButtons 异常：{Safe.Dump(e)}"); }
        }

        foreach (var global in GlobalNpcRegistry.For(npc, HookId.RegisterChatButtons))
        {
            try { global.RegisterChatButtons(npc, list); }
            catch (Exception e) { ContentErrors.Line($"{global.Name}.RegisterChatButtons 异常：{Safe.Dump(e)}"); }
        }

        _interactions[npc.type] = list;
        return list;
    }

    private static ModNpc? ModOf(NPC? npc) =>
        npc is null ? null : ModContent.Find(ContentKind.Npc, npc.type) as ModNpc;

    private static NPCInteraction? Choice(NPC npc, NPCInteractionList list, bool first)
    {
        var wanted = first ? 0 : 1;
        foreach (var entry in list.Entries)
        {
            if (!entry.Enabled || entry.NPCInteraction is null) continue;
            try
            {
                if (!entry.NPCInteraction.Condition() || string.IsNullOrEmpty(entry.NPCInteraction.GetText())) continue;
                if (wanted-- == 0) return entry.NPCInteraction;
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[npc] NPCInteraction {entry.NPCInteraction.GetType().Name} 异常：{Safe.Dump(e)}");
            }
        }

        return null;
    }

    private static string TextOf(NPCInteraction? interaction)
    {
        if (interaction is null) return "";
        try { return interaction.GetText() ?? ""; }
        catch { return ""; }
    }

    private static bool PreChatButtonClicked(NPC npc, NPCInteraction interaction)
    {
        var allowed = true;
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.PreChatButtonClicked))
        {
            try
            {
                if (!global.PreChatButtonClicked(npc, interaction)) allowed = false;
            }
            catch (Exception e) { ContentErrors.Line($"{global.Name}.PreChatButtonClicked 异常：{Safe.Dump(e)}"); }
        }

        return allowed;
    }

    private static void OnChatButtonClicked(NPC npc, NPCInteraction interaction)
    {
        var mod = ModOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.OnChatButtonClicked))
        {
            try { mod.OnChatButtonClicked(interaction); }
            catch (Exception e) { ContentErrors.Line($"{mod.Name}.OnChatButtonClicked 异常：{Safe.Dump(e)}"); }
        }

        foreach (var global in GlobalNpcRegistry.For(npc, HookId.OnChatButtonClicked))
        {
            try { global.OnChatButtonClicked(npc, interaction); }
            catch (Exception e) { ContentErrors.Line($"{global.Name}.OnChatButtonClicked 异常：{Safe.Dump(e)}"); }
        }
    }

    private sealed class LegacyButton(bool first) : NPCInteraction
    {
        public override bool Condition() => !string.IsNullOrEmpty(GetText());

        public override string GetText()
        {
            var mod = ModOf(TalkingTo());
            if (mod is null || !mod.HookEnabled(HookId.SetChatButtons)) return "";

            var button = "";
            var button2 = "";
            try { mod.SetChatButtons(ref button, ref button2); }
            catch (Exception e) { ContentErrors.Line($"{mod.Name}.SetChatButtons 异常：{Safe.Dump(e)}"); }
            return first ? button ?? "" : button2 ?? "";
        }

        public override void Interact()
        {
            var npc = TalkingTo();
            if (npc is null) return;

            var mod = ModOf(npc);
            if (mod is null) return;

            var shopName = "";
            try { mod.OnChatButtonClicked(first, ref shopName); }
            catch (Exception e) { ContentErrors.Line($"{mod.Name}.OnChatButtonClicked 异常：{Safe.Dump(e)}"); }

            if (string.IsNullOrEmpty(shopName)) return;

            var shop = NpcShopDatabase.Find(npc.type, shopName);
            if (shop is null)
            {
                Safe.Once("shop." + npc.type + "." + shopName,
                    $"{mod.Name} 的聊天按钮要开商店 '{shopName}'，但没有注册", true);
                return;
            }

            shop.Open();
        }
    }

    private void Option1(On.GUINPCDialogue.orig_Option1Clicked orig, GUINPCDialogue self, int healCost)
    {
        if (!Clicked(true)) orig(self, healCost);
    }

    private void Option2(On.GUINPCDialogue.orig_Option2Clicked orig, GUINPCDialogue self)
    {
        if (!Clicked(false)) orig(self);
    }

    private static Texture2D Nothing => null!;

    private bool Clicked(bool first)
    {
        var npc = TalkingTo();
        if (npc is null) return false;

        var interaction = Choice(npc, InteractionsOf(npc), first);
        if (interaction is null) return false;

        if (!PreChatButtonClicked(npc, interaction)) return true;

        try { interaction.Interact(); }
        catch (Exception e) { ContentErrors.Line($"[npc] NPCInteraction {interaction.GetType().Name}.Interact 异常：{Safe.Dump(e)}"); }

        OnChatButtonClicked(npc, interaction);
        return true;
    }
    #endregion

}