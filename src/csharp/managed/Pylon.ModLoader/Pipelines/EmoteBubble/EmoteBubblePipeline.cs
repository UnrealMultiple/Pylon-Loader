using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader.Pipelines;
/// <summary>表情气泡管线：mod 编号的泡泡是真的游戏 EmoteBubble；只接管"怎么画"和"帧怎么取"。</summary>
internal sealed class EmoteBubblePipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static EmoteBubblePipeline Instance { get; } = new();

    public string Name => "emotebubble";

    private static IModLog Log => LoaderHost.Current.Log;

    private const int EmoteSheetSlot = 48;

    public bool Install()
    {
        On.Terraria.GameContent.UI.EmoteBubble.NewBubble += NewBubble;
        On.Terraria.GameContent.UI.EmoteBubble.NewLocalOnlyBubble += NewLocalOnlyBubble;
        On.Terraria.GameContent.UI.EmoteBubble.NewBubbleNPC += NewBubbleNpc;
        On.Terraria.GameContent.UI.EmoteBubble.UpdateAll += UpdateAll;
        On.Terraria.GameContent.UI.EmoteBubble.Draw += Draw;
        On.GUIEmotesWindow.EmoteDraw += EmoteDraw;
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

        Log.Info("[emotebubble] 已挂（建泡 / 帧 / 自绘 / 两道下标守卫 / 菜单分类与帧）；同步本版不接");
        return true;
    }

    public void Refresh() => EmoteBubbleTable.Assign();

    internal void Build()
    {
        foreach (var emote in ModContent.GetContent<ModEmoteBubble>())
        {
            if (emote.Type < EmoteBubbleTable.Base) continue;
            emote.SetStaticDefaults();
        }

        if (EmoteBubbleTable.Count > 0) Log.Info($"[emotebubble] 安装：{EmoteBubbleTable.Count} 个表情");
    }

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

        var savedEmote = _menuEmote;
        _menuEmote = own;

        var saved = GUIEmotesWindow._emoteTexture;
        try
        {
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

    private static readonly Dictionary<ModEmoteBubble, ReLogic.Content.Asset<Texture2D>> MenuAssets = [];

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    private static void Report(string key, string what, Exception e)
    {
        if (!Reported.Add(key)) return;
        ContentErrors.Report($"{what}：{Safe.Describe(e)}");
    }

    private static ModEmoteBubble? _menuEmote;

    private static Rectangle MenuFrame(On.GUIEmotesWindow.orig_GetFrame orig, GUIEmotesWindow self, int emoteIndex)
    {
        var vanilla = orig(self, emoteIndex);

        var globals = GlobalEmoteBubbleRegistry.For(HookId.GetFrameInEmoteMenu);
        var own = _menuEmote ?? EmoteBubbleTable.Of(emoteIndex);

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

    private static int NewBubble(On.Terraria.GameContent.UI.EmoteBubble.orig_NewBubble orig, int emoticon,
                                 WorldUIAnchor bubbleAnchor, int time)
    {
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

                if (!OnReceive) EmoteNetPipeline.Instance.Push(emoticon, bubbleAnchor, time);
            }
        }
        catch (Exception e)
        {
            Report("dispatch", "[emotebubble] 建泡后的回调异常", e);
        }

        Spawned(id);
    }

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

    internal static bool OnReceive;

    private static readonly Dictionary<int, IntPtr> Live = [];

    private static void Track(EmoteBubble bubble)
    {
        if (bubble is null || EmoteBubbleTable.Of(bubble.emote) is null) return;
        Live[bubble.ID] = bubble.Pointer;
    }

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

        orig();

        if (Saved.Count == 0) return;

        var byID = EmoteBubble.byID;

        for (var index = 0; index < Saved.Count; index++)
        {
            var (id, pointer, frame, counter) = Saved[index];

            if (byID is null || !byID.TryGetValue(id, out var current) || current is null || current.Pointer != pointer)
            {
                Live.Remove(id);
                continue;
            }

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

            if (!advance || bubble.lifeTime <= 0) continue;
            if (++bubble.frameCounter < 8) continue;
            bubble.frameCounter = 0;
            if (++bubble.frame >= 2) bubble.frame = 0;
        }
    }

    private static void Draw(On.Terraria.GameContent.UI.EmoteBubble.orig_Draw orig, EmoteBubble self, SpriteBatch sb)
    {
        if (self is null) return;

        var emote = EmoteBubbleTable.Of(self.emote);
        if (emote is null && (!AnyGlobalDrawHook() || self.emote < 0))
        {
            orig(self, sb);
            return;
        }

        Track(self);

        try
        {
            DrawBubble(self, sb, emote);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {emote?.Name ?? "GlobalEmoteBubble"} 绘制异常：{Safe.Describe(e)}");
        }
    }

    private static bool AnyGlobalDrawHook() =>
        GlobalEmoteBubbleRegistry.For(HookId.PreDraw).Count > 0 ||
        GlobalEmoteBubbleRegistry.For(HookId.PostDraw).Count > 0 ||
        GlobalEmoteBubbleRegistry.For(HookId.GetFrame).Count > 0;

    private static void DrawBubble(EmoteBubble bubble, SpriteBatch sb, ModEmoteBubble? own)
    {
        var sheet = SheetTexture();
        if (sheet is null) return;

        var rows = EmoteBubble.EMOTE_SHEET_VERTICAL_FRAMES;
        var (at, effect) = Placement(bubble);
        var position = new Vector2(MathF.Floor(at.X), MathF.Floor(at.Y));

        var hidden = bubble.lifeTime < 6 || bubble.lifeTimeStart - bubble.lifeTime < 6;
        var back = Cell(sheet, rows, hidden ? 0 : 1, 0);
        var origin = new Vector2(back.Width / 2f, back.Height);
        if (Main.LocalPlayer.gravDir == -1f)
        {
            origin.Y = 0f;
            effect |= SpriteEffects.FlipVertically;
            position = Main.ReverseGravitySupport(position, 0f);
        }

        Texture2D texture;
        Rectangle frame;
        if (own is not null)
        {
            if (own.Texture?.Value is not { } modTexture) return;
            texture = modTexture;
            int half = texture.Width / 2;
            frame = FrameOf(bubble, own) ?? new Rectangle(half * bubble.frame, 0, half, texture.Height);
        }
        else
        {
            texture = sheet;
            int emote = bubble.emote;
            if ((emote == 87 || emote == 89) && (effect & SpriteEffects.FlipHorizontally) != 0)
            {
                effect &= ~SpriteEffects.FlipHorizontally;
                position.X += 4f;
            }
            frame = FrameOf(bubble, null) ?? Cell(sheet, rows, emote * 2 % 8 + bubble.frame, 1 + emote / 4);
        }

        if (own is not null) own.EmoteBubble = bubble;
        try
        {
            if (PreDrawChain(bubble, sb, own, texture, position, frame, origin, effect))
            {
                Draw(sb, sheet, position, back, origin, effect);
                if (!hidden) Draw(sb, texture, position, frame, origin, effect);
            }
            PostDrawChain(bubble, sb, own, texture, position, frame, origin, effect);
        }
        finally
        {
            if (own is not null) own.EmoteBubble = null!;
        }
    }

    private static Texture2D? SheetTexture()
    {
        try
        {
            var extra = TextureAssets.Extra;
            if (extra is null || extra.Length <= EmoteSheetSlot) return null;
            var sheet = extra[EmoteSheetSlot]?.Value;
            if (sheet is null || sheet.Width <= 0 || sheet.Height <= 0 || sheet.Width % 8 != 0) return null;
            return sheet;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] 表情图集取不到：{Safe.Describe(e)}");
            return null;
        }
    }

    private static Rectangle Cell(Texture2D sheet, int rows, int col, int row)
    {
        var width = Math.Max(sheet.Width / 8, 1);
        var height = Math.Max(sheet.Height / Math.Max(rows, 1), 1);
        return new Rectangle(col * width, row * height, width, height);
    }

    private static Rectangle? FrameOf(EmoteBubble bubble, ModEmoteBubble? own)
    {
        if (own is not null)
        {
            try
            {
                return own.GetFrame();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {own.Name}.GetFrame 异常：{Safe.Describe(e)}");
                return null;
            }
        }

        Rectangle? result = null;
        var globals = GlobalEmoteBubbleRegistry.For(HookId.GetFrame);
        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                if (globals[g].GetFrame(bubble) is { } frame) result = frame;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {globals[g].Name}.GetFrame 异常：{Safe.Describe(e)}");
            }
        }
        return result;
    }

    private static bool PreDrawChain(EmoteBubble bubble, SpriteBatch sb, ModEmoteBubble? own, Texture2D texture,
        Vector2 position, Rectangle frame, Vector2 origin, SpriteEffects effect)
    {
        var globals = GlobalEmoteBubbleRegistry.For(HookId.PreDraw);
        var result = true;
        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                result &= globals[g].PreDraw(bubble, sb, texture, position, frame, origin, effect);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {globals[g].Name}.PreDraw 异常：{Safe.Describe(e)}");
            }
        }
        if (!result || own is null) return result;

        try
        {
            return own.PreDraw(sb, texture, position, frame, origin, effect);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {own.Name}.PreDraw 异常：{Safe.Describe(e)}");
            return true;
        }
    }

    private static void PostDrawChain(EmoteBubble bubble, SpriteBatch sb, ModEmoteBubble? own, Texture2D texture,
        Vector2 position, Rectangle frame, Vector2 origin, SpriteEffects effect)
    {
        var globals = GlobalEmoteBubbleRegistry.For(HookId.PostDraw);
        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                globals[g].PostDraw(bubble, sb, texture, position, frame, origin, effect);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {globals[g].Name}.PostDraw 异常：{Safe.Describe(e)}");
            }
        }

        if (own is null) return;
        try
        {
            own.PostDraw(sb, texture, position, frame, origin, effect);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {own.Name}.PostDraw 异常：{Safe.Describe(e)}");
        }
    }

    private static void Draw(SpriteBatch sb, Texture2D texture, Vector2 position, Rectangle frame, Vector2 origin,
                             SpriteEffects effect)
    {
        sb.Draw(texture, position, new Il2CppSystem.Nullable<Rectangle>(frame), Color.White, 0f, origin, 1f, effect, 0f);
    }

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

        if (Main.LocalPlayer.gravDir == -1f)
        {
            effect |= SpriteEffects.FlipVertically;
            position = Main.ReverseGravitySupport(position, 0f);
        }

        return (new Vector2(MathF.Floor(position.X), MathF.Floor(position.Y)), effect);
    }

    private static Vector2 Estimated(EmoteBubble self)
    {
        var anchor = self.anchor;
        if (anchor is null || anchor.Pointer == 0) return Vector2.Zero;

        var entity = anchor.entity;
        if (entity is not null && entity.Pointer != 0)
            return entity.Center - new Vector2(0f, entity.height * 0.5f + 24f);

        return anchor.pos - new Vector2(0f, anchor.size.Y * 0.5f + 24f);
    }

    private static NPC? AnchoredNpc(EmoteBubble bubble)
    {
        try
        {
            var anchor = bubble.anchor;
            if (anchor is null || anchor.Pointer == 0) return null;

            var entity = anchor.entity;
            if (entity is null || entity.Pointer == 0) return null;

            return entity.TryCast<NPC>();
        }
        catch
        {
            return null;
        }
    }

    private static void PickNpcEmote(On.Terraria.GameContent.UI.EmoteBubble.orig_PickNPCEmote orig,
                                     EmoteBubble self, WorldUIAnchor other)
    {
        orig(self, other);

        var npc = AnchoredNpc(self);
        if (npc is null) return;

        var emotes = new List<int> { self.emote };

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

        int? picked = null;
        var own = NpcPipeline.Instance.InstanceOf(npc);
        if (own is { } mod && mod.HookEnabled(HookId.PickEmote))
        {
            try
            {
                picked = mod.PickEmote(closest, emotes, other);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {mod.Name}.PickEmote 异常：{Safe.Describe(e)}");
            }
        }

        var globals = GlobalNpcRegistry.For(npc, HookId.PickEmote);
        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                if (globals[g].PickEmote(npc, closest, emotes, other) is int emote) picked = emote;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {globals[g].Name}.PickEmote 异常：{Safe.Describe(e)}");
            }
        }

        if (picked.HasValue) self.emote = picked.Value;
        else if (emotes.Count > 1) self.emote = emotes[Main.rand.Next(emotes.Count)];
    }

    private static Vector2 GetPosition(On.Terraria.GameContent.UI.EmoteBubble.orig_GetPosition orig,
                                       EmoteBubble self, out SpriteEffects effect)
    {
        var position = orig(self, out effect);

        var npc = AnchoredNpc(self);
        if (npc is null) return position;

        var own = NpcPipeline.Instance.InstanceOf(npc);
        if (own is { } mod && mod.HookEnabled(HookId.EmoteBubblePosition))
        {
            try
            {
                mod.EmoteBubblePosition(ref position, ref effect);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {mod.Name}.EmoteBubblePosition 异常：{Safe.Describe(e)}");
            }
        }

        var globals = GlobalNpcRegistry.For(npc, HookId.EmoteBubblePosition);
        for (var g = 0; g < globals.Count; g++)
        {
            try
            {
                globals[g].EmoteBubblePosition(npc, ref position, ref effect);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {globals[g].Name}.EmoteBubblePosition 异常：{Safe.Describe(e)}");
            }
        }

        return position;
    }

    private static bool ShouldFilter(On.Terraria.GameContent.UI.EmoteBubble.orig_ShouldFilterPlayerEmote orig,
                                     Player player, int emoticon) =>
        EmoteBubbleTable.Of(emoticon) is null && orig(player, emoticon);
}
