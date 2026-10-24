using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Patching;
using Pylon.ModLoader.Pipeline;
using Pylon.NaviteAPI.Host;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 物品绘制四钩子（PreDrawInWorld/PostDrawInWorld/PreDrawInInventory/PostDrawInInventory）。
/// 所有补丁点都相对运行时解析出的方法入口表达：Main.DrawItem 入口经 il2cpp 元数据解析
/// （klass + 方法名/参数个数 → 方法指针），站点偏移与系数装载对偏移都以 entry 为基准；
/// 站点装前做 16 字节指纹比对（偏移仍指向预期指令的验证），系数装载对在运行时从方法体
/// 指令流里解码（ADRP 目标页 + LDR 偏移算出数据地址），解码不符就放弃静态读。
/// 世界侧用 On 包装 Main.DrawItem：入口先复刻游戏自身的三道 early-out，再重算绘制参数
/// （light=光照色；alpha=GetAlpha 加 shimmer 修正后过 GetItemLight；scale=同一次 GetItemLight 的 out；
/// rotation=velocity.X×系数，shimmered/onConveyor 时为 0），Pre 返回 false 时不调 orig——先补调一次
/// DrawItem_GetBasics 保住其内的动画推进，随后派发 Post；Post 在 orig 返回后派发。ref rotation/scale
/// 只在方法体中段才有效，因此装一个 DrawItem 入口+0x418 的中体桥，只把作者改过的值写回寄存器现场
/// （rotation→v11、scale→栈上槽）；站点校验不过时世界侧整组不接并 Warn，游戏照常绘制。系数优先按
/// 上述指令流解码读，读不到时在桥首次命中处用游戏的 v11/velocity.X 自校验补值，仍取不到则
/// rotation 初值退化为 0（作者的写入依然生效）。
/// 库存侧用 On 包装 ItemSlot.DrawItemIcon：帧/光照/最终缩放经 DrawItem_GetColorAndScale 重算，
/// origin=帧尺寸一半，drawColor=GetAlpha(light)、itemColor=GetColor(环境色)；Pre 返回 false 时不调
/// orig 并返回 finalScale，Post 照常派发。四个钩子都没有人覆写时，两个门都不装——零补丁零订阅。
/// </summary>
internal static unsafe class ItemDrawStageHooks
{
    private const int WorldSiteOffset = 0x418;
    private const int RotationVectorIndex = 11;
    private const int ScaleStackOffset = 0x24c;
    private const int RotationCoefficientAdrpOffset = 0x270;
    private const int RotationCoefficientLdrOffset = 0x278;
    private static readonly uint[] WorldSiteWords = [0x79c14108, 0x90011939, 0xaa1f03e0, 0xb9001be8];

    private static bool _worldPre;
    private static bool _worldPost;
    private static bool _globalWorldPre;
    private static bool _globalWorldPost;
    private static bool _inventoryPre;
    private static bool _inventoryPost;
    private static bool _globalInventoryPre;
    private static bool _globalInventoryPost;
    private static bool _refsLive;
    private static ulong _imageBase;
    private static float? _rotationCoef;
    private static int _siteFirstHit;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void RefreshGlobals()
    {
        _globalWorldPre = GlobalItemRegistry.AnyWith(HookId.PreDrawInWorld);
        _globalWorldPost = GlobalItemRegistry.AnyWith(HookId.PostDrawInWorld);
        _globalInventoryPre = GlobalItemRegistry.AnyWith(HookId.PreDrawInInventory);
        _globalInventoryPost = GlobalItemRegistry.AnyWith(HookId.PostDrawInInventory);
    }

