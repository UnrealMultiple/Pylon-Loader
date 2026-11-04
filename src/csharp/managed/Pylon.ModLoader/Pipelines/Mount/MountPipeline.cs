using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>
/// 坐骑管线：骑上 / 下来 / 每帧 / 跳 / 能力 / 绘制。
/// 坐骑是"玩家身上一个 Mount 对象"，所以除了类型表还要按玩家记一份作者数据。
/// </summary>
internal sealed class MountPipeline : ILoaderModule
{
    public static MountPipeline Instance { get; } = new();

    private readonly Dictionary<int, object> _specific = [];
    private readonly HashSet<int> _reported = [];

    private MountCall? _setting;
    private bool _dismountSkipDust;

    public string Name => "mount";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Mount.SetMount += SetMount;
        On.Terraria.Mount.DoSpawnDust += SpawnDust;
        On.Terraria.Mount.Dismount += Dismount;
        On.Terraria.Mount.UpdateEffects += UpdateEffects;
        On.Terraria.Mount.UpdateFrame += UpdateFrame;
        On.Terraria.Mount.JumpHeight += JumpHeight;
        On.Terraria.Mount.JumpSpeed += JumpSpeed;
        On.Terraria.Mount.UseAbility += UseAbility;
        On.Terraria.Mount.AimAbility += AimAbility;
        On.Terraria.Mount.Draw += Draw;
        On.Terraria.Mount.Initialize += Initialized;

