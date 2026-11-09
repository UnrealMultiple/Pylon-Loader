using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader;

/// <summary>
/// 表情气泡管线：mod 编号的泡泡是**真的**游戏 <see cref="EmoteBubble"/>（寿命、登记、清理都归游戏），
/// 我们只接管"怎么画"和"帧怎么取"，再把两个会按编号查表的入口挡掉。
/// </summary>
internal sealed class EmoteBubblePipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static EmoteBubblePipeline Instance { get; } = new();

    public string Name => "emotebubble";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>原版表情图集在 TextureAssets.Extra 里的槽位。</summary>
    private const int EmoteSheetSlot = 48;

    public bool Install()
    {
        On.Terraria.GameContent.UI.EmoteBubble.NewBubble += NewBubble;
        On.Terraria.GameContent.UI.EmoteBubble.NewLocalOnlyBubble += NewLocalOnlyBubble;
        On.Terraria.GameContent.UI.EmoteBubble.NewBubbleNPC += NewBubbleNpc;
        On.Terraria.GameContent.UI.EmoteBubble.UpdateAll += UpdateAll;
        On.Terraria.GameContent.UI.EmoteBubble.Draw += Draw;
        On.GUIEmotesWindow.EmoteDraw += EmoteDraw;
        On.Terraria.GameContent.UI.EmoteBubble.CheckForNPCsToReactToEmoteBubble += CheckNpcReactions;
        On.Terraria.GameContent.UI.EmoteBubble.PickNPCEmote += PickNpcEmote;
        On.Terraria.GameContent.UI.EmoteBubble.GetPosition += GetPosition;
        On.Terraria.GameContent.UI.EmoteBubble.ShouldFilterPlayerEmote += ShouldFilter;
        On.GUIEmotesWindow.GetEmotesGeneral += General;
        On.GUIEmotesWindow.GetEmotesRPS += Rps;
        On.GUIEmotesWindow.GetEmotesItems += Items;
        On.GUIEmotesWindow.GetEmotesBiomesAndEvents += BiomesAndEvents;
        On.GUIEmotesWindow.GetEmotesTownNPCs += TownNpcs;
        On.GUIEmotesWindow.GetEmotesCritters += Critters;
        On.GUIEmotesWindow.GetEmotesBosses += Bosses;
        On.GUIEmotesWindow.GetFrame += MenuFrame;
        On.GUIEmotesWindow.EmoteOver += Hover;

        Log.Info("[emotebubble] 已挂（建泡 / 帧 / 自绘 / 两道下标守卫 / 菜单分类与帧）；同步本版不接");
        return true;
    }

    public void Refresh() => EmoteBubbleTable.Assign();

    /// <summary>安装期一次：模板的静态默认值与共享数值。</summary>
    internal void Build()
    {
        foreach (var emote in ModContent.GetContent<ModEmoteBubble>())
        {
            if (emote.Type < EmoteBubbleTable.Base) continue;
            emote.SetStaticDefaults();
        }

        if (EmoteBubbleTable.Count > 0) Log.Info($"[emotebubble] 安装：{EmoteBubbleTable.Count} 个表情");
    }

    // ===================== 菜单 =====================

    private static void General(On.GUIEmotesWindow.orig_GetEmotesGeneral orig, GUIEmotesWindow self,
                                Il2CppSystem.Collections.Generic.List<int> emotes)
    {
        orig(self, emotes);
        EmoteBubbleTable.AppendCategory(0, emotes);
    }

    private static void Rps(On.GUIEmotesWindow.orig_GetEmotesRPS orig, GUIEmotesWindow self,
                            Il2CppSystem.Collections.Generic.List<int> emotes)
    {
        orig(self, emotes);
        EmoteBubbleTable.AppendCategory(1, emotes);
    }

    private static void Items(On.GUIEmotesWindow.orig_GetEmotesItems orig, GUIEmotesWindow self,
                              Il2CppSystem.Collections.Generic.List<int> emotes)
    {
        orig(self, emotes);
        EmoteBubbleTable.AppendCategory(2, emotes);
    }

    private static void BiomesAndEvents(On.GUIEmotesWindow.orig_GetEmotesBiomesAndEvents orig, GUIEmotesWindow self,
                                        Il2CppSystem.Collections.Generic.List<int> emotes)
    {
        orig(self, emotes);
        EmoteBubbleTable.AppendCategory(3, emotes);
    }

    private static void TownNpcs(On.GUIEmotesWindow.orig_GetEmotesTownNPCs orig, GUIEmotesWindow self,
                                 Il2CppSystem.Collections.Generic.List<int> emotes)
    {
        orig(self, emotes);
        EmoteBubbleTable.AppendCategory(4, emotes);
    }

    private static void Critters(On.GUIEmotesWindow.orig_GetEmotesCritters orig, GUIEmotesWindow self,
                                 Il2CppSystem.Collections.Generic.List<int> emotes)
    {
        orig(self, emotes);
        EmoteBubbleTable.AppendCategory(5, emotes);
    }

    private static void Bosses(On.GUIEmotesWindow.orig_GetEmotesBosses orig, GUIEmotesWindow self,
                               Il2CppSystem.Collections.Generic.List<int> emotes)
    {
        orig(self, emotes);
        EmoteBubbleTable.AppendCategory(6, emotes);
    }

    /// <summary>
    /// 悬停提示：原版对每个表情有自己的名字表，mod 编号在里面查不到，所以整格悬停时什么都不显示。
    /// 这里给 mod 表情写 <c>Main.hoverItemName</c>（游戏在光标附近画它）；原版编号照旧交给 orig。
    /// </summary>
    private static void Hover(On.GUIEmotesWindow.orig_EmoteOver orig, GUIEmotesWindow self, int index)
    {
        // 必须让游戏跑完：它在这里记"当前悬停的是哪一格"，点击逻辑依赖这个状态；
        // 之前 mod 表情直接 return，点下去用的还是上一次（或 -1）的格子。
        orig(self, index);

        try
        {
            EmoteBubbleTable.EnsureLocalized();
            var entries = self?._emoteEntries;
            if (entries is null || (uint)index >= (uint)entries.Count) return;

            if (EmoteBubbleTable.Of(entries[index]) is not { } emote) return;

            Main.hoverItemName = emote.DisplayName ?? emote.Name;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[emotebubble] 悬停名字异常：" + Safe.Describe(e));
        }
    }

    /// <summary>
    /// 菜单图标：游戏永远从表情图集 <c>TextureAssets.Extra[48]</c> 上按帧取块，mod 表情的贴图不在里面。
    /// 这里在调用游戏那次绘制之前把图集槽临时换成作者贴图，位置/缩放/裁剪仍由游戏算，画完换回去。
    /// </summary>
    private static void EmoteDraw(On.GUIEmotesWindow.orig_EmoteDraw orig, GUIEmotesWindow self,
                                  ItemGrid_Layout gridLayout, int index, Vector2 position, float scale)
    {
        ModEmoteBubble? own = null;

        try
        {
            var entries = self?._emoteEntries;
            if (entries is not null && (uint)index < (uint)entries.Count)
                own = EmoteBubbleTable.Of(entries[index]);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[emotebubble] 菜单取表情编号异常：" + Safe.Describe(e));
        }

        if (own?.Texture?.Value is not { } texture)
        {
            orig(self, gridLayout, index, position, scale);
            return;
        }

        // 菜单图标画的是 GUIEmotesWindow 自己那张静态图集；临时换成作者贴图，画完换回去。
        // GetFrame 在 orig 内部被调用，靠 _menuEmote 知道该按作者的贴图取块。
        var savedEmote = _menuEmote;
        _menuEmote = own;

        var saved = GUIEmotesWindow._emoteTexture;
        try
        {
            // 同一个表情只包一次 Asset：每帧新建同名 Asset 会不停往资源表里塞新条目。
            if (!MenuAssets.TryGetValue(own, out var asset))
            {
                asset = new ReLogic.Content.Asset<Texture2D>("mod-emote-" + own.FullKey);
                asset.SubmitLoadedContent(texture, null);
                MenuAssets[own] = asset;
            }

            GUIEmotesWindow._emoteTexture = asset;
            orig(self, gridLayout, index, position, scale);
        }
        catch (Exception e)
        {
            Report(own.FullKey + ".menu-icon", $"[emotebubble] {own.Name} 的菜单图标异常", e);
        }
        finally
        {
            GUIEmotesWindow._emoteTexture = saved;
            _menuEmote = savedEmote;
        }
    }

    /// <summary>菜单图标用的临时 Asset（每表情一个，复用）。</summary>
    private static readonly Dictionary<ModEmoteBubble, ReLogic.Content.Asset<Texture2D>> MenuAssets = [];

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    private static void Report(string key, string what, Exception e)
    {
        if (!Reported.Add(key)) return;
        ContentErrors.Report($"{what}：{Safe.Describe(e)}");
    }

    /// <summary>当前这一次 EmoteDraw 画的是哪个 mod 表情（GetFrame 在它内部被调用，靠这个认人）。</summary>
    private static ModEmoteBubble? _menuEmote;

    /// <summary>菜单里取哪一块：作者给了就用作者的，否则按"贴图的一半、两帧交替"取。</summary>
    private static Rectangle MenuFrame(On.GUIEmotesWindow.orig_GetFrame orig, GUIEmotesWindow self, int emoteIndex)
    {
        var vanilla = orig(self, emoteIndex);

        var globals = GlobalEmoteBubbleRegistry.For(HookId.GetFrameInEmoteMenu);
        var own = _menuEmote ?? EmoteBubbleTable.Of(emoteIndex);

        // 动画相位沿用游戏自己那一帧的宽度推出来（它已经算好了两帧循环）。
        var frame = vanilla.Width > 0 ? vanilla.X / vanilla.Width % 2 : 0;
        var counter = self is null ? 0 : self._frameCounter;

        if (own is null)
        {
            for (var g = 0; g < globals.Count; g++)
            {
                try
                {
                    if (globals[g].GetFrameInEmoteMenu(emoteIndex, frame, counter) is { } globalFrame) return globalFrame;
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[emotebubble] {globals[g].Name}.GetFrameInEmoteMenu 异常：{Safe.Describe(e)}");
                }
            }

            return vanilla;
        }

        try
        {
            if (own.GetFrameInEmoteMenu(frame, counter) is { } custom) return custom;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {own.Name}.GetFrameInEmoteMenu 异常：{Safe.Describe(e)}");
        }

        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                if (globals[g].GetFrameInEmoteMenu(own.Type, frame, counter) is { } globalFrame) return globalFrame;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {globals[g].Name}.GetFrameInEmoteMenu 异常：{Safe.Describe(e)}");
            }
        }

        var texture = own.Texture?.Value;
        var width = texture is null ? 34 : Math.Max(texture.Width / 2, 1);
        var height = texture is null ? 28 : Math.Max(texture.Height, 1);

        return new Rectangle(frame * width, 0, width, height);
    }

    // ===================== 建泡 =====================

    private static int NewBubble(On.Terraria.GameContent.UI.EmoteBubble.orig_NewBubble orig, int emoticon,
                                 WorldUIAnchor bubbleAnchor, int time)
    {
        // 泡泡本体一律由游戏建：编号、登记进 byID、寿命都归它；自己 new 出来的没有 ID，也不会进表。
        int id;
        try
        {
            id = orig(emoticon, bubbleAnchor, time);
        }
        catch (Exception e)
        {
            Report("newbubble.orig", $"[emotebubble] 游戏建泡异常（编号 {emoticon}）", e);
            return -1;
        }

        Dispatch(id, emoticon, bubbleAnchor, time);
        return id;
    }

    /// <summary>客户端只在本机显示的表情（菜单点击走这条）：编号同样是 mod 的可能。</summary>
    private static int NewLocalOnlyBubble(On.Terraria.GameContent.UI.EmoteBubble.orig_NewLocalOnlyBubble orig,
                                          int emoticon, WorldUIAnchor bubbleAnchor, int time)
    {
        int id;
        try
        {
            id = orig(emoticon, bubbleAnchor, time);
        }
        catch (Exception e)
        {
            Report("localonly.orig", $"[emotebubble] 本机建泡异常（编号 {emoticon}）", e);
            return -1;
        }

        Dispatch(id, emoticon, bubbleAnchor, time);
        return id;
    }

    /// <summary>NPC 自己发起的表情（编号由游戏/NPC 挑，也可能是 mod 编号）。</summary>
    private static int NewBubbleNpc(On.Terraria.GameContent.UI.EmoteBubble.orig_NewBubbleNPC orig,
                                    WorldUIAnchor bubbleAnchor, int time, WorldUIAnchor other)
    {
        int id;
        try
        {
            id = orig(bubbleAnchor, time, other);
        }
        catch (Exception e)
        {
            Report("npcbubble.orig", "[emotebubble] NPC 建泡异常", e);
            return -1;
        }

        var emoticon = 0;
        try
        {
            var byID = EmoteBubble.byID;
            if (byID is not null && byID.TryGetValue(id, out var bubble) && bubble is not null) emoticon = bubble.emote;
        }
        catch (Exception e)
        {
            Report("npcbubble.read", "[emotebubble] 读 NPC 泡泡编号异常", e);
        }

        Dispatch(id, emoticon, bubbleAnchor, time);
        return id;
    }

    /// <summary>泡泡建好之后：mod 表情派 <c>OnSpawn</c>（并按需转发），全局钩子对游戏表情也派。</summary>
    private static void Dispatch(int id, int emoticon, WorldUIAnchor bubbleAnchor, int time)
    {
        try
        {
            if (EmoteBubbleTable.Of(emoticon) is { } emote)
            {
                var byID = EmoteBubble.byID;
                if (byID is not null && byID.TryGetValue(id, out var bubble) && bubble is not null)
                {
                    emote.EmoteBubble = bubble;
                    try
                    {
                        emote.OnSpawn();
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Report($"[emotebubble] {emote.Name}.OnSpawn 异常：{Safe.Describe(e)}");
                    }
                    finally
                    {
                        emote.EmoteBubble = null!;
                    }
                }

                // 多人：mod 表情的编号对端不认识，随包带上"锚是谁"（游戏自带锚的序列化），收到的人用同一个锚重建。
                if (!OnReceive) EmoteNetPipeline.Instance.Push(emoticon, bubbleAnchor, time);
            }
        }
        catch (Exception e)
        {
            Report("dispatch", "[emotebubble] 建泡后的回调异常", e);
        }

        Spawned(id);
    }

    /// <summary>
    /// 气泡建好之后把全局钩子派一遍：用游戏自己的 <c>EmoteBubble.byID</c> 按 ID 取回实例，
    /// 这样 mod 表情与游戏表情走同一条路（两边的建泡代码都登记过 ID）。
    /// </summary>
    private static void Spawned(int id)
    {
        var globals = GlobalEmoteBubbleRegistry.For(HookId.OnSpawn);
        if (globals.Count == 0) return;

        var byID = EmoteBubble.byID;
        if (byID is null || !byID.TryGetValue(id, out var bubble) || bubble is null) return;

        for (var index = 0; index < globals.Count; index++)
        {
            var global = globals[index];
            try
            {
                global.OnSpawn(bubble);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {global.Name}.OnSpawn 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>收到别的端的 mod 表情：用同一个锚重建一次（重建期间不再转发）。</summary>
    internal static void ApplyRemote(int emoticon, WorldUIAnchor anchor, int time)
    {
        OnReceive = true;
        try
        {
            EmoteBubble.NewBubble(emoticon, anchor, time);
        }
        finally
        {
            OnReceive = false;
        }
    }

    /// <summary>true = 正在重建对端发来的泡泡（这时不再往外发，防回声）。</summary>
    internal static bool OnReceive;

    // ===================== 每帧 =====================

    /// <summary>
    /// 正在跟的 mod 泡泡：ID → 结构体数据指针。
    /// 游戏的泡泡按值存在 <c>byID</c> 里，托管侧拿到的 <see cref="EmoteBubble"/> 只是快照；
    /// 所以每轮都按指针重新包一份再写，否则帧号只写进了副本，动画不会动。
    /// </summary>
    private static readonly Dictionary<int, IntPtr> Live = [];

    /// <summary>按指针记下这个泡泡（建泡与绘制时都会过一遍，托管侧不知道的泡泡也补上）。</summary>
    private static void Track(EmoteBubble bubble)
    {
        if (bubble is null || EmoteBubbleTable.Of(bubble.emote) is null) return;
        Live[bubble.ID] = bubble.Pointer;
    }

    /// <summary>这一轮 UpdateAll 之前记下的帧状态：游戏的帧推进不认识 mod 编号，跑完要还回去自己推。</summary>
    private static readonly List<(int Id, IntPtr Bubble, int Frame, int Counter)> Saved = [];

    private static void UpdateAll(On.Terraria.GameContent.UI.EmoteBubble.orig_UpdateAll orig)
    {
        EmoteBubbleTable.EnsureLocalized();

        Saved.Clear();
        if (Live.Count > 0)
        {
            foreach (var pair in Live)
            {
                var bubble = new EmoteBubble(pair.Value);
                Saved.Add((pair.Key, pair.Value, bubble.frame, bubble.frameCounter));
            }
        }

        // 先让游戏推寿命、清理死掉的泡泡。
        orig();

        if (Saved.Count == 0) return;

        var byID = EmoteBubble.byID;

        for (var index = 0; index < Saved.Count; index++)
        {
            var (id, pointer, frame, counter) = Saved[index];

            // 游戏可能已经把这个泡泡删了（寿命到）或换成另一个对象。
            if (byID is null || !byID.TryGetValue(id, out var current) || current is null || current.Pointer != pointer)
            {
                Live.Remove(id);
                continue;
            }

            // 按指针重新包：写的是游戏表里那一个，不是快照。
            var bubble = new EmoteBubble(pointer);
            bubble.frame = frame;
            bubble.frameCounter = counter;

            bool advance = true;
            var globals = GlobalEmoteBubbleRegistry.For(HookId.UpdateFrame);
            for (var g = 0; g < globals.Count; g++)
            {
                try
                {
                    if (!globals[g].UpdateFrame(bubble)) advance = false;
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[emotebubble] {globals[g].Name}.UpdateFrame 异常：{Safe.Describe(e)}");
                }
            }

            if (EmoteBubbleTable.Of(bubble.emote) is { } emote)
            {
                emote.EmoteBubble = bubble;
                try
                {
                    if (!emote.UpdateFrame()) advance = false;
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[emotebubble] {emote.Name}.UpdateFrame 异常：{Safe.Describe(e)}");
                }
                finally
                {
                    emote.EmoteBubble = null!;
                }
            }

            // 和游戏自己的步子一致：8 帧翻一次，两个帧来回。
            if (!advance || bubble.lifeTime <= 0) continue;
            if (++bubble.frameCounter < 8) continue;
            bubble.frameCounter = 0;
            if (++bubble.frame >= 2) bubble.frame = 0;
        }
    }

    // ===================== 绘制 =====================

    private static void Draw(On.Terraria.GameContent.UI.EmoteBubble.orig_Draw orig, EmoteBubble self, SpriteBatch sb)
    {
        if (self is null || EmoteBubbleTable.Of(self.emote) is not { } emote)
        {
            orig(self!, sb);
            return;
        }

        Track(self);

        if (emote.Texture?.Value is not { } texture) return;

        var (position, effect) = Placement(self);
        var frame = Frame(emote, self);
        var origin = new Vector2(frame.Width * 0.5f, frame.Height);

        // 泡泡的淡入淡出：原版在头尾各 6 帧只画白壳、不画内容，这里照做——否则作者贴图会在壳之外"提前出现"。
        var fading = self.lifeTime < 6 || self.lifeTimeStart - self.lifeTime < 6;
        var shell = Shell(self, origin, effect, out var back);

        var globalsPre = GlobalEmoteBubbleRegistry.For(HookId.PreDraw);
        var globalsPost = GlobalEmoteBubbleRegistry.For(HookId.PostDraw);

        emote.EmoteBubble = self;
        try
        {
            var show = true;
            for (var g = 0; g < globalsPre.Count; g++)
            {
                try
                {
                    if (!globalsPre[g].PreDraw(self, sb, texture, position, frame, origin, effect)) show = false;
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[emotebubble] {globalsPre[g].Name}.PreDraw 异常：{Safe.Describe(e)}");
                }
            }

            if (show && emote.PreDraw(sb, texture, position, frame, origin, effect))
            {
                if (shell is not null) Draw(sb, shell, position, back, origin, effect);
                if (!fading) Draw(sb, texture, position, frame, origin, effect);

                emote.PostDraw(sb, texture, position, frame, origin, effect);
            }

            for (var g = 0; g < globalsPost.Count; g++)
            {
                try
                {
                    globalsPost[g].PostDraw(self, sb, texture, position, frame, origin, effect);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[emotebubble] {globalsPost[g].Name}.PostDraw 异常：{Safe.Describe(e)}");
                }
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {emote.Name} 绘制异常：{Safe.Describe(e)}");
        }
        finally
        {
            emote.EmoteBubble = null!;
        }
    }

    /// <summary>帧：作者给了就用作者的，其次全局的，最后左右两半按游戏的 frame 取。</summary>
    private static Rectangle Frame(ModEmoteBubble emote, EmoteBubble self)
    {
        if (emote.GetFrame() is { } own) return own;

        var globals = GlobalEmoteBubbleRegistry.For(HookId.GetFrame);
        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                if (globals[g].GetFrame(self) is { } frame) return frame;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {globals[g].Name}.GetFrame 异常：{Safe.Describe(e)}");
            }
        }

        return self.frame % 2 == 0 ? new Rectangle(0, 0, 34, 28) : new Rectangle(34, 0, 34, 28);
    }

    /// <summary>一次 <c>sb.Draw</c>，参数全部显式给（interop 的重载要 Nullable 包装）。</summary>
    private static void Draw(SpriteBatch sb, Texture2D texture, Vector2 position, Rectangle frame, Vector2 origin,
                             SpriteEffects effect)
    {
        // 9 参重载：position + sourceRect + color + rotation + origin + scale + effects + depth。
        // 10 参那个把第 3 个参数当 destinationRectangle，同时给 position 会直接抛
        // "Expected drawRectangle or position, but received neither or both"。
        sb.Draw(texture, position, new Il2CppSystem.Nullable<Rectangle>(frame), Color.White, 0f, origin, 1f, effect, 0f);
    }

    /// <summary>
    /// 位置的唯一来源是游戏自己的 <c>GetPosition</c>（它算锚点偏移、重力方向与水平翻转）；
    /// 私有方法照调不误——interop 代理把它生成成公开的。拿不到时才退回自己按锚点估的那份。
    /// </summary>
    private static (Vector2 Position, SpriteEffects Effect) Placement(EmoteBubble self)
    {
        SpriteEffects effect = SpriteEffects.None;
        Vector2 position;

        try
        {
            position = self.GetPosition(out effect);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] GetPosition 异常，改用锚点估算：{Safe.Describe(e)}");
            position = Estimated(self);
        }

        // 重力翻转的屏幕上，泡泡要把自己翻过来并换到反向重力那一侧的坐标。
        if (Main.LocalPlayer.gravDir == -1f)
        {
            effect |= SpriteEffects.FlipVertically;
            position = Main.ReverseGravitySupport(position, 0f);
        }

        return (new Vector2(MathF.Floor(position.X), MathF.Floor(position.Y)), effect);
    }

    /// <summary>兜底：锚在实体上就取实体中心上方，锚在坐标上就用锚点坐标。</summary>
    private static Vector2 Estimated(EmoteBubble self)
    {
        var anchor = self.anchor;
        if (anchor is null || anchor.Pointer == 0) return Vector2.Zero;

        // 空锚的 entity 是 Pointer = 0 的代理：碰它的字段会崩在原生里。
        var entity = anchor.entity;
        if (entity is not null && entity.Pointer != 0)
            return entity.Center - new Vector2(0f, entity.height * 0.5f + 24f);

        return anchor.pos - new Vector2(0f, anchor.size.Y * 0.5f + 24f);
    }

    /// <summary>
    /// 白泡泡外壳：原版那张表情图集里的第一行（第 0 列是淡入淡出的空壳，第 1 列是实心壳）。
    /// 图集不在了就返回 null，只画作者内容。
    /// </summary>
    private static Texture2D? Shell(EmoteBubble self, Vector2 origin, SpriteEffects effect, out Rectangle back)
    {
        back = default;

        try
        {
            var extra = TextureAssets.Extra;
            if (extra is null || extra.Length <= EmoteSheetSlot) return null;

            var sheet = extra[EmoteSheetSlot]?.Value;
            if (sheet is null || sheet.Width <= 0 || sheet.Height <= 0) return null;

            // 槽位号不是编译期常量，所以先按"8 列 × 行数"验一下这张图是不是真图集：
            // 槽错了就只是没有壳，不能让错的图被当成壳画出去。
            if (sheet.Width % 8 != 0) return null;

            var rows = Math.Max(EmoteBubble.EMOTE_SHEET_VERTICAL_FRAMES, 1);
            if (rows <= 1 || sheet.Height % rows != 0) return null;

            var cellWidth = Math.Max(sheet.Width / 8, 1);
            var cellHeight = Math.Max(sheet.Height / rows, 1);
            var fading = self.lifeTime < 6 || self.lifeTimeStart - self.lifeTime < 6;
            back = new Rectangle((fading ? 0 : 1) * cellWidth, 0, cellWidth, cellHeight);
            return sheet;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] 泡泡外壳取不到：{Safe.Describe(e)}");
            return null;
        }
    }

    // ===================== NPC 表情 =====================

    /// <summary>泡泡锚在实体上且那实体是 NPC 时给出实例；其它（玩家/位置锚）返回 null。</summary>
    private static NPC? AnchoredNpc(EmoteBubble bubble)
    {
        try
        {
            var anchor = bubble.anchor;
            if (anchor is null || anchor.Pointer == 0) return null;

            // 空锚的 entity 是 Pointer = 0 的代理（不是 C# null）：直接 TryCast 会带着空指针
            // 进原生做类型判定，崩在 il2cpp 里、托管 catch 兜不住，所以先查指针。
            var entity = anchor.entity;
            if (entity is null || entity.Pointer == 0) return null;

            // IL2CPP 接口在托管侧是代理类，不能 as，要用 TryCast 落到目标代理类。
            return entity.TryCast<NPC>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 原版挑完表情后问作者：返回值直接换掉泡泡的表情，或从 emoteList 追加的候选里随机挑。
    /// 在 orig 之后改 <c>self.emote</c>，游戏的寿命、登记、绘制都不用管。
    /// </summary>
    private static void PickNpcEmote(On.Terraria.GameContent.UI.EmoteBubble.orig_PickNPCEmote orig,
                                     EmoteBubble self, WorldUIAnchor other)
    {
        orig(self, other);

        var npc = AnchoredNpc(self);
        if (npc is null) return;

        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is null || !mod.HookEnabled(HookId.PickEmote)) return;

        Player closest;
        try
        {
            closest = Main.player[Player.FindClosest(npc.position, npc.width, npc.height)];
        }
        catch (Exception e)
        {
            ContentErrors.Report("[emotebubble] 找最近玩家失败：" + Safe.Describe(e));
            return;
        }

        var emotes = new List<int> { self.emote };
        int? picked;
        try
        {
            picked = mod.PickEmote(closest, emotes, other);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {mod.Name}.PickEmote 异常：{Safe.Describe(e)}");
            return;
        }

        if (picked.HasValue) self.emote = picked.Value;
        else if (emotes.Count > 1) self.emote = emotes[Main.rand.Next(emotes.Count)];
    }

    /// <summary>游戏算完位置后让作者挪一挪；只对锚在 mod NPC 上的泡泡派发。</summary>
    private static Vector2 GetPosition(On.Terraria.GameContent.UI.EmoteBubble.orig_GetPosition orig,
                                       EmoteBubble self, ref SpriteEffects effect)
    {
        // interop 代理把 out 参数生成成 ref：原值先拿，作者层再覆写。
        var position = orig(self, ref effect);

        var npc = AnchoredNpc(self);
        if (npc is null) return position;

        var mod = NpcPipeline.Instance.InstanceOf(npc);
        if (mod is null || !mod.HookEnabled(HookId.EmoteBubblePosition)) return position;

        try
        {
            mod.EmoteBubblePosition(ref position, ref effect);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {mod.Name}.EmoteBubblePosition 异常：{Safe.Describe(e)}");
        }

        return position;
    }

    // ===================== 两道下标守卫 =====================

    private static void CheckNpcReactions(On.Terraria.GameContent.UI.EmoteBubble.orig_CheckForNPCsToReactToEmoteBubble orig,
                                          int emoteID, Player player)
    {
        if (EmoteBubbleTable.Of(emoteID) is not { } emote)
        {
            orig(emoteID, player);
            return;
        }

        ReactNpcs(emote, player);
    }

    /// <summary>
    /// mod 表情的 NPC 反应：作者给一个原版表情号，最近一个城镇 NPC 就用它回应。
    /// 原版那套"哪个 NPC 用哪号表情"的表只认原版编号，所以这里自己挑 NPC、自己建泡。
    /// </summary>
    private static void ReactNpcs(ModEmoteBubble emote, Player player)
    {
        int reaction;
        try
        {
            reaction = emote.NPCReaction;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {emote.Name}.NPCReaction 异常：{Safe.Describe(e)}");
            return;
        }

        if (reaction < 0) return;

        var npc = NearestTownNpc(player);
        if (npc is null) return;

        try
        {
            EmoteBubble.NewBubble(reaction, new WorldUIAnchor(npc), 180);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[emotebubble] NPC 反应建泡失败：" + Safe.Describe(e));
        }
    }

    /// <summary>最近的、活着的城镇 NPC（40 格内）；没有就 null。</summary>
    private static NPC? NearestTownNpc(Player player)
    {
        const float range = 40f * 16f;

        NPC? best = null;
        var bestDistance = range * range;
        var npcs = Main.npc;

        for (var index = 0; index < npcs.Length; index++)
        {
            var npc = npcs[index];
            if (npc is null || !npc.active || !npc.townNPC) continue;

            var delta = npc.Center - player.Center;
            var distance = delta.X * delta.X + delta.Y * delta.Y;
            if (distance >= bestDistance) continue;

            best = npc;
            bestDistance = distance;
        }

        return best;
    }

    private static bool ShouldFilter(On.Terraria.GameContent.UI.EmoteBubble.orig_ShouldFilterPlayerEmote orig,
                                     Player player, int emoticon) =>
        EmoteBubbleTable.Of(emoticon) is null && orig(player, emoticon);
}