    internal static void InstallWorld()
    {
        RefreshGlobals();
        _worldPre = HookGuard.Wants<ModItem>(HookId.PreDrawInWorld) || _globalWorldPre;
        _worldPost = HookGuard.Wants<ModItem>(HookId.PostDrawInWorld) || _globalWorldPost;
        if (_worldPre && !TryInstallWorldSite())
        {
            Log.Warn("[itemdraw] 世界绘制站点未接上（原因见上一条），PreDrawInWorld/PostDrawInWorld 不接——ref 修改与跳过不可用，游戏绘制不受影响；游戏构建更新后需重验站点指纹与装载对");
            return;
        }
        On.Terraria.Main.DrawItem += DrawItem;
        Log.Info($"[itemdraw] 世界绘制钩已挂（pre={_worldPre} post={_worldPost} ref写回={_refsLive}）");
    }

    internal static void InstallInventory()
    {
        RefreshGlobals();
        _inventoryPre = HookGuard.Wants<ModItem>(HookId.PreDrawInInventory) || _globalInventoryPre;
        _inventoryPost = HookGuard.Wants<ModItem>(HookId.PostDrawInInventory) || _globalInventoryPost;
        if (!_inventoryPre && !_inventoryPost) return;
        On.Terraria.UI.ItemSlot.DrawItemIcon += DrawItemIcon;
        Log.Info($"[itemdraw] 库存绘制钩已挂（pre={_inventoryPre} post={_inventoryPost}）");
    }

