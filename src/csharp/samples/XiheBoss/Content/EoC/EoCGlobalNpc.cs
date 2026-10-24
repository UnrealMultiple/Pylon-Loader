using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Globals;
using Terraria;
using Pylon.ModLoader.Audio;
using Terraria.ID;

namespace XiheBoss.EoC
{
    public sealed class EoCGlobalNpc : GlobalNpc
    {
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) =>
            entity.type == NPCID.EyeofCthulhu;

        public override void SetDefaults(NPC npc)
        {
            npc.lifeMax = (int)((float)npc.lifeMax * 1.5f);
            npc.scale *= 1.1f;
        }

        private static int _pTick;

        public override bool PreAI(NPC npc)
        {
            if (_pTick < 3 || _pTick % 30 == 0)
            {
                XiheBossMod.Log?.Info($"[eoc] PreAI t={_pTick} ai1={npc.ai[1]} day={Main.dayTime}");
            }
            _pTick++;
            var ai = XiheHelper.ReadAi(npc);
            var lai = XiheHelper.ReadLocalAi(npc);

            lai[0] = 0f;
            npc.TargetClosest(true);
            Player player = Main.player[npc.target];
            bool dead = player.dead;
            if (dead)
            {
                npc.TargetClosest(true);
                player = Main.player[npc.target];
                dead = player.dead;
            }
            float npcRot = npc.rotation + 1.57f;
            if (Main.rand.Next(5) == 0)
            {
                int num10 = Dust.NewDust(new Vector2(npc.position.X, npc.position.Y + npc.height * 0.25f),
                    npc.width, (int)(npc.height * 0.5f), 5, npc.velocity.X, 2f, 0, default(Color), 1f);
                var dv = Main.dust[num10].velocity;
                dv.X *= 0.5f;
                dv.Y *= 0.1f;
                Main.dust[num10].velocity = dv;
            }
            if (Main.dayTime || dead)
            {
                if (_pTick < 5) XiheBossMod.Log?.Info($"[eoc] 白天逃离分支 t={_pTick}");
                var fv = npc.velocity;
                fv.Y -= 0.1f;
                npc.velocity = fv;
                RotateTo(npc, Utils.ToRotation(npc.velocity), 0.05f);
                if (npc.timeLeft > 10)
                {
                    npc.timeLeft = 10;
                    XiheHelper.WriteAi(npc, ai[0], ai[1], ai[2], ai[3]);
                    XiheHelper.WriteLocalAi(npc, lai[0], lai[1], lai[2]);
                    return false;
                }
            }
            if (npc.life <= npc.lifeMax * 3 / 4 && ai[1] < 3f)
            {
                ai[3] = 100f;
                ai[1] = 3f;
                ai[2] = 0f;
            }
            if (ai[1] == 0f)
            {
                if (ai[2]++ <= 180f)
                {
                    MoveTo(npc, player.Center + new Vector2(0f, -300f), 5f, 15f);
                    RotateTo(npc, player.Center);
                    if (ai[2] % 70f == 0f)
                    {
                        NPC a = NewNpc(npc.Center, 5);
                        a.velocity = XiheHelper.Vxr(npcRot) * 4f;
                        a.lifeMax *= 2;
                        a.life *= 2;
                        SoundEngine.PlaySound(SoundID.NPCHit1, npc.Center);
                    }
                }
                else
                {
                    ai[1] += 1f;
                    ai[2] = 0f;
                    npc.netUpdate = true;
                }
            }
            if (ai[1] == 1f)
            {
                if (ai[2]++ == 0f)
                {
                    npc.velocity = npc.DirectionTo(player.Center + player.velocity * 25f) * 10f;
                    npc.rotation = Utils.ToRotation(npc.velocity) - 1.57f;
                }
                float num11 = ai[2];
                if (num11 > 40f && num11 < 90f)
                {
                    MoveTo(npc, player.Center, 5f, 50f);
                    RotateTo(npc, player.Center + player.velocity * 28f);
                }
                if (ai[2] == 90f)
                {
                    if ((ai[3] += 1f) < 3f)
                    {
                        ai[2] = 0f;
                    }
                    else
                    {
                        ai[3] = 0f;
                        ai[2] = 0f;
                        ai[1] = 2f;
                    }
                }
            }
            if (ai[1] == 2f)
            {
                if (ai[2] < 230f)
                {
                    MoveTo(npc, player.Center + new Vector2(0f, -400f), 12f, 40f);
                    var cv = npc.velocity;
                    cv.Y -= (float)Math.Cos(ai[2] * 0.1f) * 0.5f;
                    npc.velocity = cv;
                }
                else
                {
                    MoveTo(npc, player.Center + new Vector2(npc.Center.X > player.Center.X ? 300 : -300, 0f), 6f, 10f);
                }
                RotateTo(npc, player.Center);
                if ((ai[2] += 1f) < 180f && ai[2] % 30f == 0f)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        NPC a2 = NewNpc(npc.Center, 5, 1f, npc.whoAmI);
                        a2.velocity = XiheHelper.Vxr(npcRot + i) * 9f;
                        a2.lifeMax *= 4;
                        a2.life *= 4;
                        a2.scale *= 1.2f;
                        SoundEngine.PlaySound(SoundID.NPCHit1, npc.Center);
                    }
                }
                else if (ai[2] == 270f)
                {
                    ai[3] = 100f;
                    SoundEngine.PlaySound(SoundID.ForceRoar, npc.Center);
                    XiheVisuals.Create(VfxKind.CircleWarp, npc.Center, Vector2.Zero);
                    for (int j = 0; j < 30; j++)
                    {
                        Dust obj = Dust.NewDustDirect(npc.Center, 0, 0, 182, 0f, 0f, 0, default(Color), 2.5f);
                        obj.velocity = XiheHelper.Vxr(j * ((float)Math.PI * 2f) / 30f) * 10f;
                        obj.noGravity = true;
                        obj.noLight = true;
                    }
                }
                if (ai[2] > 350f)
                {
                    ai[1] = 0f;
                    ai[2] = (ai[3] = 0f);
                }
            }
            if (ai[1] == 3f)
            {
                var sv = npc.velocity;
                sv *= 0.95f;
                npc.velocity = sv;
                if (ai[0] != 5f)
                {
                    ai[2] += 0.005f;
                }
                else
                {
                    ai[2] -= 0.005f;
                }
                npc.rotation += ai[2];
                if (ai[2] > 0.4f && ai[0] != 5f)
                {
                    ai[0] = 5f;
                    SoundEngine.PlaySound(SoundID.NPCHit1, npc.Center);
                    for (int k = 0; k < 2; k++)
                    {
                        Gore.NewGore(null, npc.position, new Vector2(Main.rand.Next(-30, 31) * 0.2f, Main.rand.Next(-30, 31) * 0.2f), 8, 1f);
                        Gore.NewGore(null, npc.position, new Vector2(Main.rand.Next(-30, 31) * 0.2f, Main.rand.Next(-30, 31) * 0.2f), 7, 1f);
                        Gore.NewGore(null, npc.position, new Vector2(Main.rand.Next(-30, 31) * 0.2f, Main.rand.Next(-30, 31) * 0.2f), 6, 1f);
                    }
                    for (int l = 0; l < 20; l++)
                    {
                        Dust.NewDust(npc.position, npc.width, npc.height, 5, Main.rand.Next(-30, 31) * 0.2f, Main.rand.Next(-30, 31) * 0.2f, 0, default(Color), 1f);
                    }
                    SoundEngine.PlaySound(SoundID.Roar, npc.Center);
                }
                if (ai[2] < 0f)
                {
                    ai[1] += 1f;
                    ai[2] = (ai[3] = 0f);
                }
            }
            if (ai[1] == 4f)
            {
                float t = ai[2];
                int prepareTime = 100 - (int)MathHelper.Lerp(0f, 60f, 1f - (float)npc.life / (float)npc.lifeMax);
                if ((t += 1f) < prepareTime + 50)
                {
                    MoveTo(npc, player.Center + new Vector2(0f, -300f), 7f, 20f);
                    RotateTo(npc, player.Center);
                    if (t == prepareTime)
                    {
                        ai[3] = 0f;
                    }
                }
                if (t == prepareTime + 50)
                {
                    SoundEngine.PlaySound(SoundID.ForceRoar, npc.Center);
                    Vector2 targetPos = player.Center + new Vector2(0f, player.Center.Y > npc.Center.Y ? 300 : -300);
                    npc.velocity = (targetPos - npc.Center) / 20f;
                    if (npc.velocity.Length() < 18f)
                    {
                        npc.velocity = XiheHelper.Normalize2(npc.velocity) * 18f;
                    }
                    if (npc.velocity.Length() > 23f)
                    {
                        npc.velocity = XiheHelper.Normalize2(npc.velocity) * 23f;
                    }
                    var rv = npc.velocity;
                    rv.Y += XiheHelper.NextFloat(-8f, 8f);
                    npc.velocity = rv;
                    npc.rotation = Utils.ToRotation(npc.velocity) - (float)Math.PI / 2f;
                }
                if (t > prepareTime + 50 && t < prepareTime + 70)
                {
                    lai[0] = 1f;
                    if (t % 2f == 0f)
                    {
                        XiheVisuals.Create(VfxKind.LineWarp,
                            npc.Center + Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f) * Main.rand.Next(10, 100),
                            -npc.velocity * 0.5f);
                    }
                }
                if (t >= prepareTime + 70 && t < prepareTime + 80)
                {
                    var hv = npc.velocity;
                    hv *= 0.96f;
                    npc.velocity = hv;
                    RotateTo(npc, player.Center, 0.3f);
                }
                if (t >= prepareTime + 80)
                {
                    if ((ai[3] += 1f) < 5f)
                    {
                        t = prepareTime + 48;
                    }
                    else
                    {
                        if (t < 1.5f * prepareTime + 110f)
                        {
                            MoveTo(npc, player.Center, 7f, 20f);
                            RotateTo(npc, player.Center);
                            Dust obj5 = Dust.NewDustDirect(npc.Center, 15, 15, 5, 0f, 0f, 0, default(Color), 1.5f);
                            obj5.noGravity = true;
                            obj5.velocity = Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f) * 10f;
                        }
                        if (t == (int)(1.5f * prepareTime) + 90)
                        {
                            int counts = 3;
                            if ((float)npc.life < (float)npc.lifeMax * 0.3f)
                            {
                                counts = 5;
                            }
                            for (int m = 0; m < counts; m++)
                            {
                                Vector2 vec = XiheHelper.RotatedBy(npc.DirectionTo(player.Center),
                                    XiheHelper.NextFloatDirection() * 0.2f) * 15f;
                                vec.Y -= 4f;
                                Projectile.NewProjectileDirect(null, npc.Center, vec,
                                    ModContent.ProjectileType<EocProj>(), npc.damage / 5, 0f, Main.myPlayer, 0f, 0f, 0f)
                                    .tileCollide = false;
                                npc.velocity = -vec * 0.5f;
                            }
                            XiheVisuals.Create(VfxKind.CircleWarp, npc.Center, Vector2.Zero);
                        }
                        if (t > 1.5f * prepareTime + 110f)
                        {
                            t = 0f;
                            ai[3] = 0f;
                            if ((float)npc.life < (float)npc.lifeMax * 0.34f)
                            {
                                if (Main.rand.Next(2) == 0)
                                {
                                    ai[1] = 6f;
                                }
                                else
                                {
                                    ai[1] = 5f;
                                    npc.spriteDirection = npc.Center.X < player.Center.X ? 1 : -1;
                                }
                                npc.netUpdate = true;
                            }
                            else if ((float)npc.life < (float)npc.lifeMax * 0.6f)
                            {
                                ai[1] = 5f;
                                npc.spriteDirection = npc.Center.X < player.Center.X ? 1 : -1;
                            }
                        }
                    }
                }
                ai[2] = t;
            }
            if (ai[1] == 5f)
            {
                if ((ai[2] += 1f) < 60f)
                {
                    MoveTo(npc, player.Center + new Vector2(-500 * npc.spriteDirection, -300f), 30f, 15f);
                    RotateTo(npc, player.Center);
                }
                if (ai[2] == 60f)
                {
                    SoundEngine.PlaySound(SoundID.ForceRoar, npc.Center);
                    npc.velocity = npc.DirectionTo(player.Center) * 25f;
                }
                float num11 = ai[2];
                if (num11 > 60f && (num11 < 90f || (num11 > 150f && num11 < 180f)))
                {
                    lai[0] = 1f;
                    if (ai[2] % 3f == 0f)
                    {
                        XiheVisuals.Create(VfxKind.LineWarp,
                            npc.Center + Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f) * Main.rand.Next(10, 100),
                            -npc.velocity * 0.5f);
                    }
                    if (ai[2] % 7f == 0f)
                    {
                        NPC a3 = NewNpc(npc.Center, 5, 1f, npc.whoAmI);
                        a3.velocity = Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f) * 3f;
                        a3.lifeMax *= 4;
                        a3.life *= 4;
                        a3.scale *= 1.2f;
                        SoundEngine.PlaySound(SoundID.NPCHit1, npc.Center);
                    }
                }
                num11 = ai[2];
                if (num11 > 90f && num11 < 150f)
                {
                    MoveTo(npc, player.Center + new Vector2(400 * npc.spriteDirection, -300f), 30f, 15f);
                    RotateTo(npc, player.Center);
                }
                if (ai[2] == 150f)
                {
                    SoundEngine.PlaySound(SoundID.ForceRoar, npc.Center);
                    npc.velocity = npc.DirectionTo(player.Center) * 30f;
                }
                num11 = ai[2];
                if (num11 >= 180f && num11 < 240f)
                {
                    MoveTo(npc, player.Center + new Vector2(npc.Center.X > player.Center.X ? 300 : -300, 0f), 10f, 10f);
                    RotateTo(npc, player.Center);
                }
                if (ai[2] == 240f)
                {
                    ai[2] = 0f;
                    ai[1] = 4f;
                    ai[3] = 100f;
                    SoundEngine.PlaySound(SoundID.ForceRoar, npc.Center);
                    XiheVisuals.Create(VfxKind.CircleWarp, npc.Center, Vector2.Zero);
                    for (int n = 0; n < 30; n++)
                    {
                        Dust obj6 = Dust.NewDustDirect(npc.Center, 0, 0, 182, 0f, 0f, 0, default(Color), 2.5f);
                        obj6.velocity = XiheHelper.Vxr(n * ((float)Math.PI * 2f) / 30f) * 10f;
                        obj6.noGravity = true;
                        obj6.noLight = true;
                    }
                }
            }
            if (ai[1] == 6f)
            {
                if ((ai[2] += 1f) < 40f)
                {
                    MoveTo(npc, player.Center + player.DirectionTo(npc.Center) * 400f, 20f, 30f);
                    RotateTo(npc, Utils.ToRotation(npc.DirectionTo(player.Center)) - 1.57f, 0.026f);
                }
                float num11 = ai[2];
                if (num11 > 40f && num11 < 60f)
                {
                    MoveTo(npc, player.Center + player.DirectionTo(npc.Center) * 420f, 20f, 5f);
                    var dv6 = npc.velocity;
                    dv6 -= XiheHelper.Vxr(npcRot);
                    npc.velocity = dv6;
                    RotateTo(npc, Utils.ToRotation(npc.DirectionTo(player.Center)) - 1.57f, 0.026f);
                }
                if (ai[2] == 60f)
                {
                    SoundEngine.PlaySound(SoundID.ForceRoar, npc.Center);
                }
                num11 = ai[2];
                if (num11 >= 60f && num11 < 160f)
                {
                    lai[0] = 1f;
                    XiheHelper.SpinAI(npc, player.Center, 0.09f);
                    npc.rotation = Utils.ToRotation(npc.velocity) - 1.57f;
                    if (ai[2] % 4f == 0f && ai[2] < 140f)
                    {
                        if (npc.Center.Y > player.Center.Y)
                        {
                            Projectile.NewProjectile(null, npc.Center, Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f),
                                ModContent.ProjectileType<EocProj>(), npc.damage / 5, 0f, Main.myPlayer, 1f, 0f, 0f);
                        }
                        else
                        {
                            Projectile.NewProjectile(null, npc.Center, Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f),
                                ModContent.ProjectileType<EocProj>(), npc.damage / 5, 0f, Main.myPlayer, 0f, 0f, 0f);
                        }
                    }
                }
                num11 = ai[2];
                if (num11 > 160f && num11 < 200f)
                {
                    MoveTo(npc, player.Center + player.DirectionTo(npc.Center) * 300f, 10f, 15f);
                    RotateTo(npc, player.Center);
                }
                if (ai[2] == 200f)
                {
                    ai[2] = 30f;
                    ai[1] = 4f;
                }
            }
            XiheHelper.WriteAi(npc, ai[0], ai[1], ai[2], ai[3]);
            XiheHelper.WriteLocalAi(npc, lai[0], lai[1], lai[2]);
            return false;
        }

        private static int _pDraw;

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, ref Color drawColor)
        {
            if (_pDraw < 3) XiheBossMod.Log?.Info($"[eoc] PreDraw #{_pDraw}");
            _pDraw++;
            var lai = npc.localAI;
            var tex = Terraria.GameContent.TextureAssets.Npc[npc.type].Value;
            if (lai[0] == 1f)
            {
                for (int i = 0; i < 8; i++)
                {
                    var color = npc.GetAlpha(drawColor);
                    color = XiheHelper.MulClamp(color, 1f - i / 8f);
                    color = XiheHelper.MulClamp(color, 0.5f);
                    XiheDraw.Sprite(spriteBatch, tex, npc.oldPos[i] + new Vector2(npc.width, npc.height) / 2f - screenPos,
                        npc.frame, color, npc.rotation, new Vector2(55f, 107f), npc.scale);
                }
            }
            var alpha = npc.GetAlpha(drawColor);
            var fr = npc.frame;
            var frames = Main.npcFrameCount[npc.type];
            if (frames <= 0) frames = 1;
            var fh = tex.Height / frames;
            if (fr.Width == 0 || fr.Height == 0 || fr.Y < 0 || fr.Y + fr.Height > tex.Height)
            {
                fr = new Rectangle(0, 0, tex.Width, fh);
            }
            XiheDraw.Sprite(spriteBatch, tex, npc.position + new Vector2(npc.width, npc.height) / 2f - screenPos,
                fr, alpha, npc.rotation, new Vector2(55f, 107f), npc.scale);
            return false;
        }

        private static NPC NewNpc(Vector2 position, int type, float ai0 = 0f, float ai1 = 0f)
        {
            return Main.npc[NPC.NewNPC(null, (int)position.X, (int)position.Y, type, 0, ai0, ai1, 0f, 0f, 255)];
        }

        private static void RotateTo(NPC npc, float target, float n = 0.2f)
        {
            npc.rotation = XiheHelper.AngleLerp(npc.rotation, target - 1.57f, n);
        }

        private static void RotateTo(NPC npc, Vector2 targetPos, float n = 0.2f)
        {
            npc.rotation = XiheHelper.AngleLerp(npc.rotation, Utils.ToRotation(npc.DirectionTo(targetPos)) - 1.57f, n);
        }

        private static void MoveTo(NPC npc, Vector2 targetPos, float Speed, float n)
        {
            Vector2 targetVec = XiheHelper.Normalize2(targetPos - npc.Center) * Speed;
            npc.velocity = (npc.velocity * n + targetVec) / (n + 1f);
        }
    }
}
