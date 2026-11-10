using Terraria;

namespace Pylon.ModLoader;

/// <summary>把更新回调接在计算完成或首次移动之前，避免用整帧前后替代精确时机。</summary>
internal static class PlayerUpdateHooks
{
    private static readonly HashSet<nint> Moved = [];
    private static bool _installed;
    private static PlayerPipeline Players => PlayerPipeline.Instance;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Player.RecalculateLuck += Luck;
        On.Terraria.Player.CapAttackSpeeds += Misc;
        On.Terraria.Player.HorizontalMovement += RunSpeeds;
        On.Terraria.Player.Update += Update;
        On.Terraria.Player.WetCollision += Wet;
        On.Terraria.Player.DryCollision += Dry;
        On.Terraria.Player.SlopingCollision += Sloping;
        On.Terraria.Player.BordersMovement += Borders;
        On.Terraria.Main.DoUpdate_WhilePaused += Paused;
    }

    private static void Luck(On.Terraria.Player.orig_RecalculateLuck orig, Player self)
    {
        if (!Players.ModsActive(HookId.PreModifyLuck) && !Players.ModsActive(HookId.ModifyLuck))
        {
            orig(self);
            return;
        }
        var luck = self.luck;
        var vanilla = true;
        var mods = Players.ModsOf(self);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.PreModifyLuck)) continue;
            try { if (!mod.PreModifyLuck(self, ref luck)) vanilla = false; }
            catch (Exception e) { Fail(mod, HookId.PreModifyLuck, e); }
        }
        if (vanilla) { orig(self); luck = self.luck; }
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.ModifyLuck)) continue;
            try { mod.ModifyLuck(self, ref luck); }
            catch (Exception e) { Fail(mod, HookId.ModifyLuck, e); }
        }
        self.luck = luck;
    }

    private static void Misc(On.Terraria.Player.orig_CapAttackSpeeds orig, Player self)
    {
        orig(self);
        Dispatch(self, HookId.PostUpdateMiscEffects);
    }

    private static void RunSpeeds(On.Terraria.Player.orig_HorizontalMovement orig, Player self)
    {
        Dispatch(self, HookId.PostUpdateRunSpeeds);
        orig(self);
    }

    private static void Update(On.Terraria.Player.orig_Update orig, Player self, int index)
    {
        // 碰撞分支在一帧内可能走多次，只在当前 Update 窗口首次进入时派发。
        var outer = Moved.Remove(self.Pointer);
        try { orig(self, index); }
        finally
        {
            Moved.Remove(self.Pointer);
            if (outer) Moved.Add(self.Pointer);
        }
    }

    private static void BeforeMovement(Player self)
    {
        if (self.dead || !Players.ModsActive(HookId.PreUpdateMovement) || !Moved.Add(self.Pointer)) return;
        Dispatch(self, HookId.PreUpdateMovement);
    }

    private static void Wet(On.Terraria.Player.orig_WetCollision orig, Player self,
                            bool fallThrough, bool ignorePlatforms, float speed)
    {
        BeforeMovement(self);
        orig(self, fallThrough, ignorePlatforms, speed);
    }

    private static void Dry(On.Terraria.Player.orig_DryCollision orig, Player self, bool fallThrough, bool ignorePlatforms)
    {
        BeforeMovement(self);
        orig(self, fallThrough, ignorePlatforms);
    }

    private static void Sloping(On.Terraria.Player.orig_SlopingCollision orig, Player self, bool fallThrough, bool ignorePlatforms)
    {
        BeforeMovement(self);
        orig(self, fallThrough, ignorePlatforms);
    }

    private static void Borders(On.Terraria.Player.orig_BordersMovement orig, Player self)
    {
        BeforeMovement(self);
        orig(self);
    }

    private static void Paused(On.Terraria.Main.orig_DoUpdate_WhilePaused orig)
    {
        orig();
        if (!Main.gameMenu && Main.LocalPlayer is { } player) Dispatch(player, HookId.UpdateAutopause);
    }

    private static void Dispatch(Player player, HookId hook)
    {
        if (!Players.ModsActive(hook)) return;
        foreach (var mod in Players.ModsOf(player))
        {
            if (!mod.HookEnabled(hook)) continue;
            try
            {
                switch (hook)
                {
                    case HookId.PostUpdateMiscEffects: mod.PostUpdateMiscEffects(player); break;
                    case HookId.PostUpdateRunSpeeds: mod.PostUpdateRunSpeeds(player); break;
                    case HookId.PreUpdateMovement: mod.PreUpdateMovement(player); break;
                    case HookId.UpdateAutopause: mod.UpdateAutopause(player); break;
                }
            }
            catch (Exception e) { Fail(mod, hook, e); }
        }
    }

    private static void Fail(ModPlayer mod, HookId hook, Exception e) =>
        ContentErrors.Report($"[playerupdate] {mod.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