        Log.Info("[mount] 已挂（骑 / 下 / 每帧 / 跳 / 能力 / 绘制 / 表重建）");
        return true;
    }

    #region 作者数据

    /// <summary>这个玩家骑在这匹上时的额外数据；没设过是 null。</summary>
    internal object SpecificData(Player player) =>
        player is not null && _specific.TryGetValue(player.whoAmI, out var value) ? value : null!;

    internal void SetSpecificData(Player player, object value)
    {
        if (player is null) return;

        if (value is null) _specific.Remove(player.whoAmI);
        else _specific[player.whoAmI] = value;
    }

    #endregion

    #region 骑乘

    /// <summary>一次 SetMount 的上下文：轮到哪匹、作者的 SetMount 跑过没有。</summary>
    private sealed class MountCall
    {
        public ModMount Mod = null!;
        public bool Called;
    }

    private void SetMount(On.Terraria.Mount.orig_SetMount orig, Mount self, int m, Player mountedPlayer,
                          bool ignoreEffect)
    {
        _setting = null;

        var mod = MountTable.Of(m);
        if (mod is not null && self._type != m)
        {
            // 换了一匹：旧数据与这匹无关了。
            _specific.Remove(mountedPlayer.whoAmI);
            _setting = new MountCall { Mod = mod };
        }

        orig(self, m, mountedPlayer, ignoreEffect);

        // 作者的 SetMount 本该在尘土里跑；原版这次没撒尘土，就在这里补一次。
        var setting = _setting;
        _setting = null;
        if (setting is null || setting.Called || self._type != m) return;

        setting.Called = true;
        var skip = false;
        try
        {
            setting.Mod.SetMount(mountedPlayer, ref skip);
        }
        catch (Exception e)
        {
            Fail(setting.Mod.Name, HookId.SetMount, e);
        }
    }

    /// <summary>
    /// 尘土：骑上时作者的 SetMount 在这里跑（这时 _type 已换成 m，比 SetMount 里可靠）；
    /// 下来时作者要求跳过就跳过。不能按 _type 过滤——SetMount 期间 _type 还没换。
    /// </summary>
    private void SpawnDust(On.Terraria.Mount.orig_DoSpawnDust orig, Mount self, Player mountedPlayer,
                           bool isDismounting)
    {
        var skip = false;

        if (isDismounting)
        {
            skip = _dismountSkipDust;
        }
        else if (_setting is { Called: false } setting)
        {
            setting.Called = true;
            try
            {
                setting.Mod.SetMount(mountedPlayer, ref skip);
            }
            catch (Exception e)
            {
                Fail(setting.Mod.Name, HookId.SetMount, e);
            }
        }

        if (skip) return;

        orig(self, mountedPlayer, isDismounting);
    }

    private void Dismount(On.Terraria.Mount.orig_Dismount orig, Mount self, Player mountedPlayer, bool ignoreEffect)
    {
        var skip = false;
        var mod = MountTable.Of(self);

        if (mod is not null)
        {
            try
            {
                mod.Dismount(mountedPlayer, ref skip);
            }
            catch (Exception e)
            {
                Fail(mod.Name, HookId.Dismount, e);
            }
        }

        _dismountSkipDust = skip;
        orig(self, mountedPlayer, ignoreEffect);
        _dismountSkipDust = false;

        if (mountedPlayer is not null) _specific.Remove(mountedPlayer.whoAmI);
    }

    #endregion

    #region 每帧

    private void UpdateEffects(On.Terraria.Mount.orig_UpdateEffects orig, Mount self, Player mountedPlayer)
    {
        var mod = MountTable.Of(self);
        if (mod is null)
        {
            orig(self, mountedPlayer);
            return;
        }

        try
        {
            mod.UpdateEffects(mountedPlayer);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.UpdateEffects, e);
        }

        orig(self, mountedPlayer);
    }

    /// <summary>只有明确返回 false 才跳过游戏动画；抛异常照跑原版。</summary>
    private void UpdateFrame(On.Terraria.Mount.orig_UpdateFrame orig, Mount self, Player mountedPlayer, int state,
                             Vector2 velocity)
    {
        var mod = MountTable.Of(self);
        if (mod is not null)
        {
            try
            {
                if (!mod.UpdateFrame(mountedPlayer, state, velocity)) return;
            }
            catch (Exception e)
            {
                Fail(mod.Name, HookId.UpdateFrame, e);
            }
        }

        orig(self, mountedPlayer, state, velocity);
    }

    /// <summary>手机版跳高没有 Player 参数：按 Mount 对象地址反查骑它的玩家。</summary>
    private int JumpHeight(On.Terraria.Mount.orig_JumpHeight orig, Mount self, float xVelocity)
    {
        var result = orig(self, xVelocity);

        var mod = MountTable.Of(self);
        if (mod is null || !mod.HookEnabled(HookId.JumpHeight)) return result;

        var owner = OwnerOf(self);
        if (owner is null) return result;

        try
        {
            mod.JumpHeight(owner, ref result, xVelocity);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.JumpHeight, e);
        }

        return result;
    }

    private float JumpSpeed(On.Terraria.Mount.orig_JumpSpeed orig, Mount self, float xVelocity)
    {
        var result = orig(self, xVelocity);

        var mod = MountTable.Of(self);
        if (mod is null || !mod.HookEnabled(HookId.JumpSpeed)) return result;

        var owner = OwnerOf(self);
        if (owner is null) return result;

        try
        {
            mod.JumpSpeed(owner, ref result, xVelocity);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.JumpSpeed, e);
        }

        return result;
    }

    private void UseAbility(On.Terraria.Mount.orig_UseAbility orig, Mount self, Player mountedPlayer,
                            Vector2 mousePosition, bool toggleOn)
    {
        orig(self, mountedPlayer, mousePosition, toggleOn);

        var mod = MountTable.Of(self);
        if (mod is null) return;

        try
        {
            mod.UseAbility(mountedPlayer, mousePosition, toggleOn);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.UseAbility, e);
        }
    }

    /// <summary>瞄准在原版判定之前跑（作者要能拦住这次的瞄准）。</summary>
    private bool AimAbility(On.Terraria.Mount.orig_AimAbility orig, Mount self, Player mountedPlayer,
                            Vector2 mousePosition)
    {
        var mod = MountTable.Of(self);
        if (mod is null) return orig(self, mountedPlayer, mousePosition);

        try
        {
            mod.AimAbility(mountedPlayer, mousePosition);
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.AimAbility, e);
        }

        return orig(self, mountedPlayer, mousePosition);
    }

    #endregion

    #region 绘制

    /// <summary>交给作者的那批绘制值：改成值类型快照，比完之后没变化就交回原版画。</summary>
    private readonly record struct DrawState(Texture2D? Texture, Texture2D? Glow, Vector2 Position, Rectangle Frame,
                                             Color Color, Color GlowColor, float Rotation, SpriteEffects Effect,
                                             Vector2 Origin, float Scale);

    private void Draw(On.Terraria.Mount.orig_Draw orig, Mount self, PlayerDrawSet playerDrawData, int drawType,
                      Player drawPlayer, Vector2 position, Color drawColor, SpriteEffects playerEffect, float shadow)
    {
        var mod = MountTable.Of(self);
        if (mod is null || !mod.HookEnabled(HookId.Draw))
        {
            orig(self, playerDrawData, drawType, drawPlayer, position, drawColor, playerEffect, shadow);
            return;
        }

        try
        {
            if (DrawLayer(mod, self, playerDrawData, drawType, drawPlayer, position, drawColor, playerEffect, shadow))
                return;
        }
        catch (Exception e)
        {
            Fail(mod.Name, HookId.Draw, e);
        }

        orig(self, playerDrawData, drawType, drawPlayer, position, drawColor, playerEffect, shadow);
    }

    /// <summary>返回 true = 这层已经处理完（画过了或不画），不再走原版。</summary>
    private static bool DrawLayer(ModMount mod, Mount mount, PlayerDrawSet set, int drawType, Player player,
                                  Vector2 position, Color drawColor, SpriteEffects effect, float shadow)
    {
        var data = mount._data;
        if (data is null) return false;

        var (textureAsset, glowAsset) = LayerOf(data, drawType);
        var texture = textureAsset?.Value;
        if (texture is null) return false;

        var glow = glowAsset?.Value;

        var screen = Main.screenPosition;
        var xOffset = mount.XOffset * (player.direction <= 0 ? -1 : 1);
        var yOffset = mount.YOffset + mount.PlayerOffset;
        var frameHeight = (int)((float)data.textureHeight / data.totalFrames);

        var glowColor = new Color(drawColor.R * 0.25f + 191.25f, drawColor.G * 0.25f + 191.25f,
                                  drawColor.B * 0.25f + 191.25f, drawColor.A * 0.25f + 191.25f);

        var spriteEffects = effect;
        if (mount.Cart && Math.Sign(player.velocity.X) == -player.direction)
            spriteEffects = (SpriteEffects)((int)effect ^ 1);

        var drawTexture = texture;
        var drawGlow = glow!;
        var drawPosition = new Vector2(
            (int)(position.X - screen.X + player.width / 2f + xOffset),
            (int)(position.Y - screen.Y + player.height / 2f + yOffset));
        var frame = new Rectangle(0, frameHeight * mount._frame, data.textureWidth, frameHeight - 2);
        var color = drawColor;
        var glowDrawColor = glowColor;
        var rotation = 0f;
        var drawOrigin = mount.Origin;
        var drawScale = 1f;

        var before = new DrawState(drawTexture, glow, drawPosition, frame, color, glowDrawColor, rotation,
            spriteEffects, drawOrigin, drawScale);

        var keep = mod.Draw(set, drawType, player, ref drawTexture, ref drawGlow, ref drawPosition, ref frame,
            ref color, ref glowDrawColor, ref rotation, ref spriteEffects, ref drawOrigin, ref drawScale, shadow);
        if (!keep) return true;

        var after = new DrawState(drawTexture, glow, drawPosition, frame, color, glowDrawColor, rotation, spriteEffects,
            drawOrigin, drawScale);

        // 一个值都没改：交回原版，让原版的特例（染色、速度拖影等）继续生效。
        if (after.Equals(before)) return false;

        if (drawTexture is not null) Push(set, drawTexture, drawPosition, frame, color, rotation, drawOrigin, drawScale, spriteEffects);
        if (glow is not null)
        {
            var alpha = color.A / 255f;
            Push(set, glow, drawPosition, frame,
                new Color(glowDrawColor.R * alpha, glowDrawColor.G * alpha, glowDrawColor.B * alpha, glowDrawColor.A * alpha),
                rotation, drawOrigin, drawScale, spriteEffects);
        }

        return true;
    }

    /// <summary>drawType → 这层的两张贴图（0 Back、1 BackExtra、2 Front、3 FrontExtra）。</summary>
    private static (Asset<Texture2D>? Texture, Asset<Texture2D>? Glow) LayerOf(Mount.MountData data, int drawType) =>
        drawType switch
        {
            0 => (data.backTexture, data.backTextureGlow),
            1 => (data.backTextureExtra, data.backTextureExtraGlow),
            2 => (data.frontTexture, data.frontTextureGlow),
            3 => (data.frontTextureExtra, data.frontTextureExtraGlow),
            _ => (null, null),
        };

    private static void Push(PlayerDrawSet set, Texture2D texture, Vector2 position, Rectangle source, Color color,
                             float rotation, Vector2 origin, float scale, SpriteEffects effect)
    {
        var data = ModMount.NewDrawData(texture, position, source, color, rotation, origin, scale, effect);
        data.shader = Mount.currentShader;
        ModMount.AddDrawData(set, data);
    }

    #endregion

    private void Initialized(On.Terraria.Mount.orig_Initialize orig)
    {
        orig();
        MountTable.Reapply();
    }

    /// <summary>按 Mount 对象地址反查骑它的玩家：本地玩家先试，其余扫数组（多出的槽位没人骑，等价于写死上限）。</summary>
    private static Player? OwnerOf(Mount mount)
    {
        var address = Address(mount);
        if (address == 0) return null;

        var local = Main.player[Main.myPlayer];
        if (local is not null && Address(local.mount) == address) return local;

        var players = Main.player;
        for (var i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (player is not null && player.active && Address(player.mount) == address) return player;
        }

        return null;
    }

    private static long Address(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? obj) =>
        obj is null ? 0 : (long)obj.Pointer;

    /// <summary>同一 (名字, 钩子) 只记一条：每帧路径出错会刷屏。</summary>
    private void Fail(string name, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(name.GetHashCode(), hook))) return;
        ContentErrors.Report($"[mount] {hook} 异常：{Safe.Describe(exception)}");
    }
}
