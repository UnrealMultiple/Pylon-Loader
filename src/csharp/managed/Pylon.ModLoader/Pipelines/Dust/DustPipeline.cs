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
            var origin = new Il2CppSystem.Nullable<Vector2>(new Vector2(texture.Width / 2f, texture.Height / 2f));
            var drawPosition = new Il2CppSystem.Nullable<Vector2>(position);
            var scale = new Il2CppSystem.Nullable<Vector2>(new Vector2(dust.scale, dust.scale));
            var colorNullable = new Il2CppSystem.Nullable<Color>(color);

            spriteBatch.Draw(texture, drawPosition, null, null, origin, dust.rotation, scale, colorNullable, SpriteEffects.None, 0f);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[dust] 绘制异常：" + Safe.Describe(e));
                Live.RemoveAt(index);
                index--;
            }
        }
    }
}