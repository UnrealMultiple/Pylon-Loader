using Pylon.ModLoader.Tables;
using System;
using Terraria.ID;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>装备贴图管线：收表、按部位扩表、发号、写贴图槽位；效果与套装钩子挂玩家更新入口。</summary>
internal sealed class EquipPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static EquipPipeline Instance { get; } = new();

    public string Name => "equip";

    public IReadOnlyList<TableRequest> Tables => EquipRequests;

    private static readonly TableRequest[] EquipRequests = BuildEquipRequests();

    private static TableRequest[] BuildEquipRequests()
    {
        var requests = new List<TableRequest>(EquipTable.Parts.Length + 4);
        foreach (var part in EquipTable.Parts)
        {
            var item = part;
            requests.Add(new TableRequest(EquipTable.Set(item), () => EquipTable.Target(item)));
            foreach (var companion in EquipTable.CompanionSets(item))
                requests.Add(new TableRequest(companion, () => EquipTable.Target(item)));
        }
        return requests.ToArray();
    }


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.UpdateEquips += UpdateEquips;
        On.Terraria.Player.UpdateArmorSets += UpdateArmorSets;
        On.Terraria.Player.ApplyEquipFunctional += ApplyEquipFunctional;
        On.Terraria.Player.ApplyEquipVanity += ApplyEquipVanity;
        On.Terraria.Player.SetMatch += SetMatch;
        On.Terraria.Player.WingFrame += WingFrame;
        On.Terraria.Player.WingMovement += WingMovement;
        On.Terraria.Player.WingAirLogicTweaks += WingAirLogicTweaks;
        On.Terraria.UI.ItemSlot.CanEquipBothAccessories += CanEquipBothAccessories;

        Log.Info("[equip] 已挂（15 个部位：编号 / 扩表 / 写贴图 / 效果回调 / 套装 / 时装套 / SetMatch / 翅膀动画）");
        return true;
    }

    private static void ApplyEquipFunctional(On.Terraria.Player.orig_ApplyEquipFunctional orig, Player self, int itemSlot, Item currentItem)
    {
        orig(self, itemSlot, currentItem);
        Effects(self, currentItem, vanity: false);
    }

    private static void ApplyEquipVanity(On.Terraria.Player.orig_ApplyEquipVanity orig, Player self, int itemSlot, Item currentItem)
    {
        orig(self, itemSlot, currentItem);
        Effects(self, currentItem, vanity: true);
    }

    private static void Effects(Player player, Item? item, bool vanity)
    {
        if (item is null || item.IsAir) return;

        var slots = EquipTable.SlotsOfItem(item.type);
        if (slots is null) return;

        foreach (var (part, slot) in slots)
        {
            if (EquipTable.OfSlot(part, slot) is not { } equip) continue;

            try
            {
                if (vanity)
                {
                    equip.Item?.UpdateVanity(player);
                }
                else
                {
                    equip.Item?.UpdateEquip(player);
                    equip.Item?.UpdateAccessory(player, hideVisual: false);
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {equip.Name} 的效果回调异常：{Safe.Describe(e)}");
            }
        }
    }

    private static bool CanEquipBothAccessories(On.Terraria.UI.ItemSlot.orig_CanEquipBothAccessories orig,
        Item acc1, Item acc2, bool vanity)
    {
        if (!orig(acc1, acc2, vanity)) return false;

        if (ModAccessory(acc1) is { } mod1 && ModAccessory(acc2) is { } mod2)
        {
            try
            {
                return mod1.CanAccessoryBeEquippedWith(acc1, acc2, Main.LocalPlayer)
                    && mod2.CanAccessoryBeEquippedWith(acc2, acc1, Main.LocalPlayer);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] CanAccessoryBeEquippedWith 异常：{Safe.Describe(e)}");
                return true;
            }
        }

        return true;
    }

    private static ModItem? ModAccessory(Item? item)
    {
        if (item is null || item.IsAir) return null;
        return ModContent.Find(ContentKind.Item, item.type) as ModItem;
    }

    private static int SetMatch(On.Terraria.Player.orig_SetMatch orig, Terraria.Player.SetMatchRequest request, ref bool somethingSpecial)
    {
        var result = orig(request, ref somethingSpecial);

        var slot = request.ArmorSlotRequested switch
        {
            0 => request.Head,
            1 => request.Body,
            _ => request.Legs,
        };

        if (EquipTable.OfSlot(request.ArmorSlotRequested == 0 ? EquipType.Head
                            : request.ArmorSlotRequested == 1 ? EquipType.Body : EquipType.Legs, slot) is not { } equip)
            return result;

        var equipSlot = result;
        var robes = somethingSpecial;
        try
        {
            equip.SetMatch(request.Male, ref equipSlot, ref robes);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] {equip.Name}.SetMatch 异常：{Safe.Describe(e)}");
        }

        somethingSpecial = robes;
        return equipSlot;
    }


    private static Item? EquippedWings(Player player, int logic)
    {
        var armor = player.armor;
        if (armor is null) return null;

        var end = Math.Min(10, armor.Length);
        for (var index = 3; index < end; index++)
        {
            var item = armor[index];
            if (item is null || item.IsAir) continue;
            if (item.wingSlot != logic) continue;
            return item;
        }

        return null;
    }

    private static void WingMovement(On.Terraria.Player.orig_WingMovement orig, Player self)
    {
        var logic = self.wingsLogic;
        if (EquipTable.OfSlot(EquipType.Wing, logic) is not { } equip)
        {
            orig(self);
            return;
        }

        if (logic == 4 && self.TryingToHoverUp)
        {
            orig(self);
            return;
        }

        try
        {
            var jump = Player.jumpSpeed;
            var constant = 0.1f;
            var falling = 0.5f;
            var maxAscent = 1.5f;
            var maxCan = 0.5f;
            var rising = 0.1f;

            SeedVanilla(self, logic, ref falling, ref rising, ref maxCan, ref maxAscent, ref constant);

            equip.VerticalWingSpeeds(self, ref falling, ref rising, ref maxCan, ref maxAscent, ref constant);

            if (logic is 44 or 45) Hover(self, jump);

            var grav = self.gravDir;
            var y = self.velocity.Y - constant * grav;

            if (grav == 1f)
            {
                if (y > 0f) y -= falling;
                else if (y > -jump * maxCan) y -= rising;
                if (y < -jump * maxAscent) y = -jump * maxAscent;
            }
            else
            {
                if (y < 0f) y += falling;
                else if (y < jump * maxCan) y += rising;
                if (y > jump * maxAscent) y = jump * maxAscent;
            }

            var velocity = self.velocity;
            velocity.Y = y;
            self.velocity = velocity;

            var slowDown = logic is 22 or 28 or 30 or 31 or 37 or 45 && self.TryingToHoverDown &&
                           !self.controlLeft && !self.controlRight;
            self.wingTime -= slowDown ? 0.5f : 1f;
            if (self.empressBrooch && self.wingTime != 0f) self.wingTime = self.wingTimeMax;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] {equip.Name} 的 VerticalWingSpeeds 异常：{Safe.Describe(e)}");
            orig(self);
        }
    }

    private static void SeedVanilla(Player player, int logic, ref float falling, ref float rising, ref float maxCan,
                                    ref float maxAscent, ref float constant)
    {
        if (logic is 26 or 37)
        {
            falling = 0.75f;
            rising = 0.15f;
            maxCan = 1f;
            maxAscent = 2.5f;
            constant = 0.125f;
        }

        if (logic is 8 or 11 or 24 or 27 or 22) maxAscent = 1.66f;
        if (logic is 21 or 12 or 20 or 23) maxAscent = 1.805f;

        if (logic == 44)
        {
            falling = 0.85f;
            rising = 0.15f;
            maxCan = 1f;
            maxAscent = 2.75f;
            constant = 0.125f;
        }

        if (logic == 45)
        {
            falling = 0.95f;
            rising = 0.15f;
            maxCan = 1f;
            maxAscent = 4.5f;
        }

        if (logic is 29 or 32)
        {
            falling = 0.85f;
            rising = 0.15f;
            maxCan = 1f;
            maxAscent = 3f;
            constant = 0.135f;
        }

        if (logic is 30 or 31)
        {
            maxCan = 1f;
            maxAscent = 2.45f;
            if (!player.TryingToHoverDown) constant = 0.15f;
        }
    }

    private static void Hover(Player player, float jump)
    {
        if (player.TryingToHoverUp)
        {
            var grav = player.gravDir;
            var y = player.velocity.Y - 0.4f * grav;

            if (grav == 1f)
            {
                if (y > 0f) y -= 1f;
                else if (y > -jump) y -= 0.2f;
                if (y < -jump * 3f) y = -jump * 3f;
            }
            else
            {
                if (y < 0f) y += 1f;
                else if (y < jump) y += 0.2f;
                if (y > jump * 3f) y = jump * 3f;
            }

            var velocity = player.velocity;
            velocity.Y = y;
            player.velocity = velocity;
        }

        if (player.TryingToHoverDown && !player.controlJump && player.velocity.Y != 0f)
        {
            var velocity = player.velocity;
            velocity.Y += 0.4f;
            player.velocity = velocity;
        }
    }

    private static void WingAirLogicTweaks(On.Terraria.Player.orig_WingAirLogicTweaks orig, Player self)
    {
        orig(self);

        var logic = self.wingsLogic;
        if (self.wings <= 0) return;
        if (EquipTable.OfSlot(EquipType.Wing, logic) is not { } equip) return;

        try
        {
            var speed = self.accRunSpeed;
            var acceleration = self.runAcceleration;
            equip.HorizontalWingSpeeds(self, ref speed, ref acceleration);

            self.accRunSpeed = speed;
            self.runAcceleration = acceleration;
            self.maxRunSpeed = speed;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] {equip.Name} 的 HorizontalWingSpeeds 异常：{Safe.Describe(e)}");
        }
    }

    private static void WingFrame(On.Terraria.Player.orig_WingFrame orig, Player self, bool wingFlap)
    {
        var wings = self.wings;
        if (wings > 0 && EquipTable.OfSlot(EquipType.Wing, wings) is { } equip)
        {
            try
            {
                if (equip.WingUpdate(self, wingFlap)) return;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {equip.Name}.WingUpdate 异常：{Safe.Describe(e)}");
            }
        }

        orig(self, wingFlap);
    }

    private static void UpdateEquips(On.Terraria.Player.orig_UpdateEquips orig, Player self, int i)
    {
        orig(self, i);

        if (EquipTable.Count == 0) return;

        EnsureWingStats(self);

        var armor = self.armor;
        if (armor is null || armor.Length < 3) return;

        var head = armor[0]?.headSlot ?? -1;
        var body = armor[1]?.bodySlot ?? -1;
        var legs = armor[2]?.legSlot ?? -1;

        Frame(EquipType.Head, head, self);
        Frame(EquipType.Body, body, self);
        Frame(EquipType.Legs, legs, self);

        Vanity(EquipType.Head, head, head, body, legs, self);
        Vanity(EquipType.Body, body, head, body, legs, self);
        Vanity(EquipType.Legs, legs, head, body, legs, self);
    }

    private static void UpdateArmorSets(On.Terraria.Player.orig_UpdateArmorSets orig, Player self, int i)
    {
        orig(self, i);

        if (EquipTable.Count == 0) return;

        var armor = self.armor;
        if (armor is null || armor.Length < 3) return;

        var head = armor[0];
        var body = armor[1];
        var legs = armor[2];
        if (head is null || body is null || legs is null) return;

        SetBonus(head, head, body, legs, self);
        SetBonus(body, head, body, legs, self);
        SetBonus(legs, head, body, legs, self);
    }

    private static void SetBonus(Item piece, Item head, Item body, Item legs, Player player)
    {
        if (piece.IsAir) return;
        if (ModContent.Find(ContentKind.Item, piece.type) is not ModItem mod) return;

        try
        {
            if (!mod.IsArmorSet(head, body, legs)) return;
            mod.UpdateArmorSet(player);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] {mod.Name} 的套装回调异常：{Safe.Describe(e)}");
        }
    }

    private static void Frame(EquipType part, int slot, Player player)
    {
        if (EquipTable.OfSlot(part, slot) is not { } equip) return;

        try
        {
            equip.FrameEffects(player, (int)part);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] {equip.Name}.FrameEffects 异常：{Safe.Describe(e)}");
        }
    }

    private static void Vanity(EquipType part, int slot, int head, int body, int legs, Player player)
    {
        if (EquipTable.OfSlot(part, slot) is not { } equip) return;

        try
        {
            if (!equip.IsVanitySet(head, body, legs)) return;
            equip.PreUpdateVanitySet(player);
            equip.UpdateVanitySet(player);
            equip.ArmorSetShadows(player);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] {equip.Name} 的时装套回调异常：{Safe.Describe(e)}");
        }
    }

    public void Refresh()
    {
        EquipTable.Collect();
        EquipTable.Assign();
    }

    internal void Build()
    {
        EquipTable.Collect();
        Grow();

        EquipTable.Assign();
        Write();
        WriteSlotOwners();
        WriteSwitches();

        if (EquipTable.Count > 0) Log.Info($"[equip] 安装：{EquipTable.Count} 张装备贴图");
    }

    private static void Grow()
    {
        for (var part = 0; part < EquipTable.Parts.Length; part++)
        {
            var type = EquipTable.Parts[part];
            var count = EquipTable.Of(type).Count;
            if (count <= 0) continue;

            var total = EquipTable.Vanilla(type) + count;

            TableMaintenance.Ensure(EquipTable.Set(type), total).GrowAll(Log.Info);

            var companions = EquipTable.CompanionSets(type);
            for (var index = 0; index < companions.Length; index++)
                TableMaintenance.Ensure(companions[index], total).GrowAll(Log.Info);

            EquipTables.WriteCount(type, total);

            switch (type)
            {
                case EquipType.Head:
                    var heads = Item.headType;
                    if (ArrayGrow.Grow(ref heads, total)) Item.headType = heads;
                    break;
                case EquipType.Body:
                    var bodies = Item.bodyType;
                    if (ArrayGrow.Grow(ref bodies, total)) Item.bodyType = bodies;
                    break;
                case EquipType.Legs:
                    var legs = Item.legType;
                    if (ArrayGrow.Grow(ref legs, total)) Item.legType = legs;
                    break;
            }
        }
    }

    private static string PartName(EquipType type) => type switch
    {
        EquipType.HandOn => "HandOn",
        EquipType.HandOff => "HandOff",
        EquipType.Shoes => "Shoe",
        EquipType.Wing => "Wing",
        _ => type.ToString(),
    };

    private static void WriteSlotOwners()
    {
        for (var part = 0; part < EquipTable.Parts.Length; part++)
        {
            var type = EquipTable.Parts[part];
            var list = EquipTable.Of(type);

            for (var index = 0; index < list.Count; index++)
            {
                var equip = list[index];
                var owner = equip.Item;
                if (owner is null || owner.Type <= 0) continue;

                try
                {
                    switch (type)
                    {
                        case EquipType.Head when equip.Slot < Terraria.Item.headType.Length:
                            Terraria.Item.headType[equip.Slot] = owner.Type;
                            break;
                        case EquipType.Body when equip.Slot < Terraria.Item.bodyType.Length:
                            Terraria.Item.bodyType[equip.Slot] = owner.Type;
                            break;
                        case EquipType.Legs when equip.Slot < Terraria.Item.legType.Length:
                            Terraria.Item.legType[equip.Slot] = owner.Type;
                            break;
                    }
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {equip.Name} 写槽位主人失败：{Safe.Describe(e)}");
                }
            }
        }
    }

    private static void WriteSwitches()
    {
        NewFraming(EquipType.Body);
        NewFraming(EquipType.HandOn);
        NewFraming(EquipType.HandOff);

        RefillWingStats();
    }

    private static void RefillWingStats()
    {
        var wings = EquipTable.Of(EquipType.Wing);
        if (wings.Count == 0) return;

        var first = wings[0].Slot;
        var filled = EquipTables.FillWingStats(first, wings[wings.Count - 1].Slot + 1);
        if (filled > 0) Log.Info($"[equip] 翅膀飞行数值（飞行时间 100）：{filled} 槽");
        else if (!_wingStatsWarned)
        {
            _wingStatsWarned = true;
            Log.Warn("[equip] 翅膀飞行数值没写进去：ArmorIDs.Wing.Sets.Stats 找不到");
        }
    }

    private static bool _wingStatsWarned;

    private static void EnsureWingStats(Player player)
    {
        var slot = player.wings;
        if (slot <= 0) return;
        if (EquipTable.OfSlot(EquipType.Wing, slot) is null) return;

        if (EquipTables.WingFlyTime(slot) <= 0) RefillWingStats();

        if (player.wingTimeMax > 0f) return;

        var flyTime = EquipTables.WingFlyTime(slot);
        if (flyTime > 0) player.wingTimeMax = flyTime;
    }

    private static void NewFraming(EquipType type)
    {
        var list = EquipTable.Of(type);
        var written = 0;

        for (var index = 0; index < list.Count; index++)
        {
            if (EquipTables.SetFlag(type, list[index].Slot, true)) written++;
        }

        if (written > 0) Log.Info($"[equip] {PartName(type)} 新帧码：{written} 槽");
    }

    private static void Write()
    {
        var written = 0;

        for (var part = 0; part < EquipTable.Parts.Length; part++)
        {
            var type = EquipTable.Parts[part];
            var list = EquipTable.Of(type);
            var table = EquipTable.Table(type);
            var companions = EquipTable.Companions(type);

            for (var index = 0; index < list.Count; index++)
            {
                var equip = list[index];

                if (string.IsNullOrEmpty(equip.Texture))
                {
                    Log.Warn($"[equip] {equip.Name} 没有贴图路径");
                    continue;
                }

                Asset<Texture2D>? asset;
                try
                {
                    asset = equip.Item?.Mod?.LoadAsset(equip.Texture);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {equip.Name} 的贴图加载失败（{equip.Texture}）：{Safe.Describe(e)}");
                    continue;
                }

                if (asset is null)
                {
                    Log.Warn($"[equip] {equip.Name} 的贴图是空资源（{equip.Texture}）");
                    continue;
                }

                if (TextureSlots.TryWrite(table, equip.Slot, asset)) written++;
                else Log.Warn($"[equip] {equip.Name} 的贴图没写进 {table}[{equip.Slot}]（表长 {TextureSlots.Length(table)}）");

                for (var extra = 0; extra < companions.Length; extra++)
                {
                    if (TextureSlots.TryWrite(companions[extra], equip.Slot, asset)) written++;
                    else Log.Warn($"[equip] {equip.Name} 的贴图没写进 {companions[extra]}[{equip.Slot}]");
                }
            }
        }

        if (EquipTable.Count > 0) Log.Info($"[equip] 贴图写槽：{written}");
    }
}

