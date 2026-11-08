using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 尘土管线：mod 尘土不进游戏的图集（那是单张的），由本管线自己更新、自己画。
/// 所以序号从 <see cref="DustTable.Base"/> 起，永远不会被交给游戏的图集索引。
/// </summary>
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

    /// <summary>安装期一次：发号 + 静态默认值。</summary>
    internal void Build()
    {
        DustTable.Assign();

        foreach (var dust in DustTable.All) dust.SetStaticDefaults();

        if (DustTable.Count > 0) Log.Info($"[dust] 安装：{DustTable.Count} 种尘土");
    }

    // ===================== 生成 =====================

    /// <summary>生成一个 mod 尘土；返回游戏那个 Dust 实例，作者可以继续改字段。</summary>
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

    // ===================== 更新 =====================

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

            // NaN/Inf 的尘土不会自然消失，画的时候还会把 NaN 递给 SpriteBatch，直接丢掉。
            if (!float.IsFinite(dust.position.X) || !float.IsFinite(dust.position.Y) ||
                !float.IsFinite(dust.velocity.X) || !float.IsFinite(dust.velocity.Y) ||
                !float.IsFinite(dust.scale))
            {
                Live.RemoveAt(index);
                continue;
            }

            // 最小物理：位置 += 速度，速度受重力，尺寸收缩。作者要别的就在 Update 里自己改。
            var velocity = dust.velocity;
            if (!dust.noGravity) velocity.Y += 0.1f;
            velocity *= 0.99f;

            dust.position += velocity;
            dust.velocity = velocity;
            dust.rotation += velocity.X * 0.05f;
            dust.scale *= 0.99f;

            if (dust.scale <= 0.02f || dust.position.Y > Main.screenPosition.Y + Main.screenHeight + 200f)
                Live.RemoveAt(index);
        }
    }

    // ===================== 绘制 =====================

    private static void DrawDust(On.Terraria.Main.orig_DrawDust orig, Main self)
    {
        orig(self);

        if (Live.Count == 0) return;

        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        // orig() 已经把游戏自己的批次 End 掉了；mod 尘土要在自己的批次里画。
        try
        {
            // 照抄 UI 层已验证可用的 8 参 Begin：状态传 null、矩阵传"无值"=单位矩阵，屏幕空间绘制。
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
            try { spriteBatch.End(); } catch { /* 批次已经坏了就别再抛 */ }
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


        for (var index = 0; index < Live.Count; index++)
        {
            var owned = Live[index];

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
            try
            {
                color = owned.Mod.GetAlpha(color);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[dust] {owned.Mod.Name}.GetAlpha 异常：{Safe.Describe(e)}");
            }

            try
            {
                // 用与 XiheBoss 一致的 9 参重载（position:Vector2 / sourceRect:Nullable<Rectangle> / color:Color / scale:float）。
                var frame = new Il2CppSystem.Nullable<Rectangle>(new Rectangle(0, 0, texture.Width, texture.Height));
                var origin = new Vector2(texture.Width / 2f, texture.Height / 2f);

                spriteBatch.Draw(texture, position, frame, color, dust.rotation, origin, dust.scale, SpriteEffects.None, 0f);
            }
            catch (Exception e)
            {
                // 带栈顶一帧：有 .pdb 时能看到文件:行号（定位"哪个参数是空对象"）。
                var stack = e.StackTrace;
                var frame = string.IsNullOrEmpty(stack) ? "" : " | " + stack.Split('\n')[0].Trim();
                ContentErrors.Report("[dust] 绘制异常：" + Safe.Describe(e) + frame);
                Live.RemoveAt(index);
                index--;
            }
        }
    }
}