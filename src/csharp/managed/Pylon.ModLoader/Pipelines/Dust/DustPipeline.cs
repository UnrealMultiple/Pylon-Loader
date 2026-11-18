using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>尘土管线：mod 尘土不进游戏图集，由本管线自己更新、自己画。</summary>
internal sealed class DustPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static DustPipeline Instance { get; } = new();

    public string Name => "dust";

    private static IModLog Log => LoaderHost.Current.Log;

    private sealed class Owned
    {
        public Dust Dust = null!;
        public ModDust Mod = null!;
    }

    private static readonly List<Owned> Live = [];

    public bool Install()
    {
        On.Terraria.Dust.UpdateDust += UpdateDust;
        On.Terraria.Main.DrawDust += DrawDust;

        Log.Info("[dust] 已挂（自己更新 / 自己画，不进游戏图集）");
        return true;
    }

    public void Refresh() => DustTable.Assign();

    internal void Build()
    {
        DustTable.Assign();

        foreach (var dust in DustTable.All) dust.SetStaticDefaults();

        if (DustTable.Count > 0) Log.Info($"[dust] 安装：{DustTable.Count} 种尘土");
    }

    public static Dust? Spawn(ModDust mod, Vector2 position, Vector2 velocity, int alpha = 0, Color? color = null, float scale = 1f)
    {
        if (mod is null || !mod.Registered) return null;

        Dust dust;
        try
        {
            dust = new Dust();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[dust] 生成 {mod.Name} 失败：{Safe.Describe(e)}");
            return null;
        }

        dust.position = position;
        dust.velocity = velocity;
        dust.alpha = alpha;
        dust.color = color ?? Color.White;
        dust.scale = scale;
        dust.active = true;

        try
        {
            mod.OnSpawn(dust);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[dust] {mod.Name}.OnSpawn 异常：{Safe.Describe(e)}");
        }

        Live.Add(new Owned { Dust = dust, Mod = mod });
        return dust;
    }

    private static void UpdateDust(On.Terraria.Dust.orig_UpdateDust orig)
    {
        orig();

        if (Live.Count == 0) return;

        for (var index = Live.Count - 1; index >= 0; index--)
        {
            var owned = Live[index];
            var dust = owned.Dust;

            bool keep;
            try
            {
                keep = owned.Mod.Update(dust);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[dust] {owned.Mod.Name}.Update 异常：{Safe.Describe(e)}");
                keep = false;
            }

            if (!keep)
            {
                Live.RemoveAt(index);
                continue;
            }

            if (!float.IsFinite(dust.position.X) || !float.IsFinite(dust.position.Y) ||
                !float.IsFinite(dust.velocity.X) || !float.IsFinite(dust.velocity.Y) ||
                !float.IsFinite(dust.scale))
            {
                Live.RemoveAt(index);
                continue;
            }

            var velocity = dust.velocity;
            if (!dust.noGravity) velocity.Y += 0.1f;
            velocity *= 0.99f;

            dust.position += velocity;
            dust.velocity = velocity;
            dust.rotation += velocity.X * 0.05f;
            dust.scale *= 0.99f;

            if (owned.Mod.HookEnabled(HookId.MidUpdate))
            {
                bool brake;
                try
                {
                    brake = owned.Mod.MidUpdate(dust);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[dust] {owned.Mod.Name}.MidUpdate 异常：{Safe.Describe(e)}");
                    brake = false;
                }
                if (brake)
                {
                    velocity = dust.velocity;
                    velocity.X /= 0.99f;
                    dust.velocity = velocity;
                }
            }

            if (dust.scale <= 0.02f || dust.position.Y > Main.screenPosition.Y + Main.screenHeight + 200f)
                Live.RemoveAt(index);
        }
    }

    private static void DrawDust(On.Terraria.Main.orig_DrawDust orig, Main self)
    {
        orig(self);

        if (Live.Count == 0) return;

        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        try
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, null, null,
                new Il2CppSystem.Nullable<Matrix>(), true);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[dust] 批次 Begin 异常：" + Safe.Describe(e));
            return;
        }

        try
        {
            DrawLive(spriteBatch);
        }
        finally
        {
            try { spriteBatch.End(); } catch { }
        }
    }

    private static bool _drawLogged;

    private static void DrawLive(SpriteBatch spriteBatch)
    {
        if (!_drawLogged && Live.Count > 0)
        {
            _drawLogged = true;
            var first = Live[0].Dust;
            Log.Info($"[dust] 首次绘制：{Live.Count} 颗；screen={Main.screenPosition}；屏幕={Main.screenWidth}x{Main.screenHeight}；第一颗 world={first.position} scale={first.scale} color={first.color}");
        }

        List<Owned>? skipped = null;
        for (var index = 0; index < Live.Count; index++)
        {
            var owned = Live[index];
            if (!owned.Mod.HookEnabled(HookId.PreDraw)) continue;

            bool draw;
            try
            {
                draw = owned.Mod.PreDraw(owned.Dust);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[dust] {owned.Mod.Name}.PreDraw 异常：{Safe.Describe(e)}");
                draw = true;
            }
            if (!draw)
            {
                skipped ??= [];
                skipped.Add(owned);
            }
        }

        for (var index = 0; index < Live.Count; index++)
        {
            var owned = Live[index];
            if (skipped is not null && skipped.Contains(owned)) continue;

            Texture2D? texture;
            try
            {
                texture = owned.Mod.Texture?.Value;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[dust] {owned.Mod.Name}.Texture 异常：{Safe.Describe(e)}");
                continue;
            }

            if (texture is null || texture.Pointer == IntPtr.Zero) continue;

            var dust = owned.Dust;
            var position = dust.position - Main.screenPosition;
            var color = dust.color;
            if (owned.Mod.HookEnabled(HookId.GetAlpha))
            {
                try
                {
                    if (owned.Mod.GetAlpha(dust, color) is { } mine) color = mine;
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[dust] {owned.Mod.Name}.GetAlpha 异常：{Safe.Describe(e)}");
                }
            }

            try
            {
                var frame = new Il2CppSystem.Nullable<Rectangle>(new Rectangle(0, 0, texture.Width, texture.Height));
                var origin = new Vector2(texture.Width / 2f, texture.Height / 2f);

                spriteBatch.Draw(texture, position, frame, color, dust.rotation, origin, dust.scale, SpriteEffects.None, 0f);
            }
            catch (Exception e)
            {
                var stack = e.StackTrace;
                var frame = string.IsNullOrEmpty(stack) ? "" : " | " + stack.Split('\n')[0].Trim();
                ContentErrors.Report("[dust] 绘制异常：" + Safe.Describe(e) + frame);
                Live.RemoveAt(index);
                index--;
            }
        }
    }
}