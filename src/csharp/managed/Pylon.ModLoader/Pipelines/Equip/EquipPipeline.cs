using System;
using Terraria.ID;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 装备贴图管线：跑作者 <c>ModItem.Load</c> 收表、按部位扩表、发号、把贴图写进槽位（主表 + 平行层）。
/// 绘制不挂钩子——作者把 <c>item.headSlot/bodySlot/legSlot/backSlot/…</c> 设成 <c>EquipTexture.Slot</c>
/// 就走游戏自己的绘制。效果钩子与时装套判定本版不接，见 docs/pipeline/content-equiptexture.md。
/// </summary>
internal sealed class EquipPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static EquipPipeline Instance { get; } = new();

    public string Name => "equip";

    public IReadOnlyList<TableRequest> Tables => EquipRequests;

    /// <summary>15 个部位 + 4 张复合层：目标长度由各部位当前内容数算（没内容就是 0，不扩）。</summary>
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

    // ===================== 效果（全部 15 个部位） =====================

    /// <summary>穿戴栏 / 时装栏里每件 mod 装备都跑作者的 UpdateEquip / UpdateAccessory。</summary>
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

    /// <summary>这件装备（armor 数组里的一项）带不带 mod 槽位；带就分派回调。</summary>
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
                    equip.Item?.UpdateVanity(item, player);
                }
                else
                {
                    equip.Item?.UpdateEquip(item, player);
                    equip.Item?.UpdateAccessory(item, player, vanity, hideVisual: false);
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {equip.Name} 的效果回调异常：{Safe.Describe(e)}");
            }
        }
    }

    // ===================== 饰品互斥 =====================

    /// <summary>两件饰品能不能同时穿：任一件声明了拒绝就拒绝。</summary>
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

    // ===================== SetMatch 与翅膀 =====================

    /// <summary>画盔甲时问"这件画到哪"：mod 盔甲按作者声明改槽/长袍。</summary>
    private static int SetMatch(On.Terraria.Player.orig_SetMatch orig, Terraria.Player.SetMatchRequest request, ref bool somethingSpecial)
    {
        var result = orig(request, ref somethingSpecial);

        // 用请求里带的三个槽位（原版按它们取贴图），不是玩家当前穿的：绘制时装/替换时两者可能不同。
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


    /// <summary>空中穿的 mod 翅膀（找 3..9 号饰品槽里 wingSlot 等于 logic 的那件）。</summary>
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

    /// <summary>竖直速度：作者改五个引用，管线按原版同一套公式重算 velocity.Y。</summary>
    private static void WingMovement(On.Terraria.Player.orig_WingMovement orig, Player self)
    {
        var logic = self.wingsLogic;
        if (EquipTable.OfSlot(EquipType.Wing, logic) is not { } equip)
        {
            orig(self);
            return;
        }

        // 悬停上飞原版自己算（翼 4 那套），作者的五个数管不到，交回原版。
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

            // 作者的翅膀可能借用了原版某个 wingsLogic；先用那个分支的原版数值打底，作者不想要的自己改。
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

    /// <summary>
    /// 原版每个 <c>wingsLogic</c> 分支的速度数值表：作者的翅膀借用了原版逻辑号时，从这里取打底值，
    /// 手感才和原版那对翅膀一致（不给这套表的话所有 mod 翅膀都是同一副"中庸"手感）。
    /// 数值照抄原版对应分支；没列的号保持通用默认。
    /// </summary>
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

    /// <summary>悬停翼（44/45）的额外推挤：按上下键时在竖直速度上叠一层。</summary>
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

    /// <summary>平飞速度：作者改两个引用，写回 accRunSpeed / runAcceleration。</summary>
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
            // 原版平飞时水平速度上限也按 accRunSpeed 走；只改 accRunSpeed 不动 maxRunSpeed，
            // 移动会被 maxRunSpeed（步行速度 3）夹住 —— 表现就是翅膀能飞、平飞按左右没反应。
            self.maxRunSpeed = speed;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] {equip.Name} 的 HorizontalWingSpeeds 异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>翅膀动画：mod 翅膀声明了 WingUpdate（返回 true）就由作者接管帧。</summary>
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

    // ===================== 效果 =====================

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

    /// <summary>穿齐三件时的套装效果：三件里任一件认这套，就跑它的 UpdateArmorSet。</summary>
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
            mod.UpdateArmorSet(piece, player);
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

    /// <summary>安装期一次：收表 → 扩表 → 发号 → 写贴图。要在作者 <c>SetDefaults</c> 之前跑（作者在那里读 Slot）。</summary>
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

            // 扩表器收的是"目标总长"，不是 mod 数量：传 mod 数量的话它认为新长度还不如原版长，
            // 直接判定完成、一张表都不扩 —— 槽位发出去却没人能写，游戏画到就是越界空指针。
            var total = EquipTable.Vanilla(type) + count;

            TableMaintenance.Ensure(EquipTable.Set(type), total).GrowAll(Log.Info);

            var companions = EquipTable.CompanionSets(type);
            for (var index = 0; index < companions.Length; index++)
                TableMaintenance.Ensure(companions[index], total).GrowAll(Log.Info);

            // 原版用 ArmorIDs.<部位>.Count 当槽位上界：槽位发到 Count 就等于越界，绘制时整件跳过。
            // 只扩 TextureAssets / Item.*Type 而不写 Count，盔甲就是"贴图在表里、人身上不画"。
            EquipTables.WriteCount(type, total);

            // 游戏侧还有一张"槽位 → 物品编号"的表（Item.headType / bodyType / legType）：
            // 时装绘制与默认的时装套判定都按槽位反查物品，不补长、不写进去的话 mod 盔甲画不出来。
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

    /// <summary>把"槽位 → 物品编号"写进游戏那张表（原版靠它按时装槽位找物品）。</summary>
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

    /// <summary>
    /// 游戏侧的逐槽开关：复合贴图的新帧码表，以及翅膀的飞行数值表。
    /// 缺了它们盔甲画得出来但帧号错位（走动时一团马赛克），翅膀看得见却一格都飞不动。
    /// </summary>
    private static void WriteSwitches()
    {
        NewFraming(EquipType.Body);
        NewFraming(EquipType.HandOn);
        NewFraming(EquipType.HandOff);

        RefillWingStats();
    }

    /// <summary>
    /// 给翅膀槽位写飞行数值。这张表游戏会整张重建（WingStats 的初始化器），重建后新槽位的
    /// FlyTime 归零、表也缩回原版长度 —— 表现就是翅膀画得出来却一格都飞不动，所以要能反复补。
    /// </summary>
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

    /// <summary>
    /// 每帧确认一次：穿着 mod 翅膀但飞行数值是 0（表被重建过）就重写；
    /// 顺手把飞行时间补上 —— 原版是按 wingsLogic 一条条分支写 <c>wingTimeMax</c> 的，
    /// mod 槽位走不到任何分支，不补就是 0（按跳跃键毫无反应）。
    /// </summary>
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

/// <summary>
/// 装备贴图的 19 类原生表（15 个部位 + 复合层 / 手臂层），以及只和装备槽位有关的原生读写。
/// 谁用装备贴图谁从这里取表；扩容/补扩由框架统一调度。
/// </summary>
internal static class EquipTables
{
    private static readonly TableClass[] Classes =
    [
        new("Terraria.GameContent", "TextureAssets"),
        new("Terraria.ID", "ArmorIDs", "Head", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Body", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Legs", "Sets"),
        new("Terraria.ID", "ArmorIDs", "HandOn", "Sets"),
        new("Terraria.ID", "ArmorIDs", "HandOff", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Back", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Front", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Shoe", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Waist", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Wing", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Shield", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Neck", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Face", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Balloon", "Sets"),
        new("Terraria.ID", "ArmorIDs", "Beard", "Sets"),
    ];

    internal static readonly TableSet Head = new("头盔贴图", () => Vanilla(0, TextureTable.ArmorHead), () => Classes);
    internal static readonly TableSet Body = new("胸甲贴图", () => Vanilla(1, TextureTable.ArmorBody), () => Classes);
    internal static readonly TableSet Legs = new("护腿贴图", () => Vanilla(2, TextureTable.ArmorLeg), () => Classes);
    internal static readonly TableSet HandOn = new("手部（前面）贴图", () => Vanilla(3, TextureTable.AccHandsOn), () => Classes);
    internal static readonly TableSet HandOff = new("手部（后面）贴图", () => Vanilla(4, TextureTable.AccHandsOff), () => Classes);
    internal static readonly TableSet Back = new("背部贴图", () => Vanilla(5, TextureTable.AccBack), () => Classes);
    internal static readonly TableSet Front = new("胸前贴图", () => Vanilla(6, TextureTable.AccFront), () => Classes);
    internal static readonly TableSet Shoes = new("鞋贴图", () => Vanilla(7, TextureTable.AccShoes), () => Classes);
    internal static readonly TableSet Waist = new("腰贴图", () => Vanilla(8, TextureTable.AccWaist), () => Classes);
    internal static readonly TableSet Wing = new("翅膀贴图", () => Vanilla(9, TextureTable.Wings), () => Classes);
    internal static readonly TableSet Shield = new("盾贴图", () => Vanilla(10, TextureTable.AccShield), () => Classes);
    internal static readonly TableSet Neck = new("项链贴图", () => Vanilla(11, TextureTable.AccNeck), () => Classes);
    internal static readonly TableSet Face = new("面部贴图", () => Vanilla(12, TextureTable.AccFace), () => Classes);
    internal static readonly TableSet Balloon = new("气球贴图", () => Vanilla(13, TextureTable.AccBalloon), () => Classes);
    internal static readonly TableSet Beard = new("胡子贴图", () => Vanilla(14, TextureTable.AccBeard), () => Classes);
    internal static readonly TableSet BodyComposite = new("胸甲复合贴图", () => Vanilla(15, TextureTable.ArmorBodyComposite), () => Classes);
    internal static readonly TableSet Arm = new("手臂贴图", () => Vanilla(16, TextureTable.ArmorArm), () => Classes);
    internal static readonly TableSet HandOnComposite = new("手部前复合贴图", () => Vanilla(17, TextureTable.AccHandsOnComposite), () => Classes);
    internal static readonly TableSet HandOffComposite = new("手部后复合贴图", () => Vanilla(18, TextureTable.AccHandsOffComposite), () => Classes);

    private static readonly int[] VanillaCache = new int[24];

    /// <summary>原版装备贴图表长：没有编译期常量，只能在扩表之前按表长捕获一次（按部位缓存）。</summary>
    internal static int Vanilla(int part, TextureTable table)
    {
        if ((uint)part < (uint)VanillaCache.Length && VanillaCache[part] > 0) return VanillaCache[part];

        var length = TextureSlots.Length(table);
        if (length > 0 && (uint)part < (uint)VanillaCache.Length) VanillaCache[part] = length;
        return length > 0 ? length : 0;
    }

    /// <summary>
    /// 写 <c>ArmorIDs.&lt;部位&gt;.Count</c>：原版拿它当装备槽位上界（<c>槽位 &lt; Count</c> 才画），
    /// 只扩表不写它就是"贴图有了、槽位发出去了，原版判定越界整件跳过"。Face / Beard 的字段是 sbyte，其余是 int。
    /// </summary>
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

    /// <summary>
    /// 给 <c>ArmorIDs.&lt;部位&gt;.Sets</c> 里"逐槽开关"的某一位置值。
    /// 表短了返回 false：这些开关是画角色时逐槽读的，越界写会踩到邻居的内存。
    /// </summary>
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

    /// <summary>
    /// 给 <c>ArmorIDs.Wing.Sets.Stats</c> 的 [from, to) 写一份默认翅膀数值（飞行时间 100 等）。
    /// 这张表决定 <c>wingTimeMax</c> 与平飞/悬停下坠速度：新槽位留 0 就是 FlyTime=0 —— 翅膀画得出来、
    /// 一格都飞不动（按跳跃键毫无反应）。返回实际写了几槽（表还没建/太短返回 0）。
    /// </summary>
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

    /// <summary>
    /// 读 <c>ArmorIDs.Wing.Sets.Stats[slot].FlyTime</c>：-1 = 表没有/太短，0 = 这个槽位还没写飞行数值。
    /// 游戏会整张重建这张表（WingStats 初始化器），所以调用方靠它判断"要不要重写一遍"。
    /// </summary>
    public static int WingFlyTime(int slot)
    {
        var stats = ArmorIDs.Wing.Sets.Stats;
        if (stats is null || (uint)slot >= (uint)stats.Length) return -1;
        return stats[slot].FlyTime;
    }
}