/// <summary>装备贴图的 19 类原生表（15 个部位 + 复合层 / 手臂层）与装备槽位相关的原生读写。</summary>
internal static class EquipTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.GameContent.TextureAssets),
        typeof(Terraria.ID.ArmorIDs.Head.Sets),
        typeof(Terraria.ID.ArmorIDs.Body.Sets),
        typeof(Terraria.ID.ArmorIDs.Legs.Sets),
        typeof(Terraria.ID.ArmorIDs.HandOn.Sets),
        typeof(Terraria.ID.ArmorIDs.HandOff.Sets),
        typeof(Terraria.ID.ArmorIDs.Back.Sets),
        typeof(Terraria.ID.ArmorIDs.Front.Sets),
        typeof(Terraria.ID.ArmorIDs.Shoe.Sets),
        typeof(Terraria.ID.ArmorIDs.Waist.Sets),
        typeof(Terraria.ID.ArmorIDs.Wing.Sets),
        typeof(Terraria.ID.ArmorIDs.Neck.Sets),
        typeof(Terraria.ID.ArmorIDs.Face.Sets),
        typeof(Terraria.ID.ArmorIDs.Balloon.Sets),
        typeof(Terraria.ID.ArmorIDs.Beard.Sets),
    ];

    internal static readonly TableSet Head = new("头盔贴图", () => Vanilla(0, TextureTable.ArmorHead), Types);
    internal static readonly TableSet Body = new("胸甲贴图", () => Vanilla(1, TextureTable.ArmorBody), Types);
    internal static readonly TableSet Legs = new("护腿贴图", () => Vanilla(2, TextureTable.ArmorLeg), Types);
    internal static readonly TableSet HandOn = new("手部（前面）贴图", () => Vanilla(3, TextureTable.AccHandsOn), Types);
    internal static readonly TableSet HandOff = new("手部（后面）贴图", () => Vanilla(4, TextureTable.AccHandsOff), Types);
    internal static readonly TableSet Back = new("背部贴图", () => Vanilla(5, TextureTable.AccBack), Types);
    internal static readonly TableSet Front = new("胸前贴图", () => Vanilla(6, TextureTable.AccFront), Types);
    internal static readonly TableSet Shoes = new("鞋贴图", () => Vanilla(7, TextureTable.AccShoes), Types);
    internal static readonly TableSet Waist = new("腰贴图", () => Vanilla(8, TextureTable.AccWaist), Types);
    internal static readonly TableSet Wing = new("翅膀贴图", () => Vanilla(9, TextureTable.Wings), Types);
    internal static readonly TableSet Shield = new("盾贴图", () => Vanilla(10, TextureTable.AccShield), Types);
    internal static readonly TableSet Neck = new("项链贴图", () => Vanilla(11, TextureTable.AccNeck), Types);
    internal static readonly TableSet Face = new("面部贴图", () => Vanilla(12, TextureTable.AccFace), Types);
    internal static readonly TableSet Balloon = new("气球贴图", () => Vanilla(13, TextureTable.AccBalloon), Types);
    internal static readonly TableSet Beard = new("胡子贴图", () => Vanilla(14, TextureTable.AccBeard), Types);
    internal static readonly TableSet BodyComposite = new("胸甲复合贴图", () => Vanilla(15, TextureTable.ArmorBodyComposite), Types);
    internal static readonly TableSet Arm = new("手臂贴图", () => Vanilla(16, TextureTable.ArmorArm), Types);
    internal static readonly TableSet HandOnComposite = new("手部前复合贴图", () => Vanilla(17, TextureTable.AccHandsOnComposite), Types);
    internal static readonly TableSet HandOffComposite = new("手部后复合贴图", () => Vanilla(18, TextureTable.AccHandsOffComposite), Types);

    private static readonly int[] VanillaCache = new int[24];

    internal static int Vanilla(int part, TextureTable table)
    {
        if ((uint)part < (uint)VanillaCache.Length && VanillaCache[part] > 0) return VanillaCache[part];

        var length = TextureSlots.Length(table);
        if (length > 0 && (uint)part < (uint)VanillaCache.Length) VanillaCache[part] = length;
        return length > 0 ? length : 0;
    }

    internal static void WriteCount(EquipType part, int total)
    {
        switch (part)
        {
            case EquipType.Head: ArmorIDs.Head.Count = total; break;
            case EquipType.Body: ArmorIDs.Body.Count = total; break;
            case EquipType.Legs: ArmorIDs.Legs.Count = total; break;
            case EquipType.HandOn: ArmorIDs.HandOn.Count = total; break;
            case EquipType.HandOff: ArmorIDs.HandOff.Count = total; break;
            case EquipType.Back: ArmorIDs.Back.Count = total; break;
            case EquipType.Front: ArmorIDs.Front.Count = total; break;
            case EquipType.Shoes: ArmorIDs.Shoe.Count = total; break;
            case EquipType.Waist: ArmorIDs.Waist.Count = total; break;
            case EquipType.Wing: ArmorIDs.Wing.Count = total; break;
            case EquipType.Shield: ArmorIDs.Shield.Count = total; break;
            case EquipType.Neck: ArmorIDs.Neck.Count = total; break;
            case EquipType.Balloon: ArmorIDs.Balloon.Count = total; break;
            case EquipType.Face: ArmorIDs.Face.Count = (sbyte)total; break;
            case EquipType.Beard: ArmorIDs.Beard.Count = (sbyte)total; break;
        }
    }

    internal static bool SetFlag(EquipType part, int slot, bool value)
    {
        var flags = part switch
        {
            EquipType.Body => ArmorIDs.Body.Sets.UsesNewFramingCode,
            EquipType.HandOn => ArmorIDs.HandOn.Sets.UsesNewFramingCode,
            EquipType.HandOff => ArmorIDs.HandOff.Sets.UsesNewFramingCode,
            _ => null,
        };

        if (flags is null || (uint)slot >= (uint)flags.Length) return false;
        flags[slot] = value;
        return true;
    }

    internal static int FillWingStats(int from, int to)
    {
        var stats = ArmorIDs.Wing.Sets.Stats;
        if (stats is null || from < 0 || to <= from || to > stats.Length) return 0;

        for (var slot = from; slot < to; slot++)
        {
            var value = stats[slot];
            value.FlyTime = 100;
            value.AccRunSpeedOverride = -1f;
            value.AccRunAccelerationMult = 1f;
            value.HasDownHoverStats = false;
            value.DownHoverSpeedOverride = -1f;
            value.DownHoverAccelerationMult = 1f;
            stats[slot] = value;
        }
        return to - from;
    }

    public static int WingFlyTime(int slot)
    {
        var stats = ArmorIDs.Wing.Sets.Stats;
        if (stats is null || (uint)slot >= (uint)stats.Length) return -1;
        return stats[slot].FlyTime;
    }
}
