using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.DataStructures;
using Terraria.Graphics;
using Terraria.Graphics.Renderers;

namespace Pylon.ModLoader;

/// <summary>
/// 玩家绘制信息管线：把 <see cref="ModPlayer"/> 的五个绘制钩子接到 <c>PlayerDrawSet</c> 的真实构建链上。
/// 之前只挂在 <c>LegacyPlayerRenderer.DrawPlayer</c> 的前后（见 PlayerDrawLayerPipeline 的两层），
/// 但 <c>DrawEffects</c> / <c>ModifyDrawInfo</c> 要改的是绘制准备阶段的颜色与参数，
/// 只有 <c>BoringSetup</c> 之后改才对（那时原版把所有颜色字段都算完了）。
/// </summary>
internal sealed class PlayerDrawInfoPipeline : ILoaderModule
{
    public static PlayerDrawInfoPipeline Instance { get; } = new();

    public string Name => "playerdrawinfo";

    private static IModLog Log => LoaderHost.Current.Log;

    private static PlayerPipeline Players => PlayerPipeline.Instance;

    public bool Install()
    {
        // 绘制准备之后改颜色 / 参数；每个玩家各一次。
        On.Terraria.DataStructures.PlayerDrawSet.BoringSetup += BoringSetup;

        // 游戏的层变换之后、进渲染缓存之前。
        On.Terraria.DataStructures.PlayerDrawLayers.DrawPlayer_RenderAllLayers += RenderAllLayers;

        // 整个玩家画完（含相机）；DrawPlayer 钩子的落点。
        On.Terraria.Graphics.Renderers.LegacyPlayerRenderer.DrawPlayerFull += DrawPlayerFull;

        // 缩放；作者要自己的视野倍率时改它。
        On.Terraria.Main.DoDraw_UpdateCameraPosition += UpdateCameraPosition;

        // 逐层：作者可以说某一层别画（VanillaDrawLayer 点名）。
        HideLayerHooks.Install();

        Log.Info("[playerdrawinfo] 已挂（DrawEffects / ModifyDrawInfo / TransformDrawData / DrawPlayer / ModifyScreenPosition）");
        return true;
    }

    /// <summary>
    /// 这一层要不要跳过：问玩家身上的每个 ModPlayer（只问开了 HideDrawLayers 的）。
    /// 没人开这个钩子时是一次 <c>Active</c> 位查询，逐层调用的开销可以忽略。
    /// </summary>
    internal static bool Hidden(Player player, VanillaDrawLayer layer)
    {
        if (player is null) return false;

        var binding = Players.Binding;
        if (!binding.Active(HookId.HideDrawLayers)) return false;

        foreach (var mod in binding.Of(player))
        {
            if (!mod.HookEnabled(HookId.HideDrawLayers)) continue;

            try
            {
                if (mod.HideDrawLayer(player, layer)) return true;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawlayer] {mod.Mod?.Name}.HideDrawLayer 异常：{Safe.Describe(e)}");
            }
        }

