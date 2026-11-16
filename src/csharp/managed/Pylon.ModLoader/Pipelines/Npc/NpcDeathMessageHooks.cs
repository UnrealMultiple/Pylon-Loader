using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>死亡消息五入口链：checkDead/CelebrateBossDeath 换入死亡状态，NewText×2/Broadcast 在城镇/首领死亡色上截获并走 GlobalNpc→ModNpc 的 ModifyDeathMessage 链。</summary>
internal static class NpcDeathMessageHooks
{
    private static readonly Color TownDeathColor = new(255, 25, 25);
    private static readonly Color BossDeathColor = new(175, 75, 255);

    [ThreadStatic] private static NPC? _dying;
    [ThreadStatic] private static bool _deathMessageDone;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Install()
    {
        On.Terraria.NPC.checkDead += CheckDead;
        On.Terraria.NPC.DoDeathEvents_CelebrateBossDeath += CelebrateBossDeath;
        On.Terraria.Main.NewText_string_Color_bool += NewTextColor;
        On.Terraria.Main.NewText_string_byte_byte_byte_bool += NewTextBytes;
        On.Terraria.Chat.ChatHelper.BroadcastChatMessage += Broadcast;
        Log.Info("[deathmsg] 已挂（checkDead/CelebrateBossDeath 状态域 + NewText×2/Broadcast 出口）");
    }

    private static bool WantsDeathMessage(NPC npc)
    {
        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is not null && mod.HookEnabled(HookId.ModifyDeathMessage)) return true;
        return GlobalNpcRegistry.AnyWith(HookId.ModifyDeathMessage);
    }

    private static void CheckDead(On.Terraria.NPC.orig_checkDead orig, NPC self)
    {
        if (!WantsDeathMessage(self))
        {
            orig(self);
            return;
        }
        var outer = _dying;
        var outerDone = _deathMessageDone;
        _dying = self;
        _deathMessageDone = false;
        try { orig(self); }
        finally
        {
            _dying = outer;
            _deathMessageDone = outerDone;
        }
    }

    private static void CelebrateBossDeath(On.Terraria.NPC.orig_DoDeathEvents_CelebrateBossDeath orig, NPC self)
    {
        if (!WantsDeathMessage(self))
        {
            orig(self);
            return;
        }
        var outer = _dying;
        var outerDone = _deathMessageDone;
        _dying = self;
        _deathMessageDone = false;
        try { orig(self); }
        finally
        {
            _dying = outer;
            _deathMessageDone = outerDone;
        }
    }

    private static bool DeathMessage(ref NetworkText text, ref Color color)
    {
        var dying = _dying;
        if (dying is null || _deathMessageDone || !(color.Equals(TownDeathColor) || color.Equals(BossDeathColor))) return true;
        _deathMessageDone = true;

        NetworkText custom = null!;
        var mod = NpcPipeline.Instance.InstanceOf(dying);
        if (mod is not null && mod.DeathMessage is not null)
            custom = NetworkText.FromLiteral(mod.DeathMessage.Format(dying.GetFullNetName().ToString()));

        if (ModifyDeathMessage(dying, ref custom, ref color)) return false;
        if (custom is not null) text = custom;
        return true;
    }

    private static bool ModifyDeathMessage(NPC npc, ref NetworkText customText, ref Color color)
    {
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.ModifyDeathMessage))
        {
            try
            {
                if (!global.ModifyDeathMessage(npc, ref customText, ref color)) return true;
            }
            catch (Exception e) { Report(global, e); }
        }

        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is null || !mod.HookEnabled(HookId.ModifyDeathMessage)) return false;
        try { return !mod.ModifyDeathMessage(ref customText, ref color); }
        catch (Exception e)
        {
            Report(mod, e);
            return false;
        }
    }

    private static void NewTextColor(On.Terraria.Main.orig_NewText_string_Color_bool orig, string? newText, Color color, bool onlyCurrentPlayer)
    {
        NetworkText text = null!;
        if (_dying is not null && !DeathMessage(ref text, ref color)) return;
        orig(text is null ? newText : text.ToString(), color, onlyCurrentPlayer);
    }

    private static void NewTextBytes(On.Terraria.Main.orig_NewText_string_byte_byte_byte_bool orig, string? newText,
        byte r, byte g, byte b, bool onlyCurrentPlayer)
    {
        if (_dying is null)
        {
            orig(newText, r, g, b, onlyCurrentPlayer);
            return;
        }
        NetworkText text = null!;
        var color = new Color(r, g, b);
        if (!DeathMessage(ref text, ref color)) return;
        orig(text is null ? newText : text.ToString(), color.R, color.G, color.B, onlyCurrentPlayer);
    }

    private static void Broadcast(On.Terraria.Chat.ChatHelper.orig_BroadcastChatMessage orig, NetworkText text, Color color, int excludedPlayer)
    {
        if (_dying is not null && !DeathMessage(ref text, ref color)) return;
        orig(text, color, excludedPlayer);
    }

    private static readonly HashSet<string> _reported = [];

    private static void Report(ModNpc mod, Exception exception)
    {
        if (!_reported.Add(mod.Type + "/" + exception.GetType().Name)) return;
        ContentErrors.Report($"[deathmsg] {mod.Name}.ModifyDeathMessage 异常：{Safe.Describe(exception)}");
    }

    private static void Report(GlobalNpc global, Exception exception)
    {
        if (!_reported.Add(global.Name + "/" + exception.GetType().Name)) return;
        ContentErrors.Report($"[deathmsg] {global.Name}.ModifyDeathMessage 异常：{Safe.Describe(exception)}");
    }
}
