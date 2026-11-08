using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// NPC.SetDefaults 接管：原版先跑（它负责写 scale / color / alpha 这类绘制乘数的出厂值），
/// 之后叠作者的默认值，再把原版"因为 type 超界而提前 return"漏掉的收尾补上。
/// 同时缓存"这个类型多宽、多少帧"——补宽高与 FindFrame 都要用。
/// </summary>
internal sealed class NpcDefaults(NpcBinding binding)
{
    private readonly Dictionary<int, FrameSize> _sizes = [];

    public void Install() => On.Terraria.NPC.SetDefaults += SetDefaults;

    private void SetDefaults(On.Terraria.NPC.orig_SetDefaults orig, NPC self, int type, NPCSpawnParams spawnParams)
    {
        orig(self, type, spawnParams);

        if (self is null || type < 0) return;

        if (ModContent.Find(ContentKind.Npc, type) is ModNpc) Custom(self, type);
        DispatchGlobals(self);
    }

    private void Custom(NPC npc, int type)
    {
        try
        {
            // 原版那条 type > NPCID.Count - 1 的比较在 IL2CPP 上改不了：这里等价补"原版清空后应有的出厂值"。
            ResetToFactory(npc);
            npc.type = type;

            var mod = binding.Rebind(npc);
            if (mod is null) return;

            mod.SetDefaults(npc);
            mod.ApplyBuffImmunity(npc);
            mod.PostSetDefaults(npc);
            Finish(npc, mod);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"NPC.SetDefaults 自定义分支异常：{e.Message}");
        }
    }

    /// <summary>原版对 mod 类型会提前 return：这里补上它本来会写的那组默认值。</summary>
    private static void ResetToFactory(NPC npc)
    {
        var ai = npc.ai;
        if (ai is not null) for (var i = 0; i < ai.Length; i++) ai[i] = 0f;

        var localAI = npc.localAI;
        if (localAI is not null) for (var i = 0; i < localAI.Length; i++) localAI[i] = 0f;

        npc.width = 0;
        npc.height = 0;
        npc.life = 0;
        npc.lifeMax = 0;
        npc.defLifeMax = 0;
        npc.damage = 0;
        npc.defDamage = 0;
        npc.defense = 0;
        npc.defDefense = 0;
        npc.aiStyle = 0;
        npc.townNPC = false;
        npc.friendly = false;
        npc.npcSlots = 0f;
        npc.frame = new Rectangle(0, 0, 0, 0);
        npc.frameCounter = 0;
        npc.direction = 0;
        npc.spriteDirection = 0;
        npc.scale = 1f;
        npc.color = Color.White;
        npc.alpha = 0;
        npc.behindTiles = false;
        npc.rarity = 0;
    }

    /// <summary>游戏 SetDefaults 末尾那几步，用作者的值重做一遍。</summary>
    private void Finish(NPC npc, ModNpc mod)
    {
        var type = mod.Type;

        if (npc.width <= 0 || npc.height <= 0)
        {
            TextureSize(type, out var width, out var height);
            if (npc.width <= 0 && width > 0) npc.width = width;
            if (npc.height <= 0 && height > 0) npc.height = height;
        }

        npc.type = type;
        npc.netID = type;
        npc.active = true;

        // 原版 SetDefaults 的尾部在作者填值之前就跑过了：生命与基础攻防都还是 0，按作者的值重做。
        npc.defDamage = npc.damage;
        npc.defDefense = npc.defense;
        npc.life = npc.lifeMax;
        // ScaleStats 对 friendly / townNPC 提前退出、不写 defLifeMax；留 0 的话城镇 NPC 的 AI_007 会把 lifeMax 清零。
        npc.defLifeMax = npc.lifeMax;

        try
        {
            // 两个空 Nullable = "不覆盖"：interop 里填值等于把托管指针交给原生。
            npc.ScaleStats(new Il2CppSystem.Nullable<int>(), new Il2CppSystem.Nullable<float>());
            npc.life = npc.lifeMax;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"{mod.Name} 的 ScaleStats 跳过：{e.Message}");
        }
    }

    private static void DispatchGlobals(NPC npc)
    {
        if (npc.type <= 0) return;
        if (!GlobalNpcRegistry.AnyWith(HookId.SetDefaults)) return;

        // 实体级列表每次都重建：槽位复用与同类型重生都不继承上一只的 AppliesToEntity 结果。
        GlobalNpcRegistry.Attach(npc, true);
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.SetDefaults))
        {
            try
            {
                global.SetDefaults(npc);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"{global.GetType().Name}.SetDefaults 异常：{Safe.Describe(e)}");
            }
        }
    }

#region 帧数与贴图尺寸

    public int FramesOf(int type)
    {
        try
        {
            var table = Main.npcFrameCount;
            if (table is not null && type >= 0 && type < table.Length && table[type] > 0) return table[type];
        }
        catch
        {
            // 表还没建好：按单帧处理。
        }

        return 1;
    }

    /// <summary>贴图整宽与单帧高；贴图不在表里时返回 0（不缓存，等下次）。</summary>
    public void TextureSize(int type, out int width, out int height)
    {
        if (_sizes.TryGetValue(type, out var cached))
        {
            width = cached.Width;
            height = cached.Height;
            return;
        }

        width = 0;
        height = 0;

        try
        {
            var assets = TextureAssets.Npc;
            if (assets is null || type < 0 || type >= assets.Length) return;

            var asset = assets[type];
            var texture = asset is not null ? asset.Value : null;
            if (texture is null) return;

            PylonTexture.Size(texture, out var fullWidth, out var fullHeight);
            width = fullWidth;
            height = fullHeight / FramesOf(type);
        }
        catch
        {
            return;
        }

        _sizes[type] = new FrameSize { Width = width, Height = height };
    }

    public int FrameHeight(int type)
    {
        TextureSize(type, out _, out var height);
        return height;
    }

    /// <summary>兜底：作者没覆写 FindFrame 时，原版对未知类型不会填 frame，这里按贴图补上（安装期就把模板 frame 定好）。</summary>
    public void EnsureFrame(NPC npc)
    {
        TextureSize(npc.type, out var width, out var height);

        var frame = npc.frame;
        var changed = false;
        if (frame.Width <= 0 && width > 0) { frame.Width = width; changed = true; }
        if (frame.Height <= 0 && height > 0) { frame.Height = height; changed = true; }
        if (changed) npc.frame = frame;
    }

    /// <summary>接管原版 FindFrame 之前先填好 frame 的 X/Width/Height：作者只写 frame.Y。</summary>
    public int PrepareFrame(NPC npc)
    {
        TextureSize(npc.type, out var width, out var height);

        var frame = npc.frame;
        frame.X = 0;
        if (frame.Width <= 0 && width > 0) frame.Width = width;
        if (frame.Height <= 0 && height > 0) frame.Height = height;
        npc.frame = frame;
        return height;
    }

    private struct FrameSize
    {
        public int Width;
        public int Height;
    }
#endregion

}
