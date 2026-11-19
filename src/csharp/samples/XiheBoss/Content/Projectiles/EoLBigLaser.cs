using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Audio;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{
    public sealed class EoLBigLaser : ModProjectile
    {
        public override string TexturePath => null;

        private const int RingSlots = 6;

        private sealed class RingData
        {
            public int Birth;
            public float Y;
            public float Scale;
            public int Life;
        }

        private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<RingData>> RingTable = new();

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
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

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            var proj = Projectile;

            var ai = proj.ai;
            if (ai[1] == 0f) AddRing(proj, 0f, -0.2f, 210);
        }

        private static void AddRing(Projectile proj, float yOffset, float scale, int life)
        {
            if (!RingTable.TryGetValue(proj.whoAmI, out var list))
                RingTable[proj.whoAmI] = list = new System.Collections.Generic.List<RingData>();
            if (list.Count >= RingSlots) return;
            list.Add(new RingData { Birth = proj.timeLeft, Y = yOffset, Scale = scale, Life = life });
        }

        public override void AI()
        {
            var proj = Projectile;
            proj.rotation = 1.57f;
            var lai = proj.localAI;
            lai[1] = 10000f;
            proj.localAI = lai;

            var ai = proj.ai;
            var mode = ai[1];

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

            if (mode == 1f)
            {

                var ownerNpc = XiheBossNpc.Current;
                if (ownerNpc != null && ownerNpc.NpcAlive)
                {
                    var npc = ownerNpc.NPC;
                    if (npc.type != ModContent.NPCType<XiheBossNpc>())
                    {
                        proj.Kill();
                        return;
                    }

                    proj.Center = npc.Center - new Vector2(0f, 200f);
                }

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

                    XiheSounds.PlayLaser(proj.Center);
                }

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
                    var target = ownerNpc != null && ownerNpc.NpcAlive ? ownerNpc.NPC.target : Main.myPlayer;
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

        }

        private static void SpawnDust(Projectile proj)
        {
            var pos = proj.Center + new Vector2(
                Main.rand.Next(-80, 80) * proj.scale,
                Main.rand.Next(-500, 1200));
            var vel = new Vector2(0f, Main.rand.Next(5, 10));
            var vfx = XiheVisuals.Create(VfxKind.LightDust, pos, vel);
            vfx.DrawColor = Color.Yellow;
        }

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

        public override void OnKill(int timeLeft)
        {
            var proj = Projectile;
            RingTable.Remove(proj.whoAmI);
        }

        public override bool ShouldUpdatePosition() => false;

        public override bool CanDamage() => Projectile.scale > 0.2f;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            var proj = Projectile;

            return Math.Abs(targetHitbox.Center.X - proj.Center.X) < proj.scale * 60f;
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public static void PushVisual(Projectile p)
        {
            if (p.scale <= 0.001f) return;

            var ai = p.ai;
            var color = XiheHelper.HsvToRgb(ai[0] % 360f, 1f, 0.5f);
            var center = p.Center - new Vector2(0f, 2000f);
            var length = p.localAI[1];
            if (length <= 1f) return;

            var tex3W = XiheTextures.WidthOf(Tex.Tex3);
            var tex3H = XiheTextures.HeightOf(Tex.Tex3);
            var tex0W = XiheTextures.WidthOf(Tex.Tex0);
            var tex0H = XiheTextures.HeightOf(Tex.Tex0);
            XiheFx.SpriteRect(center, Tex.Tex3, new Rectangle(0, 0, tex3W * 5, tex3H), color,
                p.rotation, new Vector2(length / 256f / 5f, p.scale),
                new Vector2(tex3W, tex3H) / 2f, additive: true, colorize: true,
                wrapU: true, srcRepeat: 5f, wrapShift: p.timeLeft * 4f / tex3W);
            DrawCore(center, p, color, tex0W, tex0H, 4f);
            DrawCore(center, p, color, tex0W, tex0H, 5f);
            DrawCore(center, p, color, tex0W, tex0H, 8f);

        }

        private static void DrawCore(Vector2 center, Projectile p, Color color, int width, int height, float phase)
        {
            XiheFx.SpriteRect(center, Tex.Tex0, new Rectangle(0, 0, width * 2, height), color,
                p.rotation, new Vector2(p.localAI[1] / 256f / 2f, p.scale),
                new Vector2(width, height) / 2f, additive: true, colorize: true,
                wrapU: true, srcRepeat: 2f, wrapShift: p.timeLeft * phase / width);
        }
    }
}
