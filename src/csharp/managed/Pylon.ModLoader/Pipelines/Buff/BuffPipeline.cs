using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 增益管线：效果 / 施加 / 续时 / 文字 / 图标 / 取消。
/// buff 没有实体（一个 buff = 类型 + 剩余时间），所以没有默认值接管，也没有实体绑定。
/// </summary>
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

        On.Terraria.Player.AddBuff_ActuallyTryToAddTheBuff += AppliedPlayer;
        On.Terraria.Player.AddBuff_TryUpdatingExistingBuffTime += ReApplyPlayer;
        On.Terraria.NPC.AddBuff += AppliedNpc;

        On.Terraria.Lang.GetBuffName += BuffName;
        On.Terraria.Main.GetBuffTooltip += BuffTooltip;

        On.Terraria.Main.DrawBuffIcon += DrawIcon;
        On.GUIBuffs.ItemDraw += DrawGuiIcon;
        On.GUIBuffs.RemoveBuff_int += RemoveGuiBuff;
        On.Terraria.Player.DelBuff += DelBuff;
        BuffIconCapture.Install();

        var count = Prime();
        PatchCompiledLimits();

        Log.Info($"[buff] 已挂（效果 / 施加 / 续时 / 文字 / 图标 / 取消），模板 {count}");
        return true;
    }

    /// <summary>位集按 typeof(ModBuff) 算一次挂在模板上：buff 效果每帧每格都会走门控。</summary>
    public void Refresh() => Prime();

    /// <summary>buffImmune 的槽位要盖住所有已注册的 buff；新生成的实例由这里再补一次。</summary>
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

        // 只处理游戏这次问到的槽位：遍历整张表会让同一 buff 一帧被跑 N 次。
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

        // 游戏在方法内部自己循环，没有槽位参数：这次调用每帧每 NPC 只有一次。
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
        if (mod is null) return;

        var hook = player is not null ? HookId.UpdatePlayer : HookId.UpdateNPC;
        if (!mod.HookEnabled(hook)) return;

        try
        {
            if (player is not null) mod.UpdatePlayer(player, index);
            else mod.UpdateNPC(npc!, index);
        }
        catch (Exception e)
        {
            Fail(mod.Name, hook, e);
        }
    }

    private void UpdateGlobalBuff(Il2CppStructArray<int> types, Il2CppStructArray<int> times, int index,
                                  Player? player, NPC? npc)
    {
        var hook = player is not null ? HookId.UpdatePlayer : HookId.UpdateNPC;
        var globals = GlobalBuffRegistry.For(hook);
        if (globals.Count == 0) return;

        var type = types[index];
        var slot = index;

        foreach (var global in globals)
        {
            try
            {
                if (player is not null) global.UpdatePlayer(type, player, ref slot);
                else global.UpdateNPC(type, npc!, ref slot);
            }
            catch (Exception e)
            {
                Fail(global.Name, hook, e);
            }
        }
    }

    #endregion

    #region 施加与续时

    private bool AppliedPlayer(On.Terraria.Player.orig_AddBuff_ActuallyTryToAddTheBuff orig, Player self, int type,
                               int time)
    {
        var result = orig(self, type, time);

        if (!result) return false;

        var mod = Of(type);
        if (mod is null || !mod.HookEnabled(HookId.ApplyPlayer)) return true;

        try
        {
            mod.ApplyPlayer(self, time);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.ApplyPlayer, e);
        }

        return true;
    }

    private bool ReApplyPlayer(On.Terraria.Player.orig_AddBuff_TryUpdatingExistingBuffTime orig, Player self,
                               int type, int time)
    {
        try
        {
            var index = self.FindBuffIndex(type);

            // 续不续是全局策略：任一 Global 说要停就停。
            if (index >= 0)
            {
                var stop = false;
                foreach (var global in GlobalBuffRegistry.For(HookId.ReApplyPlayer))
                {
                    var slot = index;
                    try
                    {
                        if (global.ReApplyPlayer(type, self, time, ref slot)) stop = true;
                    }
                    catch (Exception e)
                    {
                        Fail(global.Name, HookId.ReApplyPlayer, e);
                    }
                }

                if (stop) return true;
            }

            var mod = Of(type);
            if (mod is null) return orig(self, type, time);

            var renew = index < 0 || !mod.HookEnabled(HookId.ReApplyPlayer) || mod.ReApplyPlayer(self, time, index);
            return renew ? orig(self, type, time) : true;
        }
        catch (Exception e)
        {
            Fail("buff", HookId.ReApplyPlayer, e);
            return orig(self, type, time);
        }
    }

    private void AppliedNpc(On.Terraria.NPC.orig_AddBuff orig, NPC self, int type, int time, bool quiet)
    {
        var existed = self.FindBuffIndex(type) >= 0;

        var mod = Of(type);
        var globals = GlobalBuffRegistry.For(HookId.ReApplyNPC);

        if (existed && (mod is not null && mod.HookEnabled(HookId.ReApplyNPC) || globals.Count > 0))
        {
            var index = self.FindBuffIndex(type);
            var renew = true;

            foreach (var global in globals)
            {
                var slot = index;
                try
                {
                    if (global.ReApplyNPC(type, self, time, ref slot)) renew = false;
                }
                catch (Exception e)
                {
                    Fail(global.Name, HookId.ReApplyNPC, e);
                }
            }

            if (mod is not null && mod.HookEnabled(HookId.ReApplyNPC))
            {
                try
                {
                    if (!mod.ReApplyNPC(self, time, index)) renew = false;
                }
                catch (Exception e)
                {
                    Fail(mod.Name, HookId.ReApplyNPC, e);
                }
            }

            if (!renew) return;
        }

        orig(self, type, time, quiet);

        // AddBuff 返回 void：只有"之前没有、之后有了"才算这次真的加上，否则续时间会把 ApplyNPC 重跑一遍。
        if (existed || mod is null || !mod.HookEnabled(HookId.ApplyNPC)) return;
        if (self.FindBuffIndex(type) < 0) return;

        try
        {
            mod.ApplyNPC(self, time);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.ApplyNPC, e);
        }
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


    /// <summary>
    /// 游戏在 <c>GUIBuffs.Draw</c> 里编译死了 <c>1 &lt;= 类型 &lt;= 400</c>，
    /// 类型 &gt;= 401 会被直接清零（mod buff 从此消失）；护士/对话/网络移除也各有一处 400 上限。
    /// 把这几处 cmp 立即数改成"原版数 + mod 数"。
    /// </summary>
    private void PatchCompiledLimits()
    {
        var total = (uint)(VanillaCounts.Buff + ModContent.GetContent<ModBuff>().Count);
        var vanilla = (uint)VanillaCounts.Buff;

        var patches = new (string Ns, string Type, string Method, int Argc, uint Old, uint New, int Below)[]
        {
            ("", "GUIBuffs", "Draw", 0, vanilla - 2, total - 2, 1),
            ("", "GUIBuffs", "Draw", 0, vanilla, total, 1),
            ("Terraria", "Main", "GetNurseHealCost", 0, vanilla - 1, total - 1, 0),
            ("Terraria", "Main", "NPCChatText_DoNurseHeal", 1, vanilla - 1, total - 1, 0),
            ("", "GUINPCDialogue", "Option1Clicked", 1, vanilla - 1, total - 1, 0),
            ("", "GUINPCDialogue", "SetupButtonText", 6, vanilla - 1, total - 1, 0),
            ("Terraria", "NPC", "RequestBuffRemoval", 1, vanilla - 1, total - 1, 0),
        };

        var ok = 0;
        foreach (var p in patches)
        {
            try
            {
                var name = string.IsNullOrEmpty(p.Ns) ? p.Type : p.Ns + "." + p.Type;
                var changed = Pylon.NaviteAPI.Host.PatchNative.CompareLimit(name, p.Method, p.Argc, p.Old, p.New, p.Below, 0);
                if (changed > 0) ok++;
                else Log.Warn($"[buff] 上限补丁没生效：{name}.{p.Method} {p.Old}→{p.New}（结果 {changed}）");
            }
            catch (Exception e)
            {
                Log.Warn($"[buff] 上限补丁异常：{p.Type}.{p.Method}：{e.Message}");
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
                if (!global.PreDraw(spriteBatch, type, buffSlotOnPlayer, parameters)) draw = false;
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
                draw = mod!.PreDraw(spriteBatch, buffSlotOnPlayer, parameters);
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

    private void DelBuff(On.Terraria.Player.orig_DelBuff orig, Player self, int b)
    {
        var types = self.buffType;
        var times = self.buffTime;
        if (types is null || times is null || b < 0 || b >= types.Length || b >= times.Length)
        {
            orig(self, b);
            return;
        }

        var type = types[b];
        var removedTime = times[b];
        var mod = Of(type);
        var modRemoves = mod is not null && mod.HookEnabled(HookId.CanRemove);

        if (!modRemoves && (mod is null || !mod.HookEnabled(HookId.OnRemove)))
        {
            orig(self, b);
            return;
        }

        bool? verdict = null;
        if (modRemoves)
        {
            try
            {
                var debuffs = Main.debuff;
                var debuff = debuffs is not null && (uint)type < (uint)debuffs.Length && debuffs[type];
                verdict = mod!.CanRemove(self, removedTime, b, debuff);
            }
            catch (Exception e)
            {
                Fail(mod!.Name, HookId.CanRemove, e);
            }
        }

        if (verdict == false) return;

        orig(self, b);

        if (mod is null || !mod.HookEnabled(HookId.OnRemove)) return;
        try
        {
            mod.OnRemove(self, removedTime, b);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.OnRemove, e);
        }
    }

    /// <summary>构建作者参数基线；实际贴图变换在原版绘制窗口中按差值应用。</summary>
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

    // 先完成托管副本再发布，避免游戏观察到只填了一部分的免疫表。
    // int 长度曾误选 IntPtr 包装构造；运行时已补 int 重载，逐项写本身不需要绕过。
    private static bool[] Grow(Il2CppStructArray<bool>? old, int need)
    {
        var grown = new bool[need];
        if (old is not null)
        {
            for (var i = 0; i < old.Length && i < need; i++) grown[i] = old[i];
        }

        return grown;
    }

    /// <summary>同一 (名字, 钩子) 只记一条：buff 效果是每帧路径。</summary>
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
