// 层：框架 / Patch —— Projectile.SetDefaults 拦截
//
// 原版 SetDefaults(int) 对 type >= ProjectileID.Count 走"未知类型"分支（最后把 active 置 false，
// NewProjectile 会拿到死弹幕），所以自定义弹幕拦在 On.Terraria.Projectile.SetDefaults：
// 不调 orig（跳过原版），字段清成出厂值、跑 ModProjectile.SetDefaults，再做 finishModProjectile 三件收尾
// （active=1、按 scale 收尺寸、maxPenetrate=penetrate）。
// GlobalProjectile.SetDefaults 原版弹幕也跑（原版与 mod 弹幕共用一条路）。
using System;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>Projectile.SetDefaults 的 mod 类型接管（跳过原版）+ 全类型 Global 分派。</summary>
    internal static class ProjectileSetDefaultsPatch
    {
        /// <summary>自定义弹幕的最后失败原因（给 Mod 侧日志取证）。</summary>
        public static Action<string> Error = _ => { };

        /// <summary>模板初始化期间：只填原版字段（new Projectile + SetDefaults 供 Template），不调 mod 回调。</summary>
        internal static bool SuppressMod;

        private static bool _installed;

        /// <summary>注册期装一次（幂等）。</summary>
        internal static void Install(ModLogger logger)
        {
            if (_installed) return;
            _installed = true;

            On.Terraria.Projectile.SetDefaults += SetDefaults;
        }

        /// <summary>已注册的 mod 弹幕：跳过原版（不调 orig）；其余照跑原版，之后分派 Global。</summary>
        private static void SetDefaults(On.Terraria.Projectile.orig_SetDefaults orig, Projectile self, int type)
        {
            if (type >= 0 && !SuppressMod && ModContent.Find(ContentKind.Projectile, type) != null)   // 只拦已注册
            {
                Run(self, type);   // 末尾自己分派 Global
                return;
            }

            orig(self, type);

            // 原版弹幕也要跑 GlobalProjectile.SetDefaults（mod 弹幕的已在 Run 末尾跑过一次）。
            if (SuppressMod || type < 0) return;
            if (ModContent.Find(ContentKind.Projectile, type) != null) return;
            DispatchGlobals(self);
        }

        /// <summary>实际逻辑（普通静态方法，避开 hook 处理器的内联限制）。</summary>
        private static void Run(Projectile proj, int type)
        {
            try
            {
                proj.type = type;
                ProjectileDefaults.Apply(proj);

                // 实体实例：每个生成的 mod 弹幕有自己的一份 ModProjectile。
                var mod = ProjectileEntities.Rebind(proj);
                if (mod != null)
                {
                    mod.SetDefaults(proj);
                    mod.PostSetDefaults(proj);
                    Finish(proj, mod);
                }

                DispatchGlobals(proj);
            }
            catch (Exception e)
            {
                try { Error?.Invoke($"Projectile.SetDefaults 自定义分支异常：{e.Message}"); } catch { }
            }
        }

        /// <summary>游戏 SetDefaults 末尾那几步，用 mod 的值重做一遍。</summary>
        private static void Finish(Projectile proj, ModProjectile mod)
        {
            proj.type = mod.Type;
            proj.active = true;

            var frames = 1;
            try
            {
                var table = Main.projFrames;
                if (table != null && proj.type >= 0 && proj.type < table.Length && table[proj.type] > 0)
                    frames = table[proj.type];
            }
            catch { }

            // 作者没写宽高时，按贴图尺寸补（多帧贴图按帧数除高）。
            try
            {
                var texture = mod.Texture != null ? mod.Texture.Value : null;
                if (texture != null)
                {
                    Pylon.ModLoader.Textures.PylonTexture.Size(texture, out var texWidth, out var texHeight);
                    if (proj.width <= 0) proj.width = texWidth;
                    if (proj.height <= 0) proj.height = texHeight / frames;
                }
            }
            catch { }

            // SetDefaults_End：尺寸乘 scale；maxPenetrate 跟 penetrate 走（否则穿透弹幕退化成单体）。
            var scale = proj.scale <= 0 ? 1f : proj.scale;
            proj.width = (int)(proj.width * scale);
            proj.height = (int)(proj.height * scale);
            proj.maxPenetrate = proj.penetrate;
        }

        private static void DispatchGlobals(Projectile proj)
        {
            if (proj == null || proj.type <= 0) return;
            if (GlobalProjectileRegistry.All.Count == 0) return;
            // 实体级列表每次都重建：槽位复用/同类型重生不能继承上一发的 AppliesToEntity 结果。
            GlobalProjectileRegistry.Attach(proj, true);
            foreach (var g in GlobalProjectileRegistry.For(proj, "SetDefaults")) g.SetDefaults(proj);
        }
    }
}
