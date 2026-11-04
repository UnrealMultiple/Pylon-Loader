using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// 坐骑表：类型 ↔ 实例、MountData 的建立与写回、MountID.Sets 扩长。
/// 游戏会重建 Mount.mounts（Mount.Initialize），所以写回必须可重复执行。
/// </summary>
internal static class MountTable
{
    private static readonly List<ModMount> Built = [];

    private static readonly (string Suffix, Action<Mount.MountData, Asset<Texture2D>> Set)[] Layers =
    [
        ("_Back", (data, asset) => data.backTexture = asset),
        ("_BackGlow", (data, asset) => data.backTextureGlow = asset),
        ("_BackExtra", (data, asset) => data.backTextureExtra = asset),
        ("_BackExtraGlow", (data, asset) => data.backTextureExtraGlow = asset),
        ("_Front", (data, asset) => data.frontTexture = asset),
        ("_FrontGlow", (data, asset) => data.frontTextureGlow = asset),
        ("_FrontExtra", (data, asset) => data.frontTextureExtra = asset),
        ("_FrontExtraGlow", (data, asset) => data.frontTextureExtraGlow = asset),
    ];

    private static int _vanilla = -1;

    internal static int VanillaCount => _vanilla >= 0 ? _vanilla : _vanilla = MountID.Count;

    internal static int Count => VanillaCount + Built.Count;

    internal static ModMount? Of(int type) =>
        type < VanillaCount ? null : ModContent.Find(ContentKind.Mount, type) as ModMount;

    /// <summary>钩子拿到的是 Mount 对象，它自己带着类型号。</summary>
    internal static ModMount? Of(Mount mount) => mount is null ? null : Of(mount._type);

    /// <summary>内容就绪后一次：建 MountData → 写回表 → 静态默认值 → buff 标记。</summary>
    internal static void Build(IModLog log)
    {
        var mounts = ModContent.GetContent<ModMount>();
        if (mounts.Count == 0) return;

        if (Built.Count == 0)
        {
            foreach (var mount in mounts)
            {
                mount.MountData = CreateData(mount);
                Built.Add(mount);
            }
        }

        Reapply();

        // 所有 MountData 都进了表之后再跑：作者在那里改 MountData 字段与 MountID.Sets。
        foreach (var mount in Built)
        {
            mount.Hooks = HookScan.Of(mount.GetType(), typeof(ModMount));

            try
            {
                mount.SetStaticDefaults();
            }
            catch (Exception e)
            {
                log.Warn($"{mount.Name}.SetStaticDefaults 异常：{e.Message}");
            }
        }

        ApplyBuffFlags();
        log.Info($"[mount] 安装：{Built.Count} 匹（类型 {Built[0].Type}..{Count - 1}）");
    }

    /// <summary>Mount.Initialize 会按 MountID.Count 重建数组，mod 槽位变 null 就是每帧空引用：这里补回去。</summary>
    internal static void Reapply()
    {
        if (Built.Count == 0) return;

        var total = VanillaCount + Built.Count;

        try
        {
            var table = Mount.mounts;
            var grown = new Mount.MountData[total];
            if (table is not null)
            {
                for (var i = 0; i < table.Length && i < total; i++) grown[i] = table[i];
            }

            foreach (var mount in Built) grown[mount.Type] = mount.MountData;

            GrowSets(total);

            // 托管数组一次性赋给原生数组（内部整块拷贝）：在新生成的 il2cpp 数组上逐元素写会 SIGSEGV。
            Mount.mounts = grown;
            MountID.Count = total;

            ApplyBuffFlags();
        }
        catch (Exception e)
        {
            Safe.Report("mount.reapply", e);
        }
    }

    private static Mount.MountData CreateData(ModMount mount)
    {
        var data = new Mount.MountData();

        foreach (var (suffix, set) in Layers)
        {
            var asset = LoadLayer(mount, suffix);
            if (asset is not null) set(data, asset);
        }

        FrameDefaults(data);
        return data;
    }

    /// <summary>贴图存不存在决定这一层画不画：不存在就留空。</summary>
    private static Asset<Texture2D>? LoadLayer(ModMount mount, string suffix)
    {
        var path = mount.TexturePath + suffix + ".png";

        try
        {
            return ModContent.HasAsset(mount.Mod, path) ? mount.Mod.LoadAsset(path) : null;
        }
        catch (Exception e)
        {
            Safe.Report("mount." + mount.Name + "." + suffix, e);
            return null;
        }
    }