    private static bool TryInstallWorldSite()
    {
        var klass = MethodCode.KlassOf(typeof(Main));
        if (!MethodCode.TryResolve(klass, "DrawItem", 2, out var body, out _))
        {
            Log.Warn("[itemdraw] Main.DrawItem 方法体没解析到，世界绘制钩不接");
            return false;
        }
        var siteAddress = unchecked((ulong)body.ToInt64() + WorldSiteOffset);
        if (!CodeImage.TryQuery(siteAddress, out var image, out var status) ||
            image.ImageBase == 0 || string.IsNullOrEmpty(image.BuildId))
        {
            Log.Warn($"[itemdraw] 世界绘制站点镜像身份不可用（{status}）");
            return false;
        }
        var fingerprint = new byte[16];
        for (var i = 0; i < WorldSiteWords.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(fingerprint.AsSpan(i * 4, 4), WorldSiteWords[i]);
        var live = new byte[16];
        fixed (byte* destination = live)
        {
            if (Arm64Native.TryReadMemory(siteAddress, destination, 16, out _) != 0)
            {
                Log.Warn("[itemdraw] 世界绘制站点现场 16 字节读不到");
                return false;
            }
        }
        for (var i = 0; i < fingerprint.Length; i++)
        {
            if (live[i] != fingerprint[i])
            {
                Log.Warn($"[itemdraw] 世界绘制站点指纹不符 at 0x{siteAddress + (uint)i:x}");
                return false;
            }
        }
        var profile = new CodeSiteProfile(
            image.BuildId,
            "itemdraw-world-pre",
            siteAddress,
            (uint)(siteAddress - image.ImageBase),
            0,
            16,
            "ItemDrawStageHooks",
            "WorldPre",
            Array.AsReadOnly(WorldSiteWords),
            Array.AsReadOnly(fingerprint));
        if (!MidBodyIntercept.TryInstall(profile, image.BuildId, InterceptWorld, true,
                out var interception, out var diagnostics, Log))
        {
            Log.Warn("[itemdraw] 世界绘制中体桥被拒：" + string.Join("; ", diagnostics));
            return false;
        }
        _imageBase = image.ImageBase;
        _refsLive = true;
        ResolveRotationCoefficient(body);
        Log.Info($"[itemdraw] 世界绘制中体桥 site=0x{siteAddress:x} strategy={interception!.RelocationStrategy}");
        return true;
    }

    private static void ResolveRotationCoefficient(IntPtr body)
    {
        var entry = unchecked((ulong)body.ToInt64());
        try
        {
            var adrpAddress = entry + (ulong)RotationCoefficientAdrpOffset;
            var adrpWord = ReadWordAt(adrpAddress);
            if (!Arm64Decoder.TryDecode(adrpAddress, adrpWord, out var decoded, out _) ||
                decoded!.Mnemonic != AsmArm64.Arm64Mnemonic.ADRP || decoded.PcTarget is not { } page)
            {
                Log.Warn("[itemdraw] rotation 系数装载对（ADRP）解码不符，桥首次命中时自校验补值");
                return;
            }
            var destination = (int)(adrpWord & 0x1F);

            var ldrAddress = entry + (ulong)RotationCoefficientLdrOffset;
            var ldrWord = ReadWordAt(ldrAddress);
            if ((ldrWord & 0xFFC00000) != 0xBD400000)
            {
                Log.Warn("[itemdraw] rotation 系数装载对（LDR）编码不符，桥首次命中时自校验补值");
                return;
            }
            var baseRegister = (int)((ldrWord >> 5) & 0x1F);
            var byteOffset = (int)((ldrWord >> 10) & 0xFFF) * 4;
            if (baseRegister != destination)
            {
                Log.Warn("[itemdraw] rotation 系数装载对寄存器不匹配，桥首次命中时自校验补值");
                return;
            }

            var buffer = new byte[4];
            fixed (byte* source = buffer)
            {
                if (Arm64Native.TryReadMemory(page + (ulong)byteOffset, source, 4, out _) != 0)
                {
                    Log.Warn("[itemdraw] rotation 系数静态读失败，桥首次命中时自校验补值");
                    return;
                }
            }
            var value = BinaryPrimitives.ReadSingleLittleEndian(buffer);
            if (!float.IsFinite(value) || value == 0f)
            {
                Log.Warn("[itemdraw] rotation 系数静态值不可用，桥首次命中时自校验补值");
                return;
            }
            _rotationCoef = value;
            Log.Info($"[itemdraw] rotation 系数={value}（装载对 entry+0x{RotationCoefficientAdrpOffset:x}/+0x{RotationCoefficientLdrOffset:x} → 0x{page + (ulong)byteOffset:x}）");
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.coef", "[itemdraw] rotation 系数解析异常：" + Safe.Describe(e), true);
        }
    }

    private static uint ReadWordAt(ulong address)
    {
        var buffer = new byte[4];
        fixed (byte* source = buffer)
        {
            if (Arm64Native.TryReadMemory(address, source, 4, out _) != 0)
                throw new InvalidOperationException($"read at 0x{address:x} failed");
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private sealed class WorldScope
    {
        public WorldItem World = null!;
        public Item Item = null!;
        public int WhoAmI;
        public Color Light;
        public Color Alpha;
        public float Rotation;
        public float Scale;
        public float VelocityX;
        public bool RotationDirty;
        public bool ScaleDirty;
    }

    [ThreadStatic] private static WorldScope? _scopeStorage;
    [ThreadStatic] private static WorldScope? _scope;

    private static void DrawItem(On.Terraria.Main.orig_DrawItem orig, Main self, WorldItem world, int whoami)
    {
        var outer = _scope;
        WorldScope? scope = null;
        var skip = false;
        try
        {
            if (world is not null && WorldWanted(world, out var item))
            {
                _scopeStorage ??= new WorldScope();
                scope = _scopeStorage;
                scope.World = world;
                scope.Item = item;
                scope.WhoAmI = whoami;
                scope.RotationDirty = false;
                scope.ScaleDirty = false;
                RecomputeWorldArgs(scope);
                _scope = scope;
                skip = !DispatchPreDrawInWorld(scope);
            }
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.world.pre", "[itemdraw] 世界绘制 pre 分派异常：" + Safe.Describe(e), true);
            scope = null;
            skip = false;
        }

        try
        {
            if (skip)
            {
                RunGetBasics(self, world, whoami);
                if (scope is not null) DispatchPostDrawInWorld(scope);
                return;
            }
            orig(self, world, whoami);
            if (scope is not null) DispatchPostDrawInWorld(scope);
        }
        finally
        {
            _scope = outer;
        }
    }

    private static bool WorldWanted(WorldItem world, out Item item)
    {
        item = null!;
        try
        {
            if (!world.active || world.IsAir) return false;
            if (world.instanced && world.playerIndexTheItemIsReservedFor != Main.myPlayer) return false;
            item = world.inner;
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.world.wanted", "[itemdraw] 世界绘制 wanted 判定异常：" + Safe.Describe(e), true);
            return false;
        }
        return item is not null && item.type > 0 &&
            (_globalWorldPre || _globalWorldPost || ModContent.IsCustom(ContentKind.Item, item.type));
    }

    private static void RecomputeWorldArgs(WorldScope scope)
    {
        var world = scope.World;
        var item = scope.Item;
        scope.Light = Lighting.GetColor(Utils.ToTileCoordinates(world.Center));
        var alpha = item.GetAlpha(scope.Light);
        if (world.shimmered)
        {
            var fade = (int)((1f - world.shimmerTime) * 255f) & 0xFF;
            alpha = new Color(fade, fade, fade, fade);
        }
        else if (world.shimmerTime > 0f)
        {
            alpha = FadeColor(alpha, 1f - world.shimmerTime);
        }
        float scale = 1f;
        ItemSlot.GetItemLight(ref alpha, ref scale, item, false, 1f);
        scope.Alpha = alpha;
        scope.Scale = scale;
        scope.VelocityX = world.velocity.X;
        scope.Rotation = world.shimmered || world.onConveyor ? 0f : scope.VelocityX * RotationCoefficient();
        scope.RotationDirty = false;
        scope.ScaleDirty = false;
    }

    private static Color FadeColor(Color color, float factor)
    {
        return new Color(
            (int)(factor * color.R) & 0xFF,
            (int)(factor * color.G) & 0xFF,
            (int)(factor * color.B) & 0xFF,
            (int)(factor * color.A) & 0xFF);
    }

    private static float RotationCoefficient() => _rotationCoef ?? 0f;

    private static bool InterceptWorld(IntPtr frame)
    {
        try
        {
            var scope = _scope;
            if (scope is null || (IntPtr)InterceptFrame.GetGpr(frame, 19) != scope.World.Pointer) return false;
            if (Interlocked.Exchange(ref _siteFirstHit, 1) == 0)
                Log.Info("[itemdraw] 世界绘制中体桥首次命中");
            SelfCheckRotation(scope, frame);
            if (scope.RotationDirty && float.IsFinite(scope.Rotation))
                InterceptFrame.SetVectorUInt64(frame, RotationVectorIndex, 0,
                    BitConverter.SingleToUInt32Bits(scope.Rotation));
            if (scope.ScaleDirty && float.IsFinite(scope.Scale))
                *(float*)((byte*)frame + InterceptFrame.FrameBytes + ScaleStackOffset) = scope.Scale;
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.world.stub", "[itemdraw] 世界绘制中体桥分派异常：" + Safe.Describe(e), true);
        }
        return false;
    }

    private static void SelfCheckRotation(WorldScope scope, IntPtr frame)
    {
        if (_rotationCoef is not null) return;
        var live = BitConverter.UInt32BitsToSingle((uint)InterceptFrame.GetVectorUInt64(frame, RotationVectorIndex, 0));
        if (!float.IsFinite(live) || live == 0f || scope.VelocityX == 0f || scope.RotationDirty) return;
        var derived = live / scope.VelocityX;
        if (!float.IsFinite(derived) || derived == 0f) return;
        _rotationCoef = derived;
        Log.Info($"[itemdraw] rotation 系数自校验补值={derived}");
    }

    private static bool DispatchPreDrawInWorld(WorldScope scope)
    {
        var item = scope.Item;
        var rotation = scope.Rotation;
        var scale = scope.Scale;
        var go = true;

        var mod = ItemPipeline.Instance.InstanceOf(item);
        if (mod is not null && mod.Plan.Has(HookId.PreDrawInWorld))
        {
            try
            {
                go &= mod.PreDrawInWorld(Main.spriteBatch, scope.Light, scope.Alpha, ref rotation, ref scale, scope.WhoAmI);
            }
            catch (Exception e)
            {
                Report(mod.FullKey, HookId.PreDrawInWorld, e);
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.PreDrawInWorld))
        {
            if (!global.Plan.Has(HookId.PreDrawInWorld)) continue;
            try
            {
                go &= global.PreDrawInWorld(item, Main.spriteBatch, scope.Light, scope.Alpha, ref rotation, ref scale, scope.WhoAmI);
            }
            catch (Exception e)
            {
                Report(global.FullKey, HookId.PreDrawInWorld, e);
            }
        }

        scope.RotationDirty = rotation != scope.Rotation;
        scope.ScaleDirty = scale != scope.Scale;
        scope.Rotation = rotation;
        scope.Scale = scale;
        return go;
    }

    private static void DispatchPostDrawInWorld(WorldScope scope)
    {
        try
        {
            DispatchPostDrawInWorldCore(scope);
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.world.post", "[itemdraw] 世界绘制 post 分派异常：" + Safe.Describe(e), true);
        }
    }

    private static void DispatchPostDrawInWorldCore(WorldScope scope)
    {
        var item = scope.Item;
        var mod = ItemPipeline.Instance.InstanceOf(item);
        if (mod is not null && mod.Plan.Has(HookId.PostDrawInWorld))
        {
            try
            {
                mod.PostDrawInWorld(Main.spriteBatch, scope.Light, scope.Alpha, scope.Rotation, scope.Scale, scope.WhoAmI);
            }
            catch (Exception e)
            {
                Report(mod.FullKey, HookId.PostDrawInWorld, e);
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.PostDrawInWorld))
        {
            if (!global.Plan.Has(HookId.PostDrawInWorld)) continue;
            try
            {
                global.PostDrawInWorld(item, Main.spriteBatch, scope.Light, scope.Alpha, scope.Rotation, scope.Scale, scope.WhoAmI);
            }
            catch (Exception e)
            {
                Report(global.FullKey, HookId.PostDrawInWorld, e);
            }
        }
    }

    private static void RunGetBasics(Main self, WorldItem? world, int whoami)
    {
        try
        {
            if (world is null) return;
            self.DrawItem_GetBasics(world.inner, whoami, out _, out _, out _);
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.world.getbasics", "[itemdraw] 动画推进补偿调用异常：" + Safe.Describe(e), true);
        }
    }

    private sealed class InventoryArgs
    {
        public Rectangle Frame;
        public Vector2 Origin;
        public Color DrawColor;
        public Color ItemColor;
        public float Scale;
        public float FinalScale;
    }

    private static float DrawItemIcon(On.Terraria.UI.ItemSlot.orig_DrawItemIcon orig, Item item, int context,
        SpriteBatch spriteBatch, Vector2 screenPositionForItemCenter, float scale, float sizeLimit,
        Color environmentColor, float itemFade, bool flip)
    {
        var skip = false;
        InventoryArgs? args = null;
        try
        {
            if (InventoryWanted(item) && TryComputeInventoryArgs(item, scale, sizeLimit, environmentColor, out var computed))
            {
                args = computed;
                skip = !DispatchPreDrawInInventory(item, spriteBatch, screenPositionForItemCenter, computed);
            }
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.inventory", "[itemdraw] 库存绘制 pre 分派异常：" + Safe.Describe(e), true);
            args = null;
            skip = false;
        }

        if (skip)
        {
            DispatchPostDrawInInventory(item, spriteBatch, screenPositionForItemCenter, args!);
            return args!.FinalScale;
        }

        var result = orig(item, context, spriteBatch, screenPositionForItemCenter, scale, sizeLimit,
            environmentColor, itemFade, flip);
        if (args is not null) DispatchPostDrawInInventory(item, spriteBatch, screenPositionForItemCenter, args);
        return result;
    }

    private static bool InventoryWanted(Item? item) =>
        item is not null && item.type > 0 &&
        (_globalInventoryPre || _globalInventoryPost || ModContent.IsCustom(ContentKind.Item, item.type));

    private static bool TryComputeInventoryArgs(Item item, float scale, float sizeLimit, Color environmentColor,
        out InventoryArgs args)
    {
        args = new InventoryArgs();
        try
        {
            var texture = ItemTexture(item);
            if (texture is not null) args.Frame = Utils.Frame(texture, 1, 1);
            var white = Color.White;
            ItemSlot.DrawItem_GetColorAndScale(item, scale, ref white, sizeLimit, ref args.Frame,
                out var light, out var finalScale);
            args.FinalScale = finalScale;
            args.Scale = finalScale;
            args.Origin = new Vector2(args.Frame.Width / 2f, args.Frame.Height / 2f);
            args.DrawColor = item.GetAlpha(light);
            args.ItemColor = item.GetColor(environmentColor);
            return true;
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.inventory.args", "[itemdraw] 库存绘制参数重算异常：" + Safe.Describe(e), true);
            return false;
        }
    }

    private static Texture2D? ItemTexture(Item item)
    {
        var assets = TextureAssets.Item;
        var type = item.type;
        if (type >= 0 && type < assets.Length)
        {
            var asset = assets[type];
            if (asset is not null) return asset.Value;
        }
        return assets.Length > 0 ? assets[0]?.Value : null;
    }

    private static bool DispatchPreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position,
        InventoryArgs args)
    {
        var go = true;
        foreach (var global in GlobalItemRegistry.For(item, HookId.PreDrawInInventory))
        {
            if (!global.Plan.Has(HookId.PreDrawInInventory)) continue;
            try
            {
                go &= global.PreDrawInInventory(item, spriteBatch, position, args.Frame, args.DrawColor,
                    args.ItemColor, args.Origin, args.Scale);
            }
            catch (Exception e)
            {
                Report(global.FullKey, HookId.PreDrawInInventory, e);
            }
        }

        var mod = ItemPipeline.Instance.InstanceOf(item);
        if (mod is not null && mod.Plan.Has(HookId.PreDrawInInventory))
        {
            try
            {
                go &= mod.PreDrawInInventory(spriteBatch, position, args.Frame, args.DrawColor,
                    args.ItemColor, args.Origin, args.Scale);
            }
            catch (Exception e)
            {
                Report(mod.FullKey, HookId.PreDrawInInventory, e);
            }
        }
        return go;
    }

    private static void DispatchPostDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position,
        InventoryArgs args)
    {
        try
        {
            DispatchPostDrawInInventoryCore(item, spriteBatch, position, args);
        }
        catch (Exception e)
        {
            Safe.Once("itemdraw.inventory.post", "[itemdraw] 库存绘制 post 分派异常：" + Safe.Describe(e), true);
        }
    }

    private static void DispatchPostDrawInInventoryCore(Item item, SpriteBatch spriteBatch, Vector2 position,
        InventoryArgs args)
    {
        var mod = ItemPipeline.Instance.InstanceOf(item);
        if (mod is not null && mod.Plan.Has(HookId.PostDrawInInventory))
        {
            try
            {
                mod.PostDrawInInventory(spriteBatch, position, args.Frame, args.DrawColor,
                    args.ItemColor, args.Origin, args.Scale);
            }
            catch (Exception e)
            {
                Report(mod.FullKey, HookId.PostDrawInInventory, e);
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.PostDrawInInventory))
        {
            if (!global.Plan.Has(HookId.PostDrawInInventory)) continue;
            try
            {
                global.PostDrawInInventory(item, spriteBatch, position, args.Frame, args.DrawColor,
                    args.ItemColor, args.Origin, args.Scale);
            }
            catch (Exception e)
            {
                Report(global.FullKey, HookId.PostDrawInInventory, e);
            }
        }
    }

    private static readonly HashSet<string> _reported = [];

    private static void Report(string key, HookId hook, Exception exception)
    {
        if (!_reported.Add(key + "/" + hook)) return;
        ContentErrors.Report($"[item] {key}.{hook} 异常：{Safe.Describe(exception)}");
    }
}
