using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Globals;
using Terraria;
using Pylon.ModLoader.Audio;
using Terraria.GameContent;
using Terraria.ID;

namespace XiheBoss.KingSlime
{
    public sealed class KingSlimeGlobalNpc : GlobalNpc
    {
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) =>
            entity.type == NPCID.KingSlime;

        public override void SetDefaults(NPC npc)
        {
            npc.lifeMax = (int)((float)npc.lifeMax * 1.2f);
        }

        public override bool PreAI(NPC npc)
        {
            var ai = XiheHelper.ReadAi(npc);
            var lai = XiheHelper.ReadLocalAi(npc);

            float scaleMuti = 1f;
            bool flag8 = false;
            bool flag9 = false;
            npc.aiAction = 0;
            if (ai[3] == 0f && npc.life > 0)
            {
                ai[3] = npc.lifeMax;
            }
            if (lai[0] == 0f)
            {
                ai[0] = -100f;
                lai[0] = 1f;
                npc.TargetClosest(true);
                npc.netUpdate = true;
            }
            if (Main.player[npc.target].dead)
            {
                npc.TargetClosest(true);
                if (Main.player[npc.target].dead)
                {
                    npc.timeLeft = 0;
                    npc.direction = Main.player[npc.target].Center.X < npc.Center.X ? 1 : -1;
                }
            }
            if (!Main.player[npc.target].dead && ai[2] >= 350f && ai[1] < 5f && npc.velocity.Y == 0f)
            {
                ai[2] = 0f;
                ai[0] = 0f;
                ai[1] = 5f;
            }
            if (Math.Abs(npc.Top.Y - Main.player[npc.target].Bottom.Y) > 300f)
            {
                ai[2] += 1f;
            }
            if (Vector2.Distance(Main.player[npc.target].Center, npc.Center) > 1000f)
            {
                ai[2] += 1f;
            }
            if (npc.life < npc.lifeMax / 2)
            {
                ai[2] += 0.5f;
            }
            if (ai[1] == 5f)
            {
                flag8 = true;
                npc.aiAction = 1;
                ai[0] += 4f;
                scaleMuti = MathHelper.Clamp((60f - ai[0]) / 60f, 0f, 1f);
                scaleMuti = 0.5f + scaleMuti * 0.5f;
                if (ai[0] >= 60f)
                {
                    flag9 = true;
                }
                if (ai[0] == 60f)
                {
                    Gore.NewGore(null, npc.Center + new Vector2(-40f, -npc.height / 2f),
                        npc.velocity, 734, 1f);
                }
                if (ai[0] >= 60f)
                {
                    Player player = Main.player[Player.FindClosest(npc.position, npc.width, npc.height)];
                    lai[1] = player.Center.X + player.velocity.X * 50f;
                    lai[2] = player.Center.Y - 420f + player.velocity.Y * 20f;
                    npc.Bottom = new Vector2(lai[1], lai[2]);
                    ai[1] = 6f;
                    ai[0] = 0f;
                    npc.netUpdate = true;
                }
                if (!flag9)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Dust obj = Dust.NewDustDirect(npc.position + Vector2.UnitX * -20f, npc.width + 40,
                            npc.height, 4, npc.velocity.X, npc.velocity.Y, 150, new Color(78, 136, 255, 80), 2f);
                        obj.noGravity = true;
                        var dv = obj.velocity;
                        dv *= 0.5f;
                        obj.velocity = dv;
                    }
                }
            }
            else if (ai[1] == 6f)
            {
                flag8 = true;
                npc.aiAction = 0;
                ai[0] += 1f;
                scaleMuti = MathHelper.Clamp(ai[0] / 30f, 0f, 1f);
                scaleMuti = 0.5f + scaleMuti * 0.5f;
                if (ai[0] >= 30f)
                {
                    SoundEngine.PlaySound(SoundID.Item4, npc.Center);
                    ai[1] += 1f;
                    ai[0] = 0f;
                    npc.netUpdate = true;
                    npc.TargetClosest(true);
                }
                for (int j = 0; j < 10; j++)
                {
                    Dust obj2 = Dust.NewDustDirect(npc.position + Vector2.UnitX * -20f, npc.width + 40,
                        npc.height, 4, npc.velocity.X, npc.velocity.Y, 150, new Color(78, 136, 255, 80), 2f);
                    obj2.noGravity = true;
                    var dv2 = obj2.velocity;
                    dv2 *= 2f;
                    obj2.velocity = dv2;
                }
            }
            else if (ai[1] == 7f)
            {
                if ((ai[0] += 1f) < 30f)
                {
                    npc.velocity = Vector2.Zero;
                    for (int k = 0; k < 10; k++)
                    {
                        Dust obj3 = Dust.NewDustDirect(npc.position + Vector2.UnitX * -20f, npc.width + 40,
                            npc.height, 4, npc.velocity.X, npc.velocity.Y, 150, new Color(78, 136, 255, 80), 2f);
                        obj3.noGravity = true;
                        var dv3 = obj3.velocity;
                        dv3 *= 2f;
                        obj3.velocity = dv3;
                    }
                }
                if (ai[0] == 30f)
                {
                    var sv = npc.velocity;
                    sv.Y += 25f;
                    npc.velocity = sv;
                    npc.noGravity = true;
                }
                if (ai[0] >= 30f)
                {
                    bool fall = npc.Center.Y + 60f < Main.player[npc.target].Center.Y;
                    if (npc.velocity.Y == 0f || ai[0] > 100f || npc.velocity != Collision.TileCollision(
                            npc.position, npc.velocity, npc.width + 10, npc.height + 20, fall, fall, 1))
                    {
                        XiheFx.ScreenShake(10f, npc.Center);
                        bool canhit = Collision.CanHitLine(npc.Center, 1, 1, Main.player[npc.target].Center, 1, 1);
                        XiheVisuals.Create(VfxKind.CircleWarp, npc.Center, Vector2.Zero);
                        for (float i2 = 1f; i2 <= 13f; i2 += 2f)
                        {
                            Projectile.NewProjectileDirect(null, npc.Center, XiheHelper.Vxr(-(float)Math.PI / 14f * i2) * 8f,
                                ModContent.ProjectileType<SlimeBall>(), npc.damage / 8, 0f, Main.myPlayer, 0f, 0f, 0f)
                                .tileCollide = canhit;
                        }
                        for (float i3 = 2f; i3 <= 12f; i3 += 2f)
                        {
                            Projectile.NewProjectile(null, npc.Center, XiheHelper.Vxr(-(float)Math.PI / 14f * i3) * 11f,
                                ModContent.ProjectileType<SlimeBall>(), npc.damage / 8, 0f, Main.myPlayer, 0f, 0f, 0f);
                        }
                        for (int l = 0; l <= 20; l++)
                        {
                            var blob = Projectile.NewProjectileDirect(null, npc.Center,
                                -Utils.NextVector2Unit(Main.rand, 0f, 3.141f) * XiheHelper.NextFloat(1f, 5f),
                                605, npc.damage / 8, 0f, Main.myPlayer, 0f, 0f, 0f);
                            blob.tileCollide = false;
                        }
                        npc.noGravity = false;
                        ai[0] = 0f;
                        ai[1] = -1f;
                        npc.netUpdate = true;
                        npc.TargetClosest(true);
                        if (!canhit)
                        {
                            ai[0] = 30f;
                            ai[1] = 5f;
                        }
                    }
                }
            }
            npc.dontTakeDamage = (npc.hide = flag9);
            if (npc.velocity.Y == 0f && ai[1] != 7f)
            {
                if (ai[1] == 0f)
                {
                    if (ai[0] >= 0f)
                    {
                        for (int m = 0; m <= 6; m++)
                        {
                            Projectile.NewProjectile(null, npc.Center, XiheHelper.Vxr(-(float)Math.PI / 6f * (float)m) * 10f,
                                ModContent.ProjectileType<SlimeBall>(), npc.damage / 8, 0f, Main.myPlayer, 0f, 0f, 0f);
                        }
                    }
                    if (ai[0] % 5f == 0f)
                    {
                        Projectile.NewProjectile(null, npc.Center,
                            -Utils.NextVector2Unit(Main.rand, 0f, 3.141f) * XiheHelper.NextFloat(3f, 6f),
                            605, npc.damage / 8, 0f, Main.myPlayer, 0f, 0f, 0f);
                    }
                }
                var gv = npc.velocity;
                gv.X *= 0.85f;
                npc.velocity = gv;
                if (Math.Abs(npc.velocity.X) < 0.5)
                {
                    var gv2 = npc.velocity;
                    gv2.X = 0f;
                    npc.velocity = gv2;
                }
                if (!flag8)
                {
                    if (ai[0] >= 0f)
                    {
                        npc.TargetClosest(true);
                        npc.netUpdate = true;
                        if (ai[1] == 3f)
                        {
                            var v = npc.velocity;
                            v.Y = -16f;
                            v.X += 6f * npc.direction;
                            npc.velocity = v;
                            ai[0] = -120f;
                            ai[1] = 0f;
                        }
                        else if (ai[1] == 2f)
                        {
                            var v = npc.velocity;
                            v.Y = -8f;
                            v.X += 2f * npc.direction;
                            npc.velocity = v;
                            ai[0] = -40f;
                            ai[1] += 1f;
                        }
                        else
                        {
                            var v = npc.velocity;
                            v.Y = -13f;
                            v.X += 3f * npc.direction;
                            npc.velocity = v;
                            ai[0] = -10f;
                            ai[1] -= 1f;
                            if (ai[1] < -1f)
                            {
                                ai[1] = 2f;
                            }
                        }
                    }
                    else if (ai[0] >= -40f)
                    {
                        npc.aiAction = 1;
                    }
                    ai[0] += 2f;
                    if ((double)npc.life < (double)npc.lifeMax * 0.8)
                    {
                        ai[0] += 1f;
                    }
                    if ((double)npc.life < (double)npc.lifeMax * 0.6)
                    {
                        ai[0] += 1f;
                    }
                    if ((double)npc.life < (double)npc.lifeMax * 0.4)
                    {
                        ai[0] += 2f;
                    }
                    if ((double)npc.life < (double)npc.lifeMax * 0.2)
                    {
                        ai[0] += 2f;
                    }
                    if ((double)npc.life < (double)npc.lifeMax * 0.1)
                    {
                        ai[0] += 2f;
                    }
                }
            }
            else if (npc.target < 255 && ((npc.direction == 1 && npc.velocity.X < 3f) ||
                                          (npc.direction == -1 && npc.velocity.X > -3f)) && ai[1] != 7f)
            {
                if ((npc.direction == -1 && npc.velocity.X < 0.1) || (npc.direction == 1 && npc.velocity.X > -0.1))
                {
                    var av = npc.velocity;
                    av.X += 0.2f * npc.direction;
                    npc.velocity = av;
                }
                else
                {
                    var av = npc.velocity;
                    av.X *= 0.93f;
                    npc.velocity = av;
                }
            }
            int num245 = Dust.NewDust(npc.position, npc.width, npc.height, 4, npc.velocity.X, npc.velocity.Y, 255,
                new Color(0, 80, 255, 80), npc.scale * 1.2f);
            Main.dust[num245].noGravity = true;
            var dv245 = Main.dust[num245].velocity;
            dv245 *= 0.5f;
            Main.dust[num245].velocity = dv245;
            if (npc.life > 0 && ai[1] != 7f)
            {
                float num246 = (float)npc.life / (float)npc.lifeMax;
                num246 = num246 * 0.5f + 0.75f;
                num246 *= scaleMuti;
                num246 *= 1.2f;
                if (num246 != npc.scale)
                {
                    var pos = npc.position;
                    pos.X += npc.width / 2;
                    pos.Y += npc.height;
                    npc.position = pos;
                    npc.scale = num246;
                    npc.width = (int)(98f * npc.scale);
                    npc.height = (int)(92f * npc.scale);
                    var pos2 = npc.position;
                    pos2.X -= npc.width / 2;
                    pos2.Y -= npc.height;
                    npc.position = pos2;
                }
                int num247 = (int)((double)npc.lifeMax * 0.1);
                if ((float)(npc.life + num247) < ai[3])
                {
                    ai[3] = npc.life;
                    int num248 = Main.rand.Next(1, 3);
                    for (int n = 0; n < num248; n++)
                    {
                        int x = (int)(npc.position.X + (float)Main.rand.Next(npc.width - 32));
                        int y = (int)(npc.position.Y + (float)Main.rand.Next(npc.height - 32));
                        int num250 = 1;
                        if (Main.rand.Next(4) == 0)
                        {
                            num250 = 535;
                        }
                        int num251 = NPC.NewNPC(null, x, y, num250, 0, 0f, 0f, 0f, 0f, 255);
                        var minion = Main.npc[num251];
                        var mv = minion.velocity;
                        mv.X = (float)Main.rand.Next(-15, 16) * 0.1f;
                        mv.Y = (float)Main.rand.Next(-30, 1) * 0.1f;
                        minion.velocity = mv;
                        var mai = minion.ai;
                        mai[0] = -1000 * Main.rand.Next(3);
                        mai[1] = 0f;
                        minion.ai = mai;
                    }
                }
            }
            XiheHelper.WriteAi(npc, ai[0], ai[1], ai[2], ai[3]);
            XiheHelper.WriteLocalAi(npc, lai[0], lai[1], lai[2]);
            return false;
        }

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, ref Color drawColor)
        {
            var fr = npc.frame;
            if (fr.Width != 0 && fr.Height != 0) return true;
            var tex = Terraria.GameContent.TextureAssets.Npc[npc.type].Value;
            var frames = Main.npcFrameCount[npc.type];
            if (frames <= 0) frames = 1;
            var fallback = new Rectangle(0, 0, tex.Width, tex.Height / frames);
            var alpha = npc.GetAlpha(drawColor);
            var effects = npc.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            XiheDraw.Sprite(spriteBatch, tex, npc.position + new Vector2(npc.width, npc.height) / 2f - screenPos,
                fallback, alpha, 0f, new Vector2(fallback.Width, fallback.Height) / 2f, npc.scale, effects);
            return false;
        }
    }
}
