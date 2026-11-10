using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.Graphics;

namespace Pylon.ModLoader;

/// <summary>
/// 玩家管线：按玩家分派作者的 ModPlayer 钩子。
/// 每个钩子写成显式循环（不用委托/闭包）：这些入口多数是每帧每玩家，闭包会变成每帧分配。
/// </summary>
internal sealed class PlayerPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static PlayerPipeline Instance { get; } = new();

    private readonly PlayerBinding _binding = new();

    /// <summary>玩家 → 他的 ModPlayer 实例（绘制管线按层问"这一层画不画"要用）。</summary>
    internal PlayerBinding Binding => _binding;
    private readonly HashSet<int> _reported = [];

    public string Name => "player";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.ResetEffects += ResetEffects;
        On.Terraria.Player.UpdateEquips += Equips;
        On.Terraria.Player.UpdateLifeRegen += LifeRegen;
        On.Terraria.Player.UpdateManaRegen += ManaRegen;
        On.Terraria.Player.UpdateDead += Dead;
        On.Terraria.Player.Update += Update;
        On.Terraria.Player.KillMe += KillMe;
        PlayerHurtHooks.Install();
        PlayerUpdateHooks.Install();

        On.Terraria.Player.ItemCheck += ItemCheck;
        On.Terraria.Player.CanUseItem += CanUse;
        On.Terraria.Player.ApplyPotionDelay += PotionDelay;

        On.Terraria.Player.RefreshInfoAccs += InfoAccessories;
        On.Terraria.GameInput.TriggersSet.CopyInto += TriggersCopied;
        On.Terraria.Player.KeyDoubleTap += KeyDoubleTap;
        On.Terraria.Player.KeyHoldDown += KeyHoldDown;
        On.Terraria.Player.TrySwitchingLoadout += LoadoutSwitched;

        On.Terraria.Player.Spawn += Spawned;
        On.Terraria.Player.Teleport += Teleport;
        On.Terraria.Player.PickupItem += Pickup;

        Refresh();
        Log.Info($"[player] 已挂（重置 / 更新 / 装备 / 再生 / 死亡 / 物品检查 / 能力键 / 复活 / 传送 / 拾取），模板 {_binding.Count}");
        return true;
    }

    /// <summary>注册期：只记模板，位集等内容都注册完再算。</summary>
    internal void Register(ModPlayer template) => _binding.Register(template);

    /// <summary>某个玩家的全部 ModPlayer 实例（按下标与模板一一对应）。</summary>
    internal ModPlayer[] ModsOf(Player player) => _binding.Of(player);

    /// <summary>有人覆写这个钩子吗：没人就整段跳过。</summary>
    internal bool ModsActive(HookId hook) => _binding.Active(hook);

    /// <summary>这一帧要藏哪些层：在层排序完之后、绘制之前问一次。</summary>
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

    /// <summary>整表排序时给作者一次改层位的机会（没有玩家实例，跑模板）。</summary>
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
        if (_binding.Active(HookId.ResetEffects))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.ResetEffects)) continue;

                try
                {
                    mod.ResetEffects(self);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ResetEffects, e);
                }
            }
        }

        orig(self);

        // 上限加成每帧从 0 开始：作者在 ModifyMaxStats 里加，UpdateEquips 之后统一加到上限。
        if (!_binding.Active(HookId.ModifyMaxStats)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ModifyMaxStats)) continue;

            try
            {
                mod.ModifyMaxStats(self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ModifyMaxStats, e);
            }
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
                    mod.PreUpdate(self);
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
                mod.PostUpdate(self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.PostUpdate, e);
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
            var health = 0;
            var mana = 0;

            foreach (var mod in mods)
            {
                // 作者的加成本来就是累加：先汇总再一次性加到上限，避免逐次写 statLifeMax2 被游戏中途覆盖。
                health += mod.CumulativeHealth;
                mana += mod.CumulativeMana;

                if (!mod.HookEnabled(HookId.UpdateEquips)) continue;

                try
                {
                    mod.UpdateEquips(self);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.UpdateEquips, e);
                }
            }

            if (health != 0) self.statLifeMax2 += health;
            if (mana != 0) self.statManaMax2 += mana;
        }

        if (_binding.Active(HookId.FrameEffects))
        {
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.FrameEffects)) continue;

                try
                {
                    mod.FrameEffects(self);
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
                mod.PostUpdateEquips(self);
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
                    mod.UpdateBadLifeRegen(self);
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
                mod.UpdateLifeRegen(self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.UpdateLifeRegen, e);
            }
        }
    }

    private void ManaRegen(On.Terraria.Player.orig_UpdateManaRegen orig, Player self)
    {
        orig(self);

        if (!_binding.Active(HookId.UpdateManaRegen)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.UpdateManaRegen)) continue;

            try
            {
                mod.UpdateManaRegen(self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.UpdateManaRegen, e);
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
                    mod.UpdateDead(self);
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
        if (_binding.Active(HookId.PreKill))
        {
            var allowed = true;

            // 与语义：所有实例都跑（后面可能只是在记录），任一否决就不死。
            foreach (var mod in _binding.Of(self))
            {
                if (!mod.HookEnabled(HookId.PreKill)) continue;

                try
                {
                    if (!mod.PreKill(self, damageSource, dmg, hitDirection, pvp)) allowed = false;
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.PreKill, e);
                }
            }

            // 作者要求"别死"：既不走原版死亡，也不发 Kill。
            if (!allowed) return;
        }

        orig(self, damageSource, dmg, hitDirection, pvp);

        if (!_binding.Active(HookId.Kill)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.Kill)) continue;

            try
            {
                mod.Kill(self, damageSource, dmg, hitDirection, pvp);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.Kill, e);
            }
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
                    if (!mod.PreItemCheck(self)) run = false;
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
                mod.PostItemCheck(self);
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
                if (!mod.CanUseItem(self, item)) allowed = false;
            }
            catch (Exception e)
            {
                Fail(mod, HookId.CanUseItem, e);
            }
        }

        return allowed && orig(self);
    }

    /// <summary>接口给的是"这一帧的延迟值"，作者否决时连原版一起跳过（不加计时、不给药水病）。</summary>
    private void PotionDelay(On.Terraria.Player.orig_ApplyPotionDelay orig, Player self, Item sItem)
    {
        if (!_binding.Active(HookId.ApplyPotionDelay))
        {
            orig(self, sItem);
            return;
        }

        var delay = self.potionDelay;
        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ApplyPotionDelay)) continue;

            try
            {
                if (!mod.ApplyPotionDelay(self, sItem, delay)) return;
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ApplyPotionDelay, e);
            }
        }

        orig(self, sItem);
    }

    private void Pickup(On.Terraria.Player.orig_PickupItem orig, Player self, WorldItem itemToPickUp)
    {
        if (_binding.Active(HookId.OnPickup))
        {
            var item = itemToPickUp?.inner;

            if (item is not null)
            {
                foreach (var mod in _binding.Of(self))
                {
                    if (!mod.HookEnabled(HookId.OnPickup)) continue;

                    try
                    {
                        // false = 物品从地上消失但不进背包。
                        if (!mod.OnPickup(self, item)) return;
                    }
                    catch (Exception e)
                    {
                        Fail(mod, HookId.OnPickup, e);
                    }
                }
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
                mod.ResetInfoAccessories(self);
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

        if (!_binding.Active(HookId.ArmorSetBonusActivated)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ArmorSetBonusActivated)) continue;

            try
            {
                mod.ArmorSetBonusActivated(self);
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

        if (!_binding.Active(HookId.ArmorSetBonusHeld)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.ArmorSetBonusHeld)) continue;

            try
            {
                mod.ArmorSetBonusHeld(self, holdTime);
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

        // 只在这个预设真的换了之后才发：作者拿它当"换装完成"的信号。
        if (self.CurrentLoadoutIndex == old) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.OnEquipmentLoadoutSwitched)) continue;

            try
            {
                mod.OnEquipmentLoadoutSwitched(self, old, self.CurrentLoadoutIndex);
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

        if (!wasDead || !_binding.Active(HookId.OnRespawn)) return;

        foreach (var mod in _binding.Of(self))
        {
            if (!mod.HookEnabled(HookId.OnRespawn)) continue;

            try
            {
                mod.OnRespawn(self);
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
                    if (!mod.CanBeTeleportedTo(self, newPos, context)) return;
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.CanBeTeleportedTo, e);
                }
            }
        }

        orig(self, newPos, Style, extraInfo);
    }

    /// <summary>手机版只给一个样式号：翻成作者认的那四个场景名。</summary>
    private static string ContextOf(int style) => style switch
    {
        1 => "TeleportRod",
        2 => "TeleportationPotion",
        3 => "Wormhole",
        _ => "Teleport",
    };

    #endregion

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
                mod.ProcessTriggers(player, self);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ProcessTriggers, e);
            }
        }
    }

    /// <summary>同一 (类型, 钩子) 只记一条：这些入口多数每帧都走。</summary>
    private void Fail(ModPlayer mod, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(mod.GetType().Name.GetHashCode(), hook))) return;
        ContentErrors.Report($"[player] {mod.GetType().Name}.{hook} 异常：{Safe.Describe(exception)}");
    }
}