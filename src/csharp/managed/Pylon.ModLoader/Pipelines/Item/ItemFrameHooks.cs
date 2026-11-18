using Pylon.ModLoader.Content;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>手持帧钩子：玩家帧算完后改手持动作帧——动画中走 UseItemFrame，否则能举时走 HoldItemFrame。</summary>
internal static class ItemFrameHooks
{
    private static bool GlobalFrames;

    internal static void Install()
    {
        GlobalFrames = GlobalItemRegistry.AnyWith(HookId.UseItemFrame, HookId.HoldItemFrame);
        On.Terraria.Player.PlayerFrame += PlayerFrame;
    }

    private static bool Wanted(Item? item) =>
        item is not null && item.type > 0 && (GlobalFrames || ModContent.IsCustom(ContentKind.Item, item.type));

    private static void PlayerFrame(On.Terraria.Player.orig_PlayerFrame orig, Player self)
    {
        orig(self);

        Item item;
        try
        {
            var slot = self.selectedItem;
            if (slot < 0 || slot >= 59) return;

            item = self.inventory[slot];
            if (item is null || item.stack <= 0 || !Wanted(item)) return;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[item] UseItemFrame 异常：{Safe.Describe(e)}");
            return;
        }

        if (self.itemAnimation > 0) UseItemFrame(item, self);
        else if (self.CanVisuallyHoldItem(item)) HoldItemFrame(item, self);
    }

    private static void UseItemFrame(Item item, Player player)
    {
        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.UseItemFrame))
        {
            try { m.UseItemFrame(player); }
            catch (Exception e) { Report(m.FullKey, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.UseItemFrame))
        {
            try { g.UseItemFrame(item, player); }
            catch (Exception e) { Report(g.FullKey, e); }
        }
    }

    private static void HoldItemFrame(Item item, Player player)
    {
        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.HoldItemFrame))
        {
            try { m.HoldItemFrame(player); }
            catch (Exception e) { Report(m.FullKey, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.HoldItemFrame))
        {
            try { g.HoldItemFrame(item, player); }
            catch (Exception e) { Report(g.FullKey, e); }
        }
    }

    private static void Report(string key, Exception e) =>
        ContentErrors.Report($"[item] {key} 异常：{Safe.Describe(e)}");
}
