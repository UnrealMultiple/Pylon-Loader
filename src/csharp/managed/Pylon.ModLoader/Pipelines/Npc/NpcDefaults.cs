using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>NPC.SetDefaults 接管：原版先跑，再叠作者默认值并补上原版 type 超界提前 return 漏掉的收尾；同时缓存帧数与贴图尺寸。</summary>
internal sealed class NpcDefaults(NpcBinding binding)
{
    private readonly Dictionary<int, FrameSize> _sizes = [];

    public void Install()
    {
        On.Terraria.NPC.SetDefaults += SetDefaults;
        On.Terraria.NPC.SetDefaultsFromNetId += FromNetId;
    }

    private void FromNetId(On.Terraria.NPC.orig_SetDefaultsFromNetId orig, NPC self, int type, NPCSpawnParams spawnParams)
    {
        orig(self, type, spawnParams);
        if (self is null || !GlobalNpcRegistry.AnyWith(HookId.SetDefaultsFromNetId)) return;

        foreach (var global in GlobalNpcRegistry.For(self, HookId.SetDefaultsFromNetId))
        {
            try { global.SetDefaultsFromNetId(self); }
            catch (Exception e) { ContentErrors.Report($"{global.Name}.SetDefaultsFromNetId 异常：{Safe.Describe(e)}"); }
        }
    }

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
            ResetToFactory(npc);
            npc.type = type;

            var mod = binding.Rebind(npc);
            if (mod is null) return;

            mod.SetDefaults();
            Finish(npc, mod);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"NPC.SetDefaults 自定义分支异常：{e.Message}");
        }
    }

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

        npc.defDamage = npc.damage;
        npc.defDefense = npc.defense;
        npc.life = npc.lifeMax;
        npc.defLifeMax = npc.lifeMax;

        try
        {
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
        }

        return 1;
    }

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

    public void EnsureFrame(NPC npc)
    {
        TextureSize(npc.type, out var width, out var height);

        var frame = npc.frame;
        var changed = false;
        if (frame.Width <= 0 && width > 0) { frame.Width = width; changed = true; }
        if (frame.Height <= 0 && height > 0) { frame.Height = height; changed = true; }
        if (changed) npc.frame = frame;
    }

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
