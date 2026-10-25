// 大激光（MEAC EoLBigLaser）——源文件：.work/meac/MEAC.Bosses.EoL.Projectiles/EoLBigLaser.cs
//
// ai 约定：
//   ai[0] = 宿主 NPC 下标（模式 1 用；源文件的 public int owner 字段）
//   ai[1] = 模式（0 = 原地蓄力扩散，1 = 跟随 Boss 的竖直主炮）
//
// AI 逐行照抄源文件：
//   模式 0：timeLeft 195..210 涨 scale，<210 撒黄尘，<20 缩 scale；
//   模式 1：跟 Boss（Center = npc.Center - (0,200)），四组 MagicRing 时刻（350/330/320/260）、
//           260 的 RadialGlur2 + 音效、260..65 的 EoLLance 双向喷射（每 16 tick 一组，第二组错半格、
//           伤害用 projectile.damage/2）、240..260 涨 scale、<255 震屏/黄尘/每 20 tick 的 RadialGlur2、
//           <20 缩 scale 并炸 EoLProj（ai0 = Main.npc[owner].target，ai1 = 3，寿命 +20）。
//
// 近似点：
//   · MagicRing 没有移植（原版是 3D 投影的魔法阵）。这里用私有计数器 _ringCount 在完全相同的
//     timeLeft 时刻记录环事件（位置 yOffset / 半径系数 ai0 = 1 + scale / 寿命），再每 tick 用
//     XiheFx.Ring + Tex.MagicRing 重发一次（图元是即时的，必须每帧重发才能持续显示）。
//     半径 = 110*4*(1+scale)（MagicRing.Draw 的 110*scale*ai0，scale 前 20 tick 从 0 涨到 4），
//     宽度 = 10*4*(1+scale)，alpha 按环自身寿命淡出。源的 ai1（Z 倾角）恒为 0，未折算。
//   · 源文件的自定义音效 MEAC/Sounds/Customs/Laser 没有对应资源，只保留 SoundID.Item159。
//   · RadialGlur2 用 XiheFx.RadialGlur(…, additive: true) 近似。
//   · 源 PreDraw 以 Center-(0,2000) 为原点画 tex3/tex0（timeLeft*4 / *5 / *8 三张），这里用
//     两条 4000px 的 XiheFx.Beam（外层 Tex3 半宽 190*scale，内层 Tex0 半宽 90*scale），
//     scroll 按 timeLeft*4 与 timeLeft*8 推。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class EoLBigLaser : ModProjectile
    {
        public override string TexturePath => null;

        // ---------------- MagicRing 的近似：按弹幕 whoAmI 存的环事件表 ----------------
        private const int RingSlots = 6;

        private sealed class RingData
        {
            public int Birth;      // 创建时的 projectile.timeLeft
            public float Y;        // 源 CreateRing 的 yOffset
            public float Scale;    // 源 CreateRing 的 scale（叠加到 ai0）
            public int Life;       // 源 CreateRing 的 timeleft
        }

        /// <summary>whoAmI → 环事件（PushVisual 是静态的，状态只能放静态表）。</summary>
        private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<RingData>> RingTable = new();

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 20;
            proj.height = 20;
            proj.aiStyle = -1;
            proj.hostile = true;
            proj.friendly = false;
            proj.timeLeft = 300;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 0f;
            proj.alpha = 255;
            proj.extraUpdates = 0;
        }

        public override void OnSpawn(Projectile proj)
        {
            // 源 OnSpawn：ai[1] == 0 时 CreateRing(0f, -0.2f, 210)。
            var ai = proj.ai;
            if (ai[1] == 0f) AddRing(proj, 0f, -0.2f, 210);
        }

        /// <summary>记录一个 MagicRing 事件（源 CreateRing 的移植入口）。</summary>
        private static void AddRing(Projectile proj, float yOffset, float scale, int life)
        {
            if (!RingTable.TryGetValue(proj.whoAmI, out var list))
                RingTable[proj.whoAmI] = list = new System.Collections.Generic.List<RingData>();
            if (list.Count >= RingSlots) return;
            list.Add(new RingData { Birth = proj.timeLeft, Y = yOffset, Scale = scale, Life = life });
        }

        public override void AI(Projectile proj)
        {
            proj.rotation = 1.57f;
            var lai = proj.localAI;
            lai[1] = 10000f;
            proj.localAI = lai;

            var ai = proj.ai;
            var mode = ai[1];

            // ---------------- 模式 0 ----------------
            if (mode == 0f)
            {
                var timeLeft = proj.timeLeft;
                if (timeLeft > 195 && timeLeft < 210) proj.scale += 0.2f;

                if (proj.timeLeft < 210)
                {
                    for (var i = 0; i < 2; i++)
                    {
                        SpawnDust(proj);
                        if (XiheHelper.RandBool(3)) SpawnDustCn(proj);
                    }
                }

                if (proj.timeLeft < 20) proj.scale -= 0.15f;
            }

            // ---------------- 模式 1（跟 Boss 的竖直主炮） ----------------
            if (mode == 1f)
            {
                var owner = (int)ai[0];
                if (owner != -1)
                {
                    if (owner < 0 || owner >= Main.npc.Length)
                    {
                        proj.Kill();
                        return;
                    }

                    var npc = Main.npc[owner];
                    if (!npc.active || npc.type != ModContent.NpcType<XiheBossNpc>())
                    {
                        proj.Kill();
                        return;
                    }

                    proj.Center = npc.Center - new Vector2(0f, 200f);
                }

                // 源文件四组 CreateRing 的时刻（timeLeft 350/330/320/260）。
                if (proj.timeLeft == 350) AddRing(proj, 0f, -0.2f, 90);
                if (proj.timeLeft == 330) AddRing(proj, 200f, -0.5f, 70);
                if (proj.timeLeft == 320) AddRing(proj, 400f, 0f, 60);
                if (proj.timeLeft == 260)
                {
                    AddRing(proj, 0f, 0f, 260);
                    AddRing(proj, 200f, -0.3f, 260);
                    AddRing(proj, 400f, 0.2f, 260);

                    XiheFx.RadialGlur(proj.Center, 60, true);
                    SoundEngine.PlaySound(SoundID.Item159, proj.Center);
                }

                // 260..65：向左右两侧喷 EoLLance 雨（ai0=50 色相、ai1=4 模式）。
                var timeLeft2 = proj.timeLeft;
                if (timeLeft2 < 260 && timeLeft2 > 65)
                {
                    var d = (int)(1.2f * (260 - proj.timeLeft));
                    const int dis = 340;

                    if (proj.timeLeft % 16 == 0)
                    {
                        for (var j = -1; j <= 5; j++)
                        {
                            var pos = proj.Center + new Vector2(0f, j * dis - d);
                            NewLance(pos, Vector2.UnitX, 1);
                            NewLance(pos, -Vector2.UnitX, 1);
                        }
                    }

                    if (proj.timeLeft % 16 == 8)
                    {
                        for (var k = -1; k <= 5; k++)
                        {
                            var pos = proj.Center + new Vector2(0f, (k + 0.5f) * dis - d);
                            NewLance(pos, Vector2.UnitX, proj.damage / 2);
                            NewLance(pos, -Vector2.UnitX, proj.damage / 2);
                        }
                    }
                }

                var timeLeft3 = proj.timeLeft;
                if (timeLeft3 > 240 && timeLeft3 < 260) proj.scale += 0.25f;

                if (proj.timeLeft < 255)
                {
                    XiheFx.ScreenShake(10f, proj.Center);
                    if (proj.timeLeft % 20 == 0 && proj.timeLeft > 175)
                        XiheFx.RadialGlur(proj.Center + Vector2.UnitY * 200f, 25, true);

                    for (var i = 0; i < 2; i++)
                    {
                        SpawnDust(proj);
                        if (XiheHelper.RandBool(3)) SpawnDustCn(proj);
                    }
                }

                if (proj.timeLeft < 20)
                {
                    proj.scale -= 0.25f;

                    var d2 = (20 - proj.timeLeft - 4) * 100;
                    var target = owner >= 0 && owner < Main.npc.Length ? Main.npc[owner].target : Main.myPlayer;
                    for (var m = 0; m < 3; m++)
                    {
                        var pos = proj.Center + new Vector2(
                            Main.rand.Next(-20, 20),
                            d2 + Main.rand.Next(-50, 50));
                        var vel = XiheHelper.NextVector2Unit() * 12f;
                        var idx = Projectile.NewProjectile(null, pos, vel,
                            ModContent.ProjectileType<EoLProj>(), proj.damage / 2, 0f,
                            Main.myPlayer, target, 3f, 0f);
                        if (idx >= 0 && idx < Main.projectile.Length)
                            Main.projectile[idx].timeLeft += 20;
                    }
                }
            }

            // 环图元在 PushVisual 里按“年龄”重发（AI 阶段收集的图元会在提交前被清空，不能在这里画）。
        }

        /// <summary>源文件 VisualEffect.Create&lt;LightDust&gt;(...).drawColor = Color.Yellow。</summary>
        private static void SpawnDust(Projectile proj)
        {
            var pos = proj.Center + new Vector2(
                Main.rand.Next(-80, 80) * proj.scale,
                Main.rand.Next(-500, 1200));
            var vel = new Vector2(0f, Main.rand.Next(5, 10));
            var vfx = XiheVisuals.Create(VfxKind.LightDust, pos, vel);
            vfx.DrawColor = Color.Yellow;
        }

        /// <summary>源文件 LightDust_CN（bloomColor = Color.Yellow）。</summary>
        private static void SpawnDustCn(Projectile proj)
        {
            var pos = proj.Center + new Vector2(
                Main.rand.Next(-70, 70) * proj.scale,
                Main.rand.Next(-500, 1200));
            var vel = new Vector2(0f, Main.rand.Next(5, 10));
            var vfx = XiheVisuals.Create(VfxKind.LightDustCn, pos, vel);
            vfx.DrawColor = Color.Yellow;
            vfx.BloomColor = Color.Yellow;
        }

        private static void NewLance(Vector2 pos, Vector2 vel, int damage)
        {
            Projectile.NewProjectile(null, pos, vel, ModContent.ProjectileType<EoLLance>(),
                damage, 0f, Main.myPlayer, 50f, 4f, 0f);
        }

        /// <summary>
        /// MagicRing 的近似：半径/宽度按源 MagicRing.Draw 的 110*scale*ai0 与 10*scale*ai0，
        /// scale 前 20 tick 由 0 涨到 4（SmoothStep 的等效近似）；alpha 在最后 10 tick 淡出。
        /// </summary>
        private static void PushRings(Projectile proj, Projectile from)
        {
            if (!RingTable.TryGetValue(from.whoAmI, out var rings)) return;
            foreach (var ring in rings)
            {
                var age = ring.Birth - from.timeLeft;
                if (age < 0) continue;

                var remain = ring.Life - age;
                if (remain <= 0) continue;

                var grow = MathHelper.Clamp(age / 20f, 0f, 1f);
                var fade = MathHelper.Clamp(remain / 10f, 0f, 1f);
                var s = 4f * grow * fade;
                var ai0 = 1f + ring.Scale;

                var radius = 110f * s * ai0;
                var width = Math.Max(6f, 10f * s * ai0);
                var alpha = 0.8f * grow * fade;

                XiheFx.Ring(from.Center + new Vector2(0f, ring.Y), radius, width,
                    Tex.MagicRing, new Color(255, 234, 128), alpha);
            }
        }

        public override void OnKill(Projectile proj, int timeLeft)
        {
            RingTable.Remove(proj.whoAmI);
        }

        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override bool CanDamage(Projectile proj) => proj.scale > 0.2f;

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            // 源文件：只按 X 距离判定（一根竖直柱）。
            return Math.Abs(targetHitbox.Center.X - proj.Center.X) < proj.scale * 60f;
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        /// <summary>画面：优先由 XiheBigLaserFx（XiheMagic 的 XiheBeam.frag）画；着色器没起来时退回图元版本。</summary>
        public static void PushVisual(Projectile p)
        {
            if (XiheBigLaserFx.Shader != null) return;
            if (p.scale <= 0.001f) return;

            var ai = p.ai;
            var color = XiheHelper.HsvToRgb(ai[0] % 360f, 1f, 0.5f);   // 源 PreDraw 的 color

            var start = p.Center - new Vector2(0f, 2000f);              // 源：Center - (0, 2000)
            var dir = Vector2.UnitY;                                    // 源：rotation = 1.57
            const float length = 4000f;

            var outerScroll = p.timeLeft * 4f / 256f;                   // 源 rect x = timeLeft * 4
            var innerScroll = p.timeLeft * 8f / 256f;                   // 源 rect x = timeLeft * 8

            XiheFx.Beam(start, dir, length, 190f * p.scale, Tex.Tex3, color, 1f,
                outerScroll, 2000f, 0.35f);
            XiheFx.Beam(start, dir, length, 90f * p.scale, Tex.Tex0, color, 1f,
                innerScroll, 2000f, 0.45f);

            // 柱心 + 落点
            XiheFx.Orb(p.Center, 150f * p.scale, Tex.Laser0, color, 0.9f, 1.57f);
            XiheFx.Orb(p.Center + new Vector2(0f, 2000f), 220f * p.scale, Tex.Ball2, color, 0.75f,
                0f, 1.2f, 0.2f);

            PushRings(p, p);
        }
    }
}


