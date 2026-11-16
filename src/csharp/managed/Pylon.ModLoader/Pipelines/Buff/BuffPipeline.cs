using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>增益管线：效果 / 施加 / 续时 / 文字 / 图标 / 取消。</summary>
internal sealed class BuffPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static BuffPipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    public string Name => "buff";

    public IReadOnlyList<TableRequest> Tables =>
        [new(BuffTables.Set, () => ModContent.NextType(ContentKind.Buff), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.UpdateBuffs += UpdatePlayerBuffs;
        On.Terraria.NPC.UpdateNPC_BuffSetFlags += UpdateNpcBuffs;

        On.Terraria.Player.AddBuff_TryUpdatingExistingBuffTime += ReApplyPlayer;
        On.Terraria.NPC.AddBuff += AppliedNpc;

        On.Terraria.Lang.GetBuffName += BuffName;
        On.Terraria.Main.GetBuffTooltip += BuffTooltip;

        On.Terraria.Main.DrawBuffIcon += DrawIcon;
        On.GUIBuffs.ItemDraw += DrawGuiIcon;
        On.GUIBuffs.RemoveBuff_int += RemoveGuiBuff;
        BuffIconCapture.Install();

        var count = Prime();
        PatchCompiledLimits();

        Log.Info($"[buff] 已挂（效果 / 续时 / 文字 / 图标 / 取消），模板 {count}");
        return true;
    }

    public void Refresh() => Prime();

    internal void GrowImmuneArrays()
    {
        var need = LoaderHost.Current.Content.NextType(ContentKind.Buff);
        if (need <= 0) return;

        try
        {
            var players = Main.player;
            if (players is not null)
            {
                for (var i = 0; i < players.Length; i++) GrowPlayer(players[i], need);
            }

            var npcs = Main.npc;
            if (npcs is not null)
            {
                for (var i = 0; i < npcs.Length; i++) GrowNpc(npcs[i], need);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[buff] buffImmune 扩容失败：{e.Message}");
        }
    }

    #region 效果

    private void UpdatePlayerBuffs(On.Terraria.Player.orig_UpdateBuffs orig, Player self, int i)
    {
        orig(self, i);

        var types = self.buffType;
        var times = self.buffTime;
        if (types is null || times is null) return;

        if (i < 0 || i >= types.Length || i >= times.Length) return;
        if (times[i] <= 0 || types[i] <= 0) return;

        UpdateBuff(types, times, i, self, null);
        UpdateGlobalBuff(types, times, i, self, null);
    }

    private void UpdateNpcBuffs(On.Terraria.NPC.orig_UpdateNPC_BuffSetFlags orig, NPC self, bool flag)
    {
        orig(self, flag);

        var types = self.buffType;
        var times = self.buffTime;
        if (types is null || times is null) return;

        for (var i = 0; i < types.Length && i < times.Length; i++)
        {
            if (times[i] <= 0 || types[i] <= 0) continue;

            UpdateBuff(types, times, i, null, self);
            UpdateGlobalBuff(types, times, i, null, self);
        }
    }

    private void UpdateBuff(Il2CppStructArray<int> types, Il2CppStructArray<int> times, int index,
                            Player? player, NPC? npc)
    {
        var type = types[index];
        if (type < VanillaCounts.Buff) return;

        var mod = Of(type);
        if (mod is null || !mod.HookEnabled(HookId.Update)) return;

        try
        {
            var slot = index;
            if (player is not null) mod.Update(player, ref slot);
            else mod.Update(npc!, ref slot);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.Update, e);
        }
    }

    private void UpdateGlobalBuff(Il2CppStructArray<int> types, Il2CppStructArray<int> times, int index,
                                  Player? player, NPC? npc)
    {
        var globals = GlobalBuffRegistry.For(HookId.Update);
        if (globals.Count == 0) return;

        var type = types[index];
        var slot = index;

        foreach (var global in globals)
        {
            try
            {
                if (player is not null) global.Update(type, player, ref slot);
                else global.Update(type, npc!, ref slot);
            }
            catch (Exception e)
            {
                Fail(global.Name, HookId.Update, e);
            }
        }
    }

    #endregion

    #region 施加与续时

    private bool ReApplyPlayer(On.Terraria.Player.orig_AddBuff_TryUpdatingExistingBuffTime orig, Player self,
                               int type, int time)
    {
        try
        {
            var index = self.FindBuffIndex(type);

            if (index >= 0)
            {
                var stop = false;
                foreach (var global in GlobalBuffRegistry.For(HookId.ReApply))
                {
                    try
                    {
                        if (global.ReApply(type, self, time, index)) stop = true;
                    }
                    catch (Exception e)
                    {
                        Fail(global.Name, HookId.ReApply, e);
                    }
                }

                if (stop) return true;
            }

            var mod = Of(type);
            if (mod is null) return orig(self, type, time);

            var renew = index < 0 || !mod.HookEnabled(HookId.ReApply) || !mod.ReApply(self, time, index);
            return renew ? orig(self, type, time) : true;
        }
        catch (Exception e)
        {
            Fail("buff", HookId.ReApply, e);
            return orig(self, type, time);
        }
    }

    private void AppliedNpc(On.Terraria.NPC.orig_AddBuff orig, NPC self, int type, int time, bool quiet)
    {
        var existed = self.FindBuffIndex(type) >= 0;

        var mod = Of(type);
        var globals = GlobalBuffRegistry.For(HookId.ReApply);

        if (existed && (mod is not null && mod.HookEnabled(HookId.ReApply) || globals.Count > 0))
        {
            var index = self.FindBuffIndex(type);
            var renew = true;

            foreach (var global in globals)
            {
                try
                {
                    if (global.ReApply(type, self, time, index)) renew = false;
                }
                catch (Exception e)
                {
                    Fail(global.Name, HookId.ReApply, e);
                }
            }

            if (mod is not null && mod.HookEnabled(HookId.ReApply))
            {
                try
                {
                    if (mod.ReApply(self, time, index)) renew = false;
                }
                catch (Exception e)
                {
                    Fail(mod.Name, HookId.ReApply, e);
                }
            }

            if (!renew) return;
        }

        orig(self, type, time, quiet);
    }

    #endregion

    #region 文字

    private string BuffName(On.Terraria.Lang.orig_GetBuffName orig, int id)
    {
        var name = orig(id);

        var mod = Of(id);
        var globals = GlobalBuffRegistry.For(HookId.ModifyBuffText);
        if (mod is null && globals.Count == 0) return name;

        var tip = "";
        var rare = 0;

        if (mod is not null && mod.HookEnabled(HookId.ModifyBuffText))
        {
            try
            {
                mod.ModifyBuffText(ref name, ref tip, ref rare);
            }
            catch (Exception e)
            {
                Fail(mod.Name, HookId.ModifyBuffText, e);
            }
        }

        foreach (var global in globals)
        {
            try
            {
                global.ModifyBuffText(id, ref name, ref tip, ref rare);
            }
            catch (Exception e)
            {
                Fail(global.Name, HookId.ModifyBuffText, e);
            }
        }

        return name;
    }

    private string BuffTooltip(On.Terraria.Main.orig_GetBuffTooltip orig, Player player, int buffType)
    {
        var tip = orig(player, buffType);

        var mod = Of(buffType);
        var globals = GlobalBuffRegistry.For(HookId.ModifyBuffText);
        if (mod is null && globals.Count == 0) return tip;

        var name = "";
        var rare = 0;

        if (mod is not null && mod.HookEnabled(HookId.ModifyBuffText))
        {
            try
            {
                mod.ModifyBuffText(ref name, ref tip, ref rare);
            }
            catch (Exception e)
            {
                Fail(mod.Name, HookId.ModifyBuffText, e);
            }
        }

        foreach (var global in globals)
        {
            try
            {
                global.ModifyBuffText(buffType, ref name, ref tip, ref rare);
            }
            catch (Exception e)
            {
                Fail(global.Name, HookId.ModifyBuffText, e);
            }
        }

        return tip;
    }

    #endregion

    #region 图标与取消


    private void PatchCompiledLimits()
    {
        var total = (uint)(VanillaCounts.Buff + ModContent.GetContent<ModBuff>().Count);
        var vanilla = (uint)VanillaCounts.Buff;

        var patches = new (System.Type Class, string Method, int Argc, uint Old, uint New, int Below)[]
        {
            (typeof(GUIBuffs), "Draw", 0, vanilla - 2, total - 2, 1),
            (typeof(GUIBuffs), "Draw", 0, vanilla, total, 1),
            (typeof(Main), "GetNurseHealCost", 0, vanilla - 1, total - 1, 0),
            (typeof(Main), "NPCChatText_DoNurseHeal", 1, vanilla - 1, total - 1, 0),
            (typeof(GUINPCDialogue), "Option1Clicked", 1, vanilla - 1, total - 1, 0),
            (typeof(GUINPCDialogue), "SetupButtonText", 6, vanilla - 1, total - 1, 0),
            (typeof(NPC), "RequestBuffRemoval", 1, vanilla - 1, total - 1, 0),
        };

        var ok = 0;
        foreach (var p in patches)
        {
            try
            {
                var name = p.Class.FullName ?? p.Class.Name;
                var changed = RuntimeLimitInstaller.PatchMethod(p.Class, p.Method, p.Argc,
                    p.Old, p.New, p.Below != 0, false, Log);
                if (changed > 0) ok++;
                else Log.Warn($"[buff] 上限补丁没生效：{name}.{p.Method} {p.Old}→{p.New}（结果 {changed}）");
            }
            catch (Exception e)
            {
                Log.Warn($"[buff] 上限补丁异常：{p.Class.Name}.{p.Method}：{e.Message}");
            }
        }

        Log.Info($"[buff] 编译期上限补丁：{ok}/{patches.Length} 处（buff 总数 {total}）");
    }

    private int DrawIcon(On.Terraria.Main.orig_DrawBuffIcon orig, int drawBuffText, int buffSlotOnPlayer, int x, int y)
    {
        var player = Main.player[Main.myPlayer];
        var types = player?.buffType;
        if (types is null || buffSlotOnPlayer < 0 || buffSlotOnPlayer >= types.Length)
            return orig(drawBuffText, buffSlotOnPlayer, x, y);

        var type = types[buffSlotOnPlayer];
        if (type <= 0) return orig(drawBuffText, buffSlotOnPlayer, x, y);

        return AroundIcon(type, buffSlotOnPlayer, new Vector2(x, y), false,
                          () => orig(drawBuffText, buffSlotOnPlayer, x, y));
    }

    private void DrawGuiIcon(On.GUIBuffs.orig_ItemDraw orig, GUIBuffs self, ItemGrid_Layout grid,
                             int index, Vector2 position, float scale)
    {
        var active = self.activeBuffs;
        var types = Main.LocalPlayer?.buffType;
        if (index < 0 || index >= self.numActiveBuffs || active is null || index >= active.Length || types is null)
        {
            orig(self, grid, index, position, scale);
            return;
        }
        var slot = active[index];
        if ((uint)slot >= (uint)types.Length || types[slot] <= 0)
        {
            orig(self, grid, index, position, scale);
            return;
        }
        AroundIcon(types[slot], slot, position, true, () =>
        {
            orig(self, grid, index, position, scale);
            return 0;
        });
    }

    private int AroundIcon(int type, int buffSlotOnPlayer, Vector2 position, bool gui, Func<int> original)
    {
        var mod = Of(type);
        var modPre = mod is not null && mod.HookEnabled(HookId.PreDraw);
        var modPost = mod is not null && mod.HookEnabled(HookId.PostDraw);
        var pre = GlobalBuffRegistry.For(HookId.PreDraw);
        var post = GlobalBuffRegistry.For(HookId.PostDraw);
        if (!modPre && !modPost && pre.Count == 0 && post.Count == 0)
            return original();

        BuffDrawParams? parameters = null;
        try
        {
            parameters = BuildDrawParams(type, buffSlotOnPlayer, (int)position.X, (int)position.Y);
            if (parameters is not null && gui)
            {
                parameters.Position = position;
                parameters.TextPosition = new Vector2(position.X, position.Y + parameters.Texture.Height);
                var alphas = Main.buffAlpha;
                var alpha = alphas is not null && (uint)buffSlotOnPlayer < (uint)alphas.Length ? alphas[buffSlotOnPlayer] : 1f;
                parameters.DrawColor = new Color(alpha, alpha, alpha, alpha);
            }
        }
        catch (Exception e)
        {
            Fail("buff", HookId.PreDraw, e);
        }

        if (parameters is null) return original();
        var before = new BuffDrawParams
        {
            Texture = parameters.Texture, Position = parameters.Position, TextPosition = parameters.TextPosition,
            SourceRectangle = parameters.SourceRectangle, MouseRectangle = parameters.MouseRectangle,
            DrawColor = parameters.DrawColor
        };

        var spriteBatch = Main.spriteBatch;
        var draw = true;

        foreach (var global in pre)
        {
            try
            {
                if (!global.PreDraw(spriteBatch, type, buffSlotOnPlayer, ref parameters)) draw = false;
            }
            catch (Exception e)
            {
                Fail(global.Name, HookId.PreDraw, e);
            }
        }

        if (draw && modPre)
        {
            try
            {
                draw = mod!.PreDraw(spriteBatch, buffSlotOnPlayer, ref parameters);
            }
            catch (Exception e)
            {
                Fail(mod!.Name, HookId.PreDraw, e);
                draw = true;
            }
        }

        int result;
        using (new BuffIconCapture.Scope(before, parameters, draw)) result = original();

        if (modPost)
        {
            try
            {
                mod!.PostDraw(spriteBatch, buffSlotOnPlayer, parameters);
            }
            catch (Exception e)
            {
                Fail(mod!.Name, HookId.PostDraw, e);
            }
        }

        foreach (var global in post)
        {
            try
            {
                global.PostDraw(spriteBatch, type, buffSlotOnPlayer, parameters);
            }
            catch (Exception e)
            {
                Fail(global.Name, HookId.PostDraw, e);
            }
        }

        return result;
    }

    private void RemoveGuiBuff(On.GUIBuffs.orig_RemoveBuff_int orig, GUIBuffs self, int slot)
    {
        var types = Main.LocalPlayer?.buffType;
        if (types is not null && (uint)slot < (uint)types.Length && types[slot] > 0)
        {
            var type = types[slot];
            foreach (var global in GlobalBuffRegistry.For(HookId.RightClick))
            {
                try { if (!global.RightClick(type, slot)) return; }
                catch (Exception e) { Fail(global.Name, HookId.RightClick, e); }
            }
            var mod = Of(type);
            if (mod is not null && mod.HookEnabled(HookId.RightClick))
            {
                try { if (!mod.RightClick(slot)) return; }
                catch (Exception e) { Fail(mod.Name, HookId.RightClick, e); }
            }
        }
        orig(self, slot);
    }

    private static BuffDrawParams? BuildDrawParams(int type, int slot, int x, int y)
    {
        var assets = TextureAssets.Buff;
        if (assets is null || type < 0 || type >= assets.Length) return null;

        var texture = assets[type]?.Value;
        if (texture is null) return null;

        var width = texture.Width;
        var height = texture.Height;

        return new BuffDrawParams
        {
            Texture = texture,
            Position = new Vector2(x, y),
            TextPosition = new Vector2(x + width, y + height * 0.5f - 8f),
            SourceRectangle = new Rectangle(0, 0, width, height),
            MouseRectangle = new Rectangle(x, y, width, height),
            DrawColor = Color.White,
        };
    }

    #endregion

    private int Prime()
    {
        var count = 0;
        foreach (var buff in ModContent.GetContent<ModBuff>())
        {
            buff.Hooks = HookScan.Of(buff.GetType(), typeof(ModBuff));
            count++;
        }

        return count;
    }

    private static ModBuff? Of(int type) =>
        type < VanillaCounts.Buff ? null : ModContent.Find(ContentKind.Buff, type) as ModBuff;

    private static void GrowPlayer(Player player, int need)
    {
        if (player is null) return;

        try
        {
            var old = player.buffImmune;
            if (old is not null && old.Length >= need) return;
            player.buffImmune = Grow(old, need);
        }
        catch (Exception e)
        {
            Safe.Report("Player.buffImmune", e);
        }
    }

    private static void GrowNpc(NPC npc, int need)
    {
        if (npc is null) return;

        try
        {
            var old = npc.buffImmune;
            if (old is not null && old.Length >= need) return;
            npc.buffImmune = Grow(old, need);
        }
        catch (Exception e)
        {
            Safe.Report("NPC.buffImmune", e);
        }
    }

    private static bool[] Grow(Il2CppStructArray<bool>? old, int need)
    {
        var grown = new bool[need];
        if (old is not null)
        {
            for (var i = 0; i < old.Length && i < need; i++) grown[i] = old[i];
        }

        return grown;
    }

    private void Fail(string name, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(name.GetHashCode(), hook))) return;
        ContentErrors.Report($"[buff] {hook} 异常：{Safe.Describe(exception)}");
    }
}

/// <summary>Buff 编号相关的原生表：Buff、Lang、贴图、物品上的 Buff 槽。</summary>
internal static class BuffTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.ID.BuffID.Sets),
        typeof(Terraria.Main),
        typeof(Terraria.Lang),
        typeof(Terraria.GameContent.TextureAssets),
    ];

    private static readonly string[] Skip =
    [
        "item",
        "timeItemSlotCannotBeReusedFor",
        "itemFrame",
        "itemFrameCounter",
    ];

    internal static readonly TableSet Set = new("Buff", () => Terraria.ID.BuffID.Count, Types, skip: Skip);

}