    /// <summary>作者不写尺寸/帧数时也要能安全画：绘制要除 totalFrames，玩家偏移要索引 playerYOffsets。</summary>
    private static void FrameDefaults(Mount.MountData data)
    {
        var texture = data.backTexture ?? data.frontTexture;

        var width = 0;
        var height = 0;
        if (texture is not null)
        {
            try
            {
                PylonTexture.Size(texture.Value, out width, out height);
            }
            catch
            {
                // 贴图还没上传时拿不到尺寸：留 0，作者自己写。
            }
        }

        if (data.textureWidth <= 0) data.textureWidth = width;
        if (data.textureHeight <= 0) data.textureHeight = height;
        if (data.totalFrames <= 0) data.totalFrames = 1;

        var frames = data.totalFrames;
        if (data.playerYOffsets is null || data.playerYOffsets.Length < frames)
        {
            var offsets = new int[frames];
            var old = data.playerYOffsets;
            if (old is not null)
            {
                for (var i = 0; i < old.Length && i < frames; i++) offsets[i] = old[i];
            }

            data.playerYOffsets = offsets;
        }

        // 七组动画：默认"单帧停在 0"。interop 把这些生成了属性，不能 ref 传，只能逐组写。
        if (data.standingFrameCount <= 0) { data.standingFrameStart = 0; data.standingFrameCount = 1; }
        if (data.runningFrameCount <= 0) { data.runningFrameStart = 0; data.runningFrameCount = 1; }
        if (data.inAirFrameCount <= 0) { data.inAirFrameStart = 0; data.inAirFrameCount = 1; }
        if (data.flyingFrameCount <= 0) { data.flyingFrameStart = 0; data.flyingFrameCount = 1; }
        if (data.swimFrameCount <= 0) { data.swimFrameStart = 0; data.swimFrameCount = 1; }
        if (data.dashingFrameCount <= 0) { data.dashingFrameStart = 0; data.dashingFrameCount = 1; }
        if (data.idleFrameCount <= 0) { data.idleFrameStart = 0; data.idleFrameCount = 1; }
    }

    /// <summary>MountID.Sets 的 12 张 bool 表按总数扩长（原版按 MountID.Count 索引）。</summary>
    private static void GrowSets(int total)
    {
        MountID.Sets.CanUseHooks = Grow(MountID.Sets.CanUseHooks, total);
        MountID.Sets.CanDash = Grow(MountID.Sets.CanDash, total);
        MountID.Sets.DoesNotOverrideBodyFrames = Grow(MountID.Sets.DoesNotOverrideBodyFrames, total);
        MountID.Sets.DoesNotOverrideLegFrames = Grow(MountID.Sets.DoesNotOverrideLegFrames, total);
        MountID.Sets.DoesNotOverrideBackpackDraw = Grow(MountID.Sets.DoesNotOverrideBackpackDraw, total);
        MountID.Sets.DoesNotOverrideWings = Grow(MountID.Sets.DoesNotOverrideWings, total);
        MountID.Sets.IsRollerSkates = Grow(MountID.Sets.IsRollerSkates, total);
        MountID.Sets.Cart = Grow(MountID.Sets.Cart, total);
        MountID.Sets.IsTransformationMount = Grow(MountID.Sets.IsTransformationMount, total);
        MountID.Sets.PlayerIsHidden = Grow(MountID.Sets.PlayerIsHidden, total);
        MountID.Sets.DontHoldItems = Grow(MountID.Sets.DontHoldItems, total);
        MountID.Sets.DontDismountWhenCCed = Grow(MountID.Sets.DontDismountWhenCCed, total);
    }

    private static bool[] Grow(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>? old, int total)
    {
        var grown = new bool[total];
        if (old is not null)
        {
            for (var i = 0; i < old.Length && i < total; i++) grown[i] = old[i];
        }

        return grown;
    }

    /// <summary>BuffID.Sets.MountType 指向坐骑的 buff 不显示时间、不存档；buff 表重建后标记会丢，所以每次写表都补。</summary>
    private static void ApplyBuffFlags()
    {
        var mountOf = BuffID.Sets.MountType;
        var noTime = Main.buffNoTimeDisplay;
        var noSave = Main.buffNoSave;
        if (mountOf is null || noTime is null || noSave is null) return;

        foreach (var buff in ModContent.GetContent<ModBuff>())
        {
            var type = buff.Type;
            if (type < 0 || type >= mountOf.Length || mountOf[type] < 0) continue;
            if (type >= noTime.Length || type >= noSave.Length) continue;

            noTime[type] = true;
            noSave[type] = true;
        }
    }
}
