using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Patching;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Tables;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader.Pipelines;
/// <summary>增益管线：效果 / 施加 / 续时 / 文字 / 图标 / 取消。</summary>
internal sealed class BuffPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static BuffPipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];
    private uint _limitsTotal;

    public string Name => "buff";

    public IReadOnlyList<TableRequest> Tables =>
        [new(BuffTables.Set, () => ModContent.NextType(ContentKind.Buff), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>buff 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal struct Core : IGate
        {
            public static bool Wanted() => true;

            public static void Install() => Instance.InstallCore();
        }

        internal struct Icons : IGate
        {
            public static bool Wanted() => BuffIconCapture.Wanted();

            public static void Install() => BuffIconCapture.Install();
        }
    }

    public bool Install() => GateHub<BuffPipeline>.Run();

    private void InstallCore()
    {
        On.Terraria.Player.UpdateBuffs += UpdatePlayerBuffs;
        On.Terraria.NPC.UpdateNPC_BuffSetFlags += UpdateNpcBuffs;

        On.Terraria.Player.AddBuff_TryUpdatingExistingBuffTime += ReApplyPlayer;
        On.Terraria.NPC.AddBuff += AppliedNpc;

        On.Terraria.Lang.GetBuffName += BuffName;
        On.Terraria.Main.GetBuffTooltip += BuffTooltip;
        On.Terraria.Main.MouseText_DrawBuffTooltip += DrawBuffTooltip;

        On.GUIBuffs.ItemDraw += DrawGuiIcon;
        On.GUIBuffs.RemoveBuff_int += RemoveGuiBuff;

        On.Terraria.IO.WorldFile.LoadWorld += WorldLoaded;

        Log.Info($"[buff] 已挂（效果 / 续时 / 文字 / 自定义说明 / 图标 / 取消），模板 {Prime()}");
    }

    public void Refresh()
    {
        Prime();
        GrowImmuneArrays();
        PatchCompiledLimits();
    }

    private void WorldLoaded(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        orig(canCheckFileState);
        GrowImmuneArrays();
    }

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

        try
        {
            for (var b = 0; b < types.Length; b++)
            {
                var type = types[b];
                if (type <= 0 || times[b] <= 0) continue;

                var index = b;
                var mod = Of(type);
                if (mod is not null && mod.HookEnabled(HookId.Update))
                {
                    try { mod.Update(self, ref index); }
                    catch (Exception e) { Fail(mod.Name, HookId.Update, e); }
                }

                foreach (var global in GlobalBuffRegistry.For(HookId.Update))
                {
                    if (index != b) break;
                    try { global.Update(type, self, ref index); }
                    catch (Exception e) { Fail(global.Name, HookId.Update, e); }
                }

                b = index;
            }
        }
        catch (Exception e)
        {
            Fail("buff", HookId.Update, e);
        }
    }

    private void UpdateNpcBuffs(On.Terraria.NPC.orig_UpdateNPC_BuffSetFlags orig, NPC self, bool flag)
    {
        orig(self, flag);

        var types = self.buffType;
        var times = self.buffTime;
        if (types is null || times is null) return;

        try
        {
            for (var b = 0; b < types.Length; b++)
            {
                var type = types[b];
                if (type <= 0 || times[b] <= 0) continue;

                var index = b;
                var mod = Of(type);
                if (mod is not null && mod.HookEnabled(HookId.Update))
                {
                    try { mod.Update(self, ref index); }
                    catch (Exception e) { Fail(mod.Name, HookId.Update, e); }
                }

                foreach (var global in GlobalBuffRegistry.For(HookId.Update))
                {
                    if (index != b) break;
                    try { global.Update(type, self, ref index); }
                    catch (Exception e) { Fail(global.Name, HookId.Update, e); }
                }

                b = index;
            }
        }
        catch (Exception e)
        {
            Fail("buff", HookId.Update, e);
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
                foreach (var global in GlobalBuffRegistry.For(HookId.ReApply))
                {
                    try
                    {
                        if (global.ReApply(type, self, time, index)) return true;
                    }
                    catch (Exception e)
                    {
                        Fail(global.Name, HookId.ReApply, e);
                    }
                }

                var mod = Of(type);
                if (mod is not null && mod.HookEnabled(HookId.ReApply))
                {
                    try
                    {
                        if (mod.ReApply(self, time, index)) return true;
                    }
                    catch (Exception e)
                    {
                        Fail(mod.Name, HookId.ReApply, e);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Fail("buff", HookId.ReApply, e);
        }

        return orig(self, type, time);
    }

    private void AppliedNpc(On.Terraria.NPC.orig_AddBuff orig, NPC self, int type, int time, bool quiet)
    {
        try
        {
            var index = self.FindBuffIndex(type);
            if (index >= 0)
            {
                foreach (var global in GlobalBuffRegistry.For(HookId.ReApply))
                {
                    try
                    {
                        if (global.ReApply(type, self, time, index)) return;
                    }
                    catch (Exception e)
                    {
                        Fail(global.Name, HookId.ReApply, e);
                    }
                }

                var mod = Of(type);
                if (mod is not null && mod.HookEnabled(HookId.ReApply))
                {
                    try
                    {
                        if (mod.ReApply(self, time, index)) return;
                    }
                    catch (Exception e)
                    {
                        Fail(mod.Name, HookId.ReApply, e);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Fail("buff", HookId.ReApply, e);
        }

        orig(self, type, time, quiet);
    }

    #endregion

    #region 文字

    [ThreadStatic] private static bool _texting;

    private string BuffName(On.Terraria.Lang.orig_GetBuffName orig, int id)
    {
        var name = orig(id);
        if (_texting || !WantsText(id)) return name;

        var text = name ?? "";
        string tip;
        var outer = _texting;
        _texting = true;
        try { tip = Lang.GetBuffDescription(id) ?? ""; }
        finally { _texting = outer; }

        DispatchBuffText(id, ref text, ref tip);
        return text;
    }

    private string BuffTooltip(On.Terraria.Main.orig_GetBuffTooltip orig, Player player, int buffType)
    {
        var tip = orig(player, buffType);
        if (_texting || !WantsText(buffType)) return tip;

        var text = tip ?? "";
        string name;
        var outer = _texting;
        _texting = true;
        try { name = Lang.GetBuffName(buffType) ?? ""; }
        finally { _texting = outer; }

        DispatchBuffText(buffType, ref name, ref text);
        return text;
    }

    private bool WantsText(int type)
    {
        if (Of(type) is { } mod && mod.HookEnabled(HookId.ModifyBuffText)) return true;
        return GlobalBuffRegistry.For(HookId.ModifyBuffText).Count > 0;
    }

    private void DispatchBuffText(int type, ref string name, ref string tip)
    {
        var rare = 0;
        var outer = _texting;
        _texting = true;
        try
        {
            if (Of(type) is { } mod && mod.HookEnabled(HookId.ModifyBuffText))
            {
                try { mod.ModifyBuffText(ref name, ref tip, ref rare); }
                catch (Exception e) { Fail(mod.Name, HookId.ModifyBuffText, e); }
            }

            foreach (var global in GlobalBuffRegistry.For(HookId.ModifyBuffText))
            {
                try { global.ModifyBuffText(type, ref name, ref tip, ref rare); }
                catch (Exception e) { Fail(global.Name, HookId.ModifyBuffText, e); }
            }
        }
        finally { _texting = outer; }
    }

    private void DrawBuffTooltip(On.Terraria.Main.orig_MouseText_DrawBuffTooltip orig, Main self,
                                 string cursorText, string buffString, ref int X, ref int Y)
    {
        var tip = buffString ?? "";
        var font = Terraria.GameContent.FontAssets.MouseText.Value;
        var sizes = new List<Il2CppMicrosoft.Xna.Framework.Vector2>
        {
            font.MeasureString(tip),
            font.MeasureString(cursorText ?? ""),
        };
        var fixedCount = sizes.Count;

        foreach (var global in GlobalBuffRegistry.For(HookId.CustomBuffTipSize))
        {
            try { global.CustomBuffTipSize(tip, sizes); }
            catch (Exception e) { Fail(global.Name, HookId.CustomBuffTipSize, e); }
        }

        if (sizes.Count > fixedCount)
        {
            var largest = Il2CppMicrosoft.Xna.Framework.Vector2.Zero;
            foreach (var size in sizes)
            {
                largest.X = MathF.Max(largest.X, size.X);
                largest.Y = MathF.Max(largest.Y, size.Y);
            }

            if (X + largest.X + 24f > Main.screenWidth) X = (int)(Main.screenWidth - largest.X - 24f);
            if (Y + largest.Y + 4f > Main.screenHeight) Y = (int)(Main.screenHeight - largest.Y - 4f);
        }

        orig(self, cursorText, buffString, ref X, ref Y);

        var originY = Y + (int)font.MeasureString(tip).Y;
        foreach (var global in GlobalBuffRegistry.For(HookId.DrawCustomBuffTip))
        {
            try { global.DrawCustomBuffTip(tip, Main.spriteBatch, X, originY); }
            catch (Exception e) { Fail(global.Name, HookId.DrawCustomBuffTip, e); }
        }
    }

    #endregion

    #region 图标与取消

    private void PatchCompiledLimits()
    {
        var total = (uint)(VanillaCounts.Buff + ModContent.GetContent<ModBuff>().Count);
        if (total == _limitsTotal) return;
        _limitsTotal = total;

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

    private void DrawGuiIcon(On.GUIBuffs.orig_ItemDraw orig, GUIBuffs self, ItemGrid_Layout grid,
                             int index, Vector2 position, float scale)
    {
        var active = self.activeBuffs;
        if (index < 0 || index >= self.numActiveBuffs || active is null || index >= active.Length)
        {
            orig(self, grid, index, position, scale);
            return;
        }

        var slot = active[index];
        var types = Main.LocalPlayer?.buffType;
        if (types is null || (uint)slot >= (uint)types.Length || types[slot] <= 0)
        {
            orig(self, grid, index, position, scale);
            return;
        }

        var type = types[slot];
        if (!WantsDraw(type))
        {
            orig(self, grid, index, position, scale);
            return;
        }

        BuffDrawParams? parameters = null;
        try
        {
            parameters = BuildDrawParams(type, slot, position);
        }
        catch (Exception e)
        {
            Fail("buff", HookId.PreDraw, e);
        }

        if (parameters is null)
        {
            orig(self, grid, index, position, scale);
            return;
        }

        var before = new BuffDrawParams
        {
            Texture = parameters.Texture,
            Position = parameters.Position,
            TextPosition = parameters.TextPosition,
            SourceRectangle = parameters.SourceRectangle,
            MouseRectangle = parameters.MouseRectangle,
            DrawColor = parameters.DrawColor
        };

        var mod = Of(type);
        var modPre = mod is not null && mod.HookEnabled(HookId.PreDraw);
        var modPost = mod is not null && mod.HookEnabled(HookId.PostDraw);
        var spriteBatch = Main.spriteBatch;
        var draw = true;

        foreach (var global in GlobalBuffRegistry.For(HookId.PreDraw))
        {
            try
            {
                if (!global.PreDraw(spriteBatch, type, slot, ref parameters)) draw = false;
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
                draw = mod!.PreDraw(spriteBatch, slot, ref parameters);
            }
            catch (Exception e)
            {
                Fail(mod!.Name, HookId.PreDraw, e);
                draw = true;
            }
        }

        using (new BuffIconCapture.Scope(before, parameters, draw)) orig(self, grid, index, position, scale);

        if (modPost)
        {
            try
            {
                mod!.PostDraw(spriteBatch, slot, parameters);
            }
            catch (Exception e)
            {
                Fail(mod!.Name, HookId.PostDraw, e);
            }
        }

        foreach (var global in GlobalBuffRegistry.For(HookId.PostDraw))
        {
            try
            {
                global.PostDraw(spriteBatch, type, slot, parameters);
            }
            catch (Exception e)
            {
                Fail(global.Name, HookId.PostDraw, e);
            }
        }
    }

    private bool WantsDraw(int type)
    {
        if (Of(type) is { } mod && (mod.HookEnabled(HookId.PreDraw) || mod.HookEnabled(HookId.PostDraw))) return true;
        return GlobalBuffRegistry.For(HookId.PreDraw).Count > 0 || GlobalBuffRegistry.For(HookId.PostDraw).Count > 0;
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

    private static BuffDrawParams? BuildDrawParams(int type, int slot, Vector2 position)
    {
        var assets = TextureAssets.Buff;
        if (assets is null || type < 0 || type >= assets.Length) return null;

        var texture = assets[type]?.Value;
        if (texture is null) return null;

        var width = texture.Width;
        var height = texture.Height;
        var alphas = Main.buffAlpha;
        var alpha = alphas is not null && (uint)slot < (uint)alphas.Length ? alphas[slot] : 1f;

        return new BuffDrawParams
        {
            Texture = texture,
            Position = position,
            TextPosition = new Vector2(position.X, position.Y + height),
            SourceRectangle = new Rectangle(0, 0, width, height),
            MouseRectangle = new Rectangle((int)position.X, (int)position.Y, width, height),
            DrawColor = new Color(alpha, alpha, alpha, alpha),
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
        ContentErrors.Line($"[buff] {hook} 异常：{Safe.Dump(exception)}");
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
