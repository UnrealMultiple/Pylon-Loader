using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.Graphics;

namespace Pylon.ModLoader;

/// <summary>玩家管线：按玩家分派作者的 ModPlayer 钩子；每个钩子写成显式循环，避免每帧闭包分配。</summary>
internal sealed class PlayerPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static PlayerPipeline Instance { get; } = new();

    private readonly PlayerBinding _binding = new();

    internal PlayerBinding Binding => _binding;
    private readonly HashSet<int> _reported = [];

    public string Name => "player";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.ResetEffects += ResetEffects;
        On.Terraria.Player.UpdateEquips += Equips;
        On.Terraria.Player.UpdateLifeRegen += LifeRegen;
        On.Terraria.Player.UpdateDead += Dead;
        On.Terraria.Player.Update += Update;
        On.Terraria.Player.UpdateBuffs += Buffs;
        On.Terraria.Player.KillMe += KillMe;
        PlayerHurtHooks.Install();
        PlayerUpdateHooks.Install();

        On.Terraria.Player.ItemCheck += ItemCheck;
        On.Terraria.Player.CanUseItem += CanUse;
        On.Terraria.Player.ApplyPotionDelay += PotionDelay;
        On.Terraria.Player.AddBuff += AddBuff;

        On.Terraria.Player.RefreshInfoAccs += InfoAccessories;
        On.Terraria.GameInput.TriggersSet.CopyInto += TriggersCopied;
        On.Terraria.Player.KeyDoubleTap += KeyDoubleTap;
        On.Terraria.Player.KeyHoldDown += KeyHoldDown;
        On.Terraria.Player.TrySwitchingLoadout += LoadoutSwitched;

        On.Terraria.Player.Spawn += Spawned;
        On.Terraria.Player.Teleport += Teleport;
        On.Terraria.Player.PickupItem += Pickup;
        On.Terraria.Player.OnHit += HitAnything;
        On.GUIPlayerCreateMenu.SetupStartingItems += StartingItemsCreated;
        On.Terraria.Player.DropItems += StartingItemsDropped;

        Refresh();
        Log.Info($"[player] 已挂（重置 / 更新 / 装备 / 再生 / 死亡 / 物品检查 / 能力键 / 复活 / 传送 / 拾取），模板 {_binding.Count}");
        return true;
    }

    internal void Register(ModPlayer template) => _binding.Register(template);

    internal ModPlayer[] ModsOf(Player player) => _binding.Of(player);

    internal bool ModsActive(HookId hook) => _binding.Active(hook);

    internal void HideDrawLayers(Terraria.DataStructures.PlayerDrawSet drawInfo)
    {
        if (!_binding.Active(HookId.HideDrawLayers)) return;

        var player = drawInfo.drawPlayer;
        if (player is null) return;

        foreach (var mod in _binding.Of(player))
        {
            if (!mod.HookEnabled(HookId.HideDrawLayers)) continue;

            try
            {
                mod.HideDrawLayers(drawInfo);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.HideDrawLayers, e);
            }
        }
    }

    internal void ModifyDrawLayerOrdering(IDictionary<PlayerDrawLayer, PlayerDrawLayer.Position> positions)
    {
        if (!_binding.Active(HookId.ModifyDrawLayerOrdering)) return;

        foreach (var mod in _binding.Templates)
        {
            if (!mod.HookEnabled(HookId.ModifyDrawLayerOrdering)) continue;

            try
            {
                mod.ModifyDrawLayerOrdering(positions);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawinfo] {mod.Mod?.Name}.ModifyDrawLayerOrdering 异常：{Safe.Describe(e)}");
            }
        }
    }

    public void Refresh() => _binding.Refresh();

    #region 每帧

    private void ResetEffects(On.Terraria.Player.orig_ResetEffects orig, Player self)
    {
        orig(self);

        if (_binding.Active(HookId.ResetEffects))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.ResetEffects)) continue;

                try
                {
                    mod.ResetEffects();
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ResetEffects, e);
                }
            }
        }

        if (!_binding.Active(HookId.ModifyMaxStats)) return;

        var health = StatModifier.Default;
        var mana = StatModifier.Default;
        var any = false;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ModifyMaxStats)) continue;

            try
            {
                mod.ModifyMaxStats(out var h, out var m);
                health = health.CombineWith(h);
                mana = mana.CombineWith(m);
                any = true;
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ModifyMaxStats, e);
            }
        }

        if (!any) return;

        try
        {
            var life = self.statLifeMax;
            var magic = self.statManaMax;
            self.statLifeMax2 = Math.Max(1, self.statLifeMax2 + (int)health.ApplyTo(life) - life);
            self.statManaMax2 = Math.Max(0, self.statManaMax2 + (int)mana.ApplyTo(magic) - magic);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[player] ModifyMaxStats 应用异常：" + Safe.Describe(e));
        }
    }

    private void Update(On.Terraria.Player.orig_Update orig, Player self, int i)
    {
        if (_binding.Active(HookId.PreUpdate))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.PreUpdate)) continue;

                try
                {
                    mod.PreUpdate();
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.PreUpdate, e);
                }
            }
        }

        orig(self, i);


        if (!_binding.Active(HookId.PostUpdate)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.PostUpdate)) continue;

            try
            {
                mod.PostUpdate();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PostUpdate, e);
            }
        }
    }


    private void Buffs(On.Terraria.Player.orig_UpdateBuffs orig, Player self, int i)
    {
        if (_binding.Active(HookId.PreUpdateBuffs))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.PreUpdateBuffs)) continue;

                try
                {
                    mod.PreUpdateBuffs();
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.PreUpdateBuffs, e);
                }
            }
        }

        orig(self, i);

        if (!_binding.Active(HookId.PostUpdateBuffs)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.PostUpdateBuffs)) continue;

            try
            {
                mod.PostUpdateBuffs();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PostUpdateBuffs, e);
            }
        }
    }

    private void Equips(On.Terraria.Player.orig_UpdateEquips orig, Player self, int i)
    {
        orig(self, i);

        var mods = _binding.Active(HookId.UpdateEquips) || _binding.Active(HookId.PostUpdateEquips)
            ? _binding.Of(self)
            : null;

        if (mods is not null)
        {
            foreach (var mod in mods)
            {
                if (!mod.HookEnabled(HookId.UpdateEquips)) continue;

                try
                {
                    mod.UpdateEquips();
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.UpdateEquips, e);
                }
            }
        }

        if (_binding.Active(HookId.FrameEffects))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.FrameEffects)) continue;

                try
                {
                    mod.FrameEffects();
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.FrameEffects, e);
                }
            }
        }

        if (!_binding.Active(HookId.PostUpdateEquips)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.PostUpdateEquips)) continue;

            try
            {
                mod.PostUpdateEquips();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PostUpdateEquips, e);
            }
        }
    }

    private void LifeRegen(On.Terraria.Player.orig_UpdateLifeRegen orig, Player self)
    {
        if (_binding.Active(HookId.UpdateBadLifeRegen))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.UpdateBadLifeRegen)) continue;

                try
                {
                    mod.UpdateBadLifeRegen();
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.UpdateBadLifeRegen, e);
                }
            }
        }

        orig(self);

        if (!_binding.Active(HookId.UpdateLifeRegen)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.UpdateLifeRegen)) continue;

            try
            {
                mod.UpdateLifeRegen();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.UpdateLifeRegen, e);
            }
        }
    }

    private void Dead(On.Terraria.Player.orig_UpdateDead orig, Player self)
    {
        if (_binding.Active(HookId.UpdateDead))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.UpdateDead)) continue;

                try
                {
                    mod.UpdateDead();
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.UpdateDead, e);
                }
            }
        }

        orig(self);
    }

    #endregion

    #region 死亡

    private void KillMe(On.Terraria.Player.orig_KillMe orig, Player self, PlayerDeathReason damageSource, double dmg,
                        int hitDirection, bool pvp)
    {
        var dies = !self.dead && !self.creativeGodMode;

        if (dies && _binding.Active(HookId.PreKill))
        {
            var allowed = true;
            bool playSound = true, genDust = true;
            var source = damageSource;

            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.PreKill)) continue;

                try
                {
                    if (!mod.PreKill(dmg, hitDirection, pvp, ref playSound, ref genDust, ref source)) allowed = false;
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.PreKill, e);
                }
            }

            if (!allowed) return;
            damageSource = source;
        }

        orig(self, damageSource, dmg, hitDirection, pvp);

        if (!dies || !self.dead || !_binding.Active(HookId.Kill)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.Kill)) continue;

            try
            {
                mod.Kill(dmg, hitDirection, pvp, damageSource);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.Kill, e);
            }
        }
    }

    private void HitAnything(On.Terraria.Player.orig_OnHit orig, Player self, float x, float y, Terraria.Entity victim)
    {
        orig(self, x, y, victim);

        if (!_binding.Active(HookId.OnHitAnything)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.OnHitAnything)) continue;

            try
            {
                mod.OnHitAnything(x, y, victim);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.OnHitAnything, e);
            }
        }
    }

    private void StartingItemsCreated(On.GUIPlayerCreateMenu.orig_SetupStartingItems orig, GUIPlayerCreateMenu self)
    {
        orig(self);
        var pending = Main.PendingPlayer;
        if (pending is not null) Starting(pending, mediumCoreDeath: false);
    }

    private void StartingItemsDropped(On.Terraria.Player.orig_DropItems orig, Player self, bool gemsOnly)
    {
        orig(self, gemsOnly);
        if (!gemsOnly && self.difficulty == 1) Starting(self, mediumCoreDeath: true);
    }

    private void Starting(Player player, bool mediumCoreDeath)
    {
        try
        {
            var inventory = player.inventory;
            var vanilla = new List<Item>();
            for (var i = 0; i < 50 && i < inventory.Length; i++)
            {
                var item = inventory[i];
                if (item is not null && item.type > 0 && item.stack > 0) vanilla.Add(item);
            }

            var byMod = new Dictionary<string, List<Item>> { ["Terraria"] = vanilla };

            foreach (var mod in _binding.Of(player))
            {
                if (!mod.HookEnabled(HookId.AddStartingItems)) continue;
                var name = mod.Mod?.Name;
                if (name is null) continue;

                try
                {
                    var added = mod.AddStartingItems(mediumCoreDeath);
                    if (added is not null)
                    {
                        if (!byMod.TryGetValue(name, out var list)) byMod[name] = list = [];
                        list.AddRange(added);
                    }
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.AddStartingItems, e);
                }
            }

            foreach (var mod in _binding.Of(player))
            {
                if (!mod.HookEnabled(HookId.ModifyStartingInventory)) continue;

                try
                {
                    mod.ModifyStartingInventory(byMod, mediumCoreDeath);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ModifyStartingInventory, e);
                }
            }

            var items = byMod.Values.SelectMany(l => l)
                .Where(item => item is not null && item.type > 0 && item.stack > 0)
                .ToList();

            for (var i = 0; i < 50 && i < inventory.Length; i++)
                inventory[i] = i < items.Count ? items[i]! : new Item();

            for (var i = 50; i < items.Count; i++)
                player.GetOrDropItem(items[i], default);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[player] 开局背包异常：{Safe.Describe(e)}");
        }
    }

    #endregion

    #region 物品与药水

    private void ItemCheck(On.Terraria.Player.orig_ItemCheck orig, Player self)
    {
        if (_binding.Active(HookId.PreItemCheck))
        {
            var run = true;

            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.PreItemCheck)) continue;

                try
                {
                    if (!mod.PreItemCheck()) run = false;
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.PreItemCheck, e);
                }
            }

            if (!run) return;
        }

        orig(self);

        if (!_binding.Active(HookId.PostItemCheck)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.PostItemCheck)) continue;

            try
            {
                mod.PostItemCheck();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PostItemCheck, e);
            }
        }
    }

    private bool CanUse(On.Terraria.Player.orig_CanUseItem orig, Player self)
    {
        if (!_binding.Active(HookId.CanUseItem)) return orig(self);

        var item = Held(self);
        if (item is null) return orig(self);

        var allowed = true;
        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.CanUseItem)) continue;

            try
            {
                if (!mod.CanUseItem(item)) allowed = false;
            }
            catch (Exception e)
            {
                Fail(mod, HookId.CanUseItem, e);
            }
        }

        return allowed && orig(self);
    }

    /// <summary>药水病建立期间的作用域：AddBuff(PotionSickness) 在这里做作者链，否决时整个不加药水病。</summary>
    private sealed class PotionScope
    {
        public Player Player = null!;
        public Item Item = null!;
        public int Previous;
        public bool Blocked;
    }

    [ThreadStatic] private static PotionScope? _potion;

    private void PotionDelay(On.Terraria.Player.orig_ApplyPotionDelay orig, Player self, Item sItem)
    {
        var outer = _potion;
        var scope = new PotionScope { Player = self, Item = sItem, Previous = self.potionDelay };
        _potion = scope;
        try
        {
            orig(self, sItem);
        }
        finally
        {
            _potion = outer;
            if (scope.Blocked) self.potionDelay = scope.Previous;
        }
    }

    private void AddBuff(On.Terraria.Player.orig_AddBuff orig, Player self, int type, int time, bool quiet)
    {
        var scope = _potion;
        if (scope is null || scope.Player.Pointer != self.Pointer || type != Terraria.ID.BuffID.PotionSickness)
        {
            orig(self, type, time, quiet);
            return;
        }

        var item = scope.Item;
        var modItem = ItemPipeline.Instance.InstanceOf(item);

        var delay = time;

        if (modItem is { } m && m.Plan.Has(HookId.ModifyPotionDelay))
        {
            try { m.ModifyPotionDelay(self, ref delay); }
            catch (Exception e) { ContentErrors.Report($"[player] {m.FullKey}.ModifyPotionDelay 异常：{Safe.Describe(e)}"); }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyPotionDelay))
        {
            try { global.ModifyPotionDelay(item, self, ref delay); }
            catch (Exception e) { ContentErrors.Report($"[player] {global.FullKey}.ModifyPotionDelay 异常：{Safe.Describe(e)}"); }
        }

        delay = Math.Max(0, delay);

        var allowed = true;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ApplyPotionDelay)) continue;

            try
            {
                if (!mod.ApplyPotionDelay(item, delay)) allowed = false;
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ApplyPotionDelay, e);
            }
        }

        if (modItem is { } owner && owner.Plan.Has(HookId.ApplyPotionDelay))
        {
            try
            {
                if (!owner.ApplyPotionDelay(self, delay)) allowed = false;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[player] {owner.FullKey}.ApplyPotionDelay 异常：{Safe.Describe(e)}");
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.ApplyPotionDelay))
        {
            try
            {
                if (!global.ApplyPotionDelay(item, self, delay)) allowed = false;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[player] {global.FullKey}.ApplyPotionDelay 异常：{Safe.Describe(e)}");
            }
        }

        if (delay <= 0 || !allowed)
        {
            scope.Blocked = true;
            return;
        }

        self.potionDelay = delay;
        orig(self, type, delay, quiet);
    }

    private void Pickup(On.Terraria.Player.orig_PickupItem orig, Player self, WorldItem itemToPickUp)
    {
        if (_binding.Active(HookId.OnPickup) && itemToPickUp is not null)
        {
            var take = true;
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.OnPickup)) continue;

                try
                {
                    take &= mod.OnPickup(itemToPickUp);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.OnPickup, e);
                }
            }

            if (!take)
            {
                itemToPickUp.TurnToAirAndSync();
                return;
            }
        }

        orig(self, itemToPickUp);
    }

    #endregion

    #region 能力键与预设

    private void InfoAccessories(On.Terraria.Player.orig_RefreshInfoAccs orig, Player self)
    {
        orig(self);

        if (!_binding.Active(HookId.ResetInfoAccessories)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ResetInfoAccessories)) continue;

            try
            {
                mod.ResetInfoAccessories();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ResetInfoAccessories, e);
            }
        }
    }

    private void KeyDoubleTap(On.Terraria.Player.orig_KeyDoubleTap orig, Player self, int keyDir)
    {
        orig(self, keyDir);
        if (keyDir != SetBonusKey) return;

        if (!_binding.Active(HookId.ArmorSetBonusActivated)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ArmorSetBonusActivated)) continue;

            try
            {
                mod.ArmorSetBonusActivated();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ArmorSetBonusActivated, e);
            }
        }
    }

    private void KeyHoldDown(On.Terraria.Player.orig_KeyHoldDown orig, Player self, int keyDir, int holdTime)
    {
        orig(self, keyDir, holdTime);
        if (keyDir != SetBonusKey) return;

        if (!_binding.Active(HookId.ArmorSetBonusHeld)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ArmorSetBonusHeld)) continue;

            try
            {
                mod.ArmorSetBonusHeld(holdTime);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ArmorSetBonusHeld, e);
            }
        }
    }

    private void LoadoutSwitched(On.Terraria.Player.orig_TrySwitchingLoadout orig, Player self, int loadoutIndex)
    {
        var old = self.CurrentLoadoutIndex;
        orig(self, loadoutIndex);

        if (!_binding.Active(HookId.OnEquipmentLoadoutSwitched)) return;

        if (self.CurrentLoadoutIndex == old) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.OnEquipmentLoadoutSwitched)) continue;

            try
            {
                mod.OnEquipmentLoadoutSwitched(old, self.CurrentLoadoutIndex);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.OnEquipmentLoadoutSwitched, e);
            }
        }
    }

    #endregion

    #region 复活与传送

    private void Spawned(On.Terraria.Player.orig_Spawn orig, Player self, PlayerSpawnContext context)
    {
        var wasDead = self.dead;
        orig(self, context);

        if ((!wasDead && context != PlayerSpawnContext.ReviveFromDeath) || !_binding.Active(HookId.OnRespawn)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.OnRespawn)) continue;

            try
            {
                mod.OnRespawn();
            }
            catch (Exception e)
            {
                Fail(mod, HookId.OnRespawn, e);
            }
        }
    }

    private void Teleport(On.Terraria.Player.orig_Teleport orig, Player self, Vector2 newPos, int Style, int extraInfo)
    {
        if (_binding.Active(HookId.CanBeTeleportedTo))
        {
            var context = ContextOf(Style);

            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.CanBeTeleportedTo)) continue;

                try
                {
                    if (!mod.CanBeTeleportedTo(newPos, context)) return;
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.CanBeTeleportedTo, e);
                }
            }
        }

        orig(self, newPos, Style, extraInfo);
    }

    private static string ContextOf(int style) => style switch
    {
        1 => "TeleportRod",
        2 => "TeleportationPotion",
        3 => "Wormhole",
        _ => "Teleport",
    };

    #endregion

    private static int SetBonusKey => Main.ReversedUpDownArmorSetBonuses ? 1 : 0;

    private static Item? Held(Player player)
    {
        var inventory = player.inventory;
        var slot = player.selectedItem;
        if (inventory is null || slot < 0 || slot >= inventory.Length) return null;

        return inventory[slot];
    }

    private void TriggersCopied(On.Terraria.GameInput.TriggersSet.orig_CopyInto orig, Terraria.GameInput.TriggersSet self, Player player)
    {
        orig(self, player);

        if (player.whoAmI != Main.myPlayer) return;
        if (!_binding.Active(HookId.ProcessTriggers)) return;

        foreach (var mod in _binding.Of(player))
        {
            if (!mod.HookEnabled(HookId.ProcessTriggers)) continue;

            try
            {
                mod.ProcessTriggers(self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ProcessTriggers, e);
            }
        }
    }

    private void Fail(ModPlayer mod, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(mod.GetType().Name.GetHashCode(), hook))) return;
        ContentErrors.Report($"[player] {mod.GetType().Name}.{hook} 异常：{Safe.Describe(exception)}");
    }
}