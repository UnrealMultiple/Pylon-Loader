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
        On.Terraria.Player.UpdateArmorSets += UpdateArmorSets;
        On.Terraria.Player.PlayerFrame += PlayerFrame;
        On.Terraria.Player.SetArmorEffectVisuals += SetArmorEffectVisuals;
        On.Terraria.Player.SetMatch += SetMatch;
        On.Terraria.Player.WingFrame += WingFrame;
        On.Terraria.Player.WingMovement += WingMovement;
        On.Terraria.Player.WingAirLogicTweaks += WingAirLogicTweaks;

        Log.Info("[equip] 已挂（15 个部位：编号 / 扩表 / 写贴图 / 套装 / 时装套 / SetMatch / 翅膀动画）");
        return true;
    }

    private static bool _globalVertical;
    private static bool _globalHorizontal;
    private static bool _globalUpdate;

    private static int SetMatch(On.Terraria.Player.orig_SetMatch orig, Terraria.Player.SetMatchRequest request, ref bool somethingSpecial)
    {
        var result = orig(request, ref somethingSpecial);

        try
        {
            var part = request.ArmorSlotRequested;
            var type = part == 1 ? EquipType.Body : part == 2 ? EquipType.Legs : EquipType.Head;
            var slot = part == 1 ? request.Body : part == 2 ? request.Legs : request.Head;
            var equip = EquipTable.OfSlot(type, slot);

            var equipSlot = result;
            var robes = somethingSpecial;
            if (equip is not null)
            {
                try
                {
                    equip.SetMatch(request.Male, ref equipSlot, ref robes);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {equip.Name}.SetMatch 异常：{Safe.Describe(e)}");
                }
            }

            foreach (var global in GlobalItemRegistry.All)
            {
                if (!global.Plan.Has(HookId.SetMatch)) continue;

                try
                {
                    global.SetMatch(part, slot, request.Male, ref equipSlot, ref robes);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {global.FullKey}.SetMatch 异常：{Safe.Describe(e)}");
                }
            }

            somethingSpecial = robes;
            return equipSlot;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] SetMatch 异常：{Safe.Describe(e)}");
            return result;
        }
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
        var equip = EquipTable.OfSlot(EquipType.Wings, logic);
        if (equip is null && !_globalVertical)
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

            equip?.VerticalWingSpeeds(self, ref falling, ref rising, ref maxCan, ref maxAscent, ref constant);

            var item = equip is null ? EquippedWings(self, logic) : null;
            foreach (var global in GlobalItemRegistry.All)
            {
                if (!global.Plan.Has(HookId.VerticalWingSpeeds)) continue;

                try
                {
                    global.VerticalWingSpeeds(item, self, ref falling, ref rising, ref maxCan, ref maxAscent, ref constant);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {global.FullKey}.VerticalWingSpeeds 异常：{Safe.Describe(e)}");
                }
            }

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
            ContentErrors.Report("[equip] VerticalWingSpeeds 异常：" + Safe.Describe(e));
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

        var equip = EquipTable.OfSlot(EquipType.Wings, logic);
        if (equip is null && !_globalHorizontal) return;

        try
        {
            var speed = self.accRunSpeed;
            var acceleration = self.runAcceleration;
            equip?.HorizontalWingSpeeds(self, ref speed, ref acceleration);

            var item = equip is null ? EquippedWings(self, logic) : null;
            foreach (var global in GlobalItemRegistry.All)
            {
                if (!global.Plan.Has(HookId.HorizontalWingSpeeds)) continue;

                try
                {
                    global.HorizontalWingSpeeds(item, self, ref speed, ref acceleration);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {global.FullKey}.HorizontalWingSpeeds 异常：{Safe.Describe(e)}");
                }
            }

            self.accRunSpeed = speed;
            self.runAcceleration = acceleration;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[equip] HorizontalWingSpeeds 异常：" + Safe.Describe(e));
        }
    }

    private static void WingFrame(On.Terraria.Player.orig_WingFrame orig, Player self, bool wingFlap)
    {
        var wings = self.wings;
        var equip = wings > 0 ? EquipTable.OfSlot(EquipType.Wings, wings) : null;
        if (equip is not null || (_globalUpdate && wings > 0))
        {
            var custom = false;
            try
            {
                if (equip is not null) custom = equip.WingUpdate(self, wingFlap);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {equip?.Name}.WingUpdate 异常：{Safe.Describe(e)}");
            }

            foreach (var global in GlobalItemRegistry.All)
            {
                if (!global.Plan.Has(HookId.WingUpdate)) continue;

                try
                {
                    custom |= global.WingUpdate(wings, self, wingFlap);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {global.FullKey}.WingUpdate 异常：{Safe.Describe(e)}");
                }
            }

            if (custom) return;
        }

        orig(self, wingFlap);
    }

    private static void UpdateArmorSets(On.Terraria.Player.orig_UpdateArmorSets orig, Player self, int i)
    {
        if (_setBonusWanted) SetBonusText(self, null);
        orig(self, i);

        try
        {
            var armor = self.armor;
            Item? head = null, body = null, legs = null;
            if (armor is not null && armor.Length >= 3)
            {
                head = armor[0];
                body = armor[1];
                legs = armor[2];
            }

            if (head is not null && body is not null && legs is not null)
            {
                foreach (var piece in new[] { head, body, legs })
                {
                    if (piece.IsAir || ModContent.Find(ContentKind.Item, piece.type) is not ModItem mod) continue;

                    try
                    {
                        if (!mod.IsArmorSet(head, body, legs)) continue;
                        mod.UpdateArmorSet(self);
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Report($"[equip] {mod.Name} 的套装回调异常：{Safe.Describe(e)}");
                    }
                }

                foreach (var global in GlobalItemRegistry.All)
                {
                    if (!global.Plan.Has(HookId.IsArmorSet)) continue;

                    string set = "";
                    try
                    {
                        set = global.IsArmorSet(head, body, legs) ?? "";
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Report($"[equip] {global.FullKey}.IsArmorSet 异常：{Safe.Describe(e)}");
                    }
                    if (set.Length == 0) continue;

                    try
                    {
                        global.UpdateArmorSet(self, set);
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Report($"[equip] {global.FullKey}.UpdateArmorSet 异常：{Safe.Describe(e)}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] 套装回调异常：{Safe.Describe(e)}");
        }

        PlayerPipeline.Instance.RunPostUpdateEquips(self);
    }

    private static void PlayerFrame(On.Terraria.Player.orig_PlayerFrame orig, Player self)
    {
        orig(self);

        try
        {
            Vanity(self, nameof(EquipTexture.PreUpdateVanitySet));
            PlayerPipeline.Instance.RunFrameEffects(self);
            foreach (var type in EquipTable.Installed())
            {
                if (EquipTable.OfSlot(type, EquipTable.PlayerSlot(self, type)) is not { } equip) continue;
                try
                {
                    equip.FrameEffects(self, type);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {equip.Name}.FrameEffects 异常：{Safe.Describe(e)}");
                }
            }
            Vanity(self, nameof(EquipTexture.UpdateVanitySet));
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] 时装套回调异常：{Safe.Describe(e)}");
        }

        EnsureWingStats(self);
    }

    private static void SetArmorEffectVisuals(On.Terraria.Player.orig_SetArmorEffectVisuals orig, Player self, Player drawPlayer)
    {
        orig(self, drawPlayer);

        try
        {
            Vanity(drawPlayer, nameof(EquipTexture.ArmorSetShadows));

            if (!drawPlayer.stoned && drawPlayer.stealth == 1f) return;

            self.armorEffectDrawOutlines = false;
            self.armorEffectDrawShadow = false;
            self.armorEffectDrawShadowSubtle = false;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[equip] 盔甲影子回调异常：{Safe.Describe(e)}");
        }
    }

    private static void Vanity(Player player, string method)
    {
        var head = player.head;
        var body = player.body;
        var legs = player.legs;

        foreach (var (type, slot) in new[] { (EquipType.Head, head), (EquipType.Body, body), (EquipType.Legs, legs) })
        {
            if (EquipTable.OfSlot(type, slot) is not { } equip) continue;

            bool set = false;
            try
            {
                set = equip.IsVanitySet(head, body, legs);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {equip.Name}.IsVanitySet 异常：{Safe.Describe(e)}");
            }
            if (!set) continue;

            try
            {
                switch (method)
                {
                    case nameof(EquipTexture.PreUpdateVanitySet):
                        equip.PreUpdateVanitySet(player);
                        break;
                    case nameof(EquipTexture.UpdateVanitySet):
                        equip.UpdateVanitySet(player);
                        break;
                    case nameof(EquipTexture.ArmorSetShadows):
                        equip.ArmorSetShadows(player);
                        break;
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {equip.Name}.{method} 异常：{Safe.Describe(e)}");
            }
        }

        foreach (var global in GlobalItemRegistry.All)
        {
            if (!global.Plan.Has(HookId.IsVanitySet)) continue;

            string set = "";
            try
            {
                set = global.IsVanitySet(head, body, legs) ?? "";
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {global.FullKey}.IsVanitySet 异常：{Safe.Describe(e)}");
            }
            if (set.Length == 0) continue;

            try
            {
                switch (method)
                {
                    case nameof(EquipTexture.PreUpdateVanitySet):
                        global.PreUpdateVanitySet(player, set);
                        break;
                    case nameof(EquipTexture.UpdateVanitySet):
                        global.UpdateVanitySet(player, set);
                        break;
                    case nameof(EquipTexture.ArmorSetShadows):
                        global.ArmorSetShadows(player, set);
                        break;
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[equip] {global.FullKey}.{method} 异常：{Safe.Describe(e)}");
            }
        }
    }

    internal static string? SetBonusText(Player player) =>
        _setBonuses.TryGetValue(player.Pointer, out var text) ? text : null;

    internal static void SetBonusText(Player player, string? text)
    {
        if (!_setBonusWanted)
        {
            _setBonusWanted = true;
        }
        if (string.IsNullOrEmpty(text)) _setBonuses.Remove(player.Pointer);
        else _setBonuses[player.Pointer] = text;
    }

    private static bool _setBonusWanted;

    private static readonly Dictionary<long, string> _setBonuses = [];

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

        _globalVertical = GlobalItemRegistry.AnyWith(HookId.VerticalWingSpeeds);
        _globalHorizontal = GlobalItemRegistry.AnyWith(HookId.HorizontalWingSpeeds);
        _globalUpdate = GlobalItemRegistry.AnyWith(HookId.WingUpdate);

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
        EquipType.HandsOn => "HandOn",
        EquipType.HandsOff => "HandOff",
        EquipType.Shoes => "Shoe",
        EquipType.Wings => "Wing",
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
        NewFraming(EquipType.HandsOn);
        NewFraming(EquipType.HandsOff);

        RefillWingStats();
    }

    private static void RefillWingStats()
    {
        var wings = EquipTable.Of(EquipType.Wings);
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
        if (EquipTable.OfSlot(EquipType.Wings, slot) is null) return;

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
            case EquipType.HandsOn: ArmorIDs.HandOn.Count = total; break;
            case EquipType.HandsOff: ArmorIDs.HandOff.Count = total; break;
            case EquipType.Back: ArmorIDs.Back.Count = total; break;
            case EquipType.Front: ArmorIDs.Front.Count = total; break;
            case EquipType.Shoes: ArmorIDs.Shoe.Count = total; break;
            case EquipType.Waist: ArmorIDs.Waist.Count = total; break;
            case EquipType.Wings: ArmorIDs.Wing.Count = total; break;
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
            EquipType.HandsOn => ArmorIDs.HandOn.Sets.UsesNewFramingCode,
            EquipType.HandsOff => ArmorIDs.HandOff.Sets.UsesNewFramingCode,
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