        return false;
    }

    // ===================== 钩子 =====================

    private static void BoringSetup(On.Terraria.DataStructures.PlayerDrawSet.orig_BoringSetup orig,
                                    PlayerDrawSet self, Player player, ref Il2CppMicrosoft.Xna.Framework.Vector2 drawPosition,
                                    float shadowOpacity, float rotation,
                                    ref Il2CppMicrosoft.Xna.Framework.Vector2 rotationOrigin,
                                    Projectile overrideHeldProjectile)
    {
        orig(self, player, ref drawPosition, shadowOpacity, rotation, ref rotationOrigin, overrideHeldProjectile);

        if (player is null || !Players.ModsActive(HookId.DrawEffects) && !Players.ModsActive(HookId.ModifyDrawInfo))
            return;

        var mods = Players.ModsOf(player);

        if (Players.ModsActive(HookId.DrawEffects)) Effects(player, self, mods);

        for (var index = 0; index < mods.Length; index++)
        {
            var mod = mods[index];
            if (!mod.HookEnabled(HookId.ModifyDrawInfo)) continue;

            try
            {
                mod.ModifyDrawInfo(player, self);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawinfo] {mod.Mod?.Name}.ModifyDrawInfo 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>r/g/b/a 四个颜色倍率与满亮开关：与原版的 <c>GetImmuneAlpha</c> 叠法一致。</summary>
    private static void Effects(Player player, PlayerDrawSet info, ModPlayer[] mods)
    {
        var r = 1f;
        var g = 1f;
        var b = 1f;
        var a = 1f;
        var fullBright = false;

        for (var index = 0; index < mods.Length; index++)
        {
            var mod = mods[index];
            if (!mod.HookEnabled(HookId.DrawEffects)) continue;

            try
            {
                mod.DrawEffects(player, info, ref r, ref g, ref b, ref a, ref fullBright);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawinfo] {mod.Mod?.Name}.DrawEffects 异常：{Safe.Describe(e)}");
            }
        }

        if (r == 1f && g == 1f && b == 1f && a == 1f && !fullBright) return;

        ApplyTones(player, info, r, g, b, a, fullBright);
    }

    /// <summary>
    /// 倍率写回 <c>PlayerDrawSet</c> 的十四个颜色字段（皮肤、发型、衣服、护甲、坐骑……），
    /// 与原版的免疫透明度叠加保持同一形状；某个字段涂错不会崩，只会这一帧颜色不对。
    /// </summary>
    private static void ApplyTones(Player player, PlayerDrawSet info, float r, float g, float b, float a,
                                   bool fullBright)
    {
        var white = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255, 255);

        info.colorHair = Toned(fullBright ? player.GetHairColor(false) : info.colorHair);
        info.colorEyeWhites = Toned(fullBright ? white : info.colorEyeWhites);
        info.colorEyes = Toned(fullBright ? player.eyeColor : info.colorEyes);
        info.colorHead = Toned(fullBright ? player.skinColor : info.colorHead);
        info.colorBodySkin = Toned(fullBright ? player.skinColor : info.colorBodySkin);
        info.colorLegs = Toned(fullBright ? player.skinColor : info.colorLegs);
        info.colorShirt = Toned(fullBright ? player.shirtColor : info.colorShirt);
        info.colorUnderShirt = Toned(fullBright ? player.underShirtColor : info.colorUnderShirt);
        info.colorPants = Toned(fullBright ? player.pantsColor : info.colorPants);
        info.colorShoes = Toned(fullBright ? player.shoeColor : info.colorShoes);
        info.colorArmorHead = Toned(fullBright ? white : info.colorArmorHead);
        info.colorArmorBody = Toned(fullBright ? white : info.colorArmorBody);
        info.colorArmorLegs = Toned(fullBright ? white : info.colorArmorLegs);
        info.colorMount = Toned(fullBright ? white : info.colorMount);

        Il2CppMicrosoft.Xna.Framework.Graphics.Color Toned(Il2CppMicrosoft.Xna.Framework.Graphics.Color baseColor) =>
            new(Channel(baseColor.R * r), Channel(baseColor.G * g), Channel(baseColor.B * b),
                Channel(baseColor.A * a));
    }

    private static byte Channel(float value) =>
        (byte)Math.Clamp((int)(float.IsFinite(value) ? value : 0f), 0, 255);

    private static void RenderAllLayers(
        On.Terraria.DataStructures.PlayerDrawLayers.orig_DrawPlayer_RenderAllLayers orig, PlayerDrawSet drawinfo,
        Il2CppStructArray<Il2CppMicrosoft.Xna.Framework.Vector2> positionalOffsets)
    {
        var player = drawinfo.drawPlayer;

        if (player is not null && Players.ModsActive(HookId.TransformDrawData))
        {
            var mods = Players.ModsOf(player);

            for (var index = 0; index < mods.Length; index++)
            {
                var mod = mods[index];
                if (!mod.HookEnabled(HookId.TransformDrawData)) continue;

                try
                {
                    mod.TransformDrawData(player, drawinfo);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[playerdrawinfo] {mod.Mod?.Name}.TransformDrawData 异常：{Safe.Describe(e)}");
                }
            }
        }

        orig(drawinfo, positionalOffsets);
    }

    private static void DrawPlayerFull(
        On.Terraria.Graphics.Renderers.LegacyPlayerRenderer.orig_DrawPlayerFull orig, LegacyPlayerRenderer self,
        Camera camera, Player drawPlayer)
    {
        orig(self, camera, drawPlayer);

        if (drawPlayer is null || !Players.ModsActive(HookId.DrawPlayerFull)) return;

        var mods = Players.ModsOf(drawPlayer);

        for (var index = 0; index < mods.Length; index++)
        {
            var mod = mods[index];
            if (!mod.HookEnabled(HookId.DrawPlayerFull)) continue;

            try
            {
                mod.DrawPlayer(drawPlayer, camera);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawinfo] {mod.Mod?.Name}.DrawPlayer 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>
    /// 本地玩家的 <see cref="ModPlayer.ModifyZoom"/> 与 <see cref="ModPlayer.ModifyScreenPosition"/>。
    /// 逐玩家钩子里只有本地玩家的那一份生效（相机的世界只此一份，别人的修改没有意义）。
    /// </summary>
    private static void UpdateCameraPosition(On.Terraria.Main.orig_DoDraw_UpdateCameraPosition orig)
    {
        orig();

        var me = Main.LocalPlayer;
        if (me is null) return;

        var mods = Players.ModsOf(me);

        for (var index = 0; index < mods.Length; index++)
        {
            var mod = mods[index];

            try
            {
                if (mod.HookEnabled(HookId.ModifyScreenPosition)) mod.ModifyScreenPosition(me);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawinfo] {mod.Mod?.Name}.ModifyScreenPosition 异常：{Safe.Describe(e)}");
            }
        }
    }
}
