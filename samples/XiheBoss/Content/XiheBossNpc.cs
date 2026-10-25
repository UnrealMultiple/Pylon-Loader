// 羲和（MEAC.Bosses.EoL.XiHe 的 1:1 复刻）。
//
// AI 完全照 MEAC 的状态机抄：timerM 绝对时钟 + 23 个 State + SwitchState 的随机分支，
// 每一段的生成物、节拍（51.5 tick/拍）、伤害（npc.damage / N）都与原版一致。
// 画面：身体/翅膀/手臂用原版贴图（NPC_636 + Extra[157..160]）在 PreDraw 里画，
// 加色元素（场地环、残影、光晕）走 XiheFx 的屏幕着色器。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class XiheBossNpc : ModNpc
    {
        internal enum State
        {
            Spawn, Roar, Delay,
            EoLProjShoot1, EoLProjShoot2,
            SunDance1, SunDance2,
            Laser1, VLaser,
            Lance1, Lance2,
            Dash1, Dash2, DashSpin,
            CrossRay, Prepare, Prepare2,
            Speacial1, Speacial2, Transition, Speacial3, Speacial4,
            Leave,
        }

        internal static XiheBossNpc Current;

        internal static Texture2D BodyTexture;
        internal static Texture2D WingTexture;
        internal static Texture2D WingGlowTexture;
        internal static Texture2D ArmTexture;
        internal static Texture2D ArmGlowTexture;
        internal static Texture2D GlowTexture;
        internal static Texture2D Glow2Texture;

        private static int _musicSlot = -1;

        // ---- 状态机字段（对应 MEAC 的 timerM / t / ai[2] / ai[3] / phase / alpha ...）----
        internal int TimerM;
        private int _t;
        private int _phase = 1;
        private float _ai2;
        private float _ai3;
        private float _alpha;
        private float _setAreaRadius;
        private float _realAreaRadius;
        private Vector2 _areaCenter;
        private Vector2 _oldPosition = Vector2.UnitX;
        // MEAC 的 act/actTimer：源码里 act 只在 OnSpawn 设过 1，绘制固定取手臂第 1 帧，故只留注释。
        private const int Act = 1;
        private bool _spawned;

        internal bool GrayScale;
        internal State BossState = State.Spawn;

        internal bool NpcAlive => NPC != null && NPC.active;
        internal Vector2 Center => NPC != null ? NPC.Center : Vector2.Zero;
        internal float Alpha => _alpha;
        internal Vector2 AreaCenter => _areaCenter;
        internal float RealAreaRadius => _realAreaRadius;
        internal int Phase => _phase;

        public override string DisplayName => "羲和";
        public override int Music => _musicSlot;
        public override int SceneEffectPriority => 6;
        public override bool HideFromBestiary => true;

        // 贴图：PostSetupContent 里指向原版光之女皇的贴图槽（原版素材，不复制美术）。
        public override string TexturePath => "assets/textures/Ball.png";

        internal static int MusicSlot => _musicSlot;

        internal static void ResolveMusic(Mod mod)
        {
            try { _musicSlot = MusicLoader.GetMusicSlot(mod, "Music/TakeFlight"); }
            catch { _musicSlot = -1; }
            if (_musicSlot <= 0)
            {
                try { _musicSlot = MusicLoader.GetMusicSlot("Music/TakeFlight"); }
                catch { }
            }
            XiheBossMod.Log?.Info($"[music] TakeFlight slot = {_musicSlot}");
        }

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 2;
        }

        public override void SetDefaults(NPC npc)
        {
            npc.noGravity = true;
            npc.width = 100;
            npc.height = 50;
            npc.aiStyle = -1;
            npc.damage = 50;
            npc.defense = 50;
            npc.lifeMax = 17000;
            npc.HitSound = SoundID.NPCHit1;
            npc.DeathSound = SoundID.NPCDeath65;
            npc.knockBackResist = 0f;
            npc.value = 250000f;
            npc.noTileCollide = true;
            npc.boss = true;
            npc.Opacity = 1f;
            npc.dontTakeDamage = true;
        }

        public override void PostSetupContent()
        {
            BodyTexture = LoadGameTexture("Images/NPC_" + NPCID.HallowBoss, "boss 身体");
            WingTexture = LoadGameTexture("Images/Extra_157", "翅膀底");
            ArmTexture = LoadGameTexture("Images/Extra_158", "左手");
            WingGlowTexture = LoadGameTexture("Images/Extra_159", "翅膀光");
            ArmGlowTexture = LoadGameTexture("Images/Extra_160", "右手");
            GlowTexture = LoadGameTexture("Images/Extra_187", "外圈光点");
            Glow2Texture = LoadGameTexture("Images/Extra_188", "内圈光点");
        }

        /// <summary>
        /// 原版贴图：不走 TextureAssets 表（在 mod 的晚阶段可能还是空 Asset），
        /// 直接向 Main.Assets 要立即加载的那份，拿不到再退回表。
        /// </summary>
        private static Texture2D LoadGameTexture(string path, string label)
        {
            try
            {
                var asset = Main.Assets.Request<Texture2D>(path, ReLogic.Content.AssetRequestMode.ImmediateLoad);
                var tex = asset?.Value;
                if (tex != null)
                {
                    XiheBossMod.Log?.Info($"[boss] 贴图就绪：{label} {path} {tex.Width}x{tex.Height}");
                    return tex;
                }
            }
            catch (Exception e)
            {
                XiheBossMod.Log?.Warn($"[boss] 贴图 {label} 加载失败（{path}）：{e.Message}");
            }
            return null;
        }

        // ================= 生成 / 死亡 =================

        public override void OnSpawn(NPC npc, Terraria.DataStructures.IEntitySource source)
        {
            Current = this;
            _spawned = true;
            // act = 1（源码同值；手臂帧固定第 1 帧）
            BossState = State.Spawn;
            _areaCenter = npc.Center;
            _setAreaRadius = 10000f;
            _realAreaRadius = 10000f;
            _alpha = 0f;
            TimerM = 0;
            _t = 0;

            XiheFx.CancelScreenPosition();
            XiheFx.RadialGlur(npc.Center, 30, false);
            if (_musicSlot > 0) MusicLoader.RestartMusic(_musicSlot);
            XiheBossMod.Log?.Info("[boss] 羲和已生成");
        }

        public override void ApplyDifficultyAndPlayerScaling(NPC npc, int numPlayers, float balance, float bossAdjustment)
        {
            npc.lifeMax = 100000;
            if (Main.expertMode) npc.lifeMax = 120000;
            if (Main.masterMode) npc.lifeMax = 150000;
            npc.lifeMax = (int)(bossAdjustment * npc.lifeMax);
        }

        public override bool CheckDead(NPC npc)
        {
            if (BossState != State.Leave)
            {
                npc.life = 1;
                npc.dontTakeDamage = true;
                return false;
            }
            return true;
        }

        public override void OnKill(NPC npc)
        {
            Current = null;
            XiheFx.CancelScreenPosition();
            XiheVisuals.Clear();
        }

        public override void HitEffect(NPC npc, int hitDirection, double damage)
        {
            // MEAC OnHit()：11451~11500 锁血；11501~11529 转 Speacial4。
            if (TimerM > 11451 && TimerM < 11500) npc.dontTakeDamage = true;
            if (TimerM > 11500 && TimerM < 11530)
            {
                GrayScale = true;
                XiheFx.ScreenShake(30f, npc.Center);
                XiheFx.RadialGlur(npc.Center, 40, true);
                npc.dontTakeDamage = true;
                SoundEngine.PlaySound(SoundID.Item160, npc.Center);
                BossState = State.Speacial4;
                _t = 0;

                for (var i = 0; i < Main.projectile.Length; i++)
                {
                    var p = Main.projectile[i];
                    if (p != null && p.active && p.type == ModContent.ProjectileType<Cycloid>()) p.Kill();
                }
            }
        }

        public override void SendExtraAI(NPC npc, NetWriter writer)
        {
            writer.WriteInt32(TimerM);
            writer.WriteVector2(_areaCenter);
            writer.WriteInt32(_phase);
            writer.WriteInt32((int)BossState);
            writer.WriteInt32(_t);
            writer.WriteSingle(_ai2);
            writer.WriteSingle(_ai3);
        }

        public override void ReceiveExtraAI(NPC npc, NetReader reader)
        {
            TimerM = reader.ReadInt32();
            _areaCenter = reader.ReadVector2();
            _phase = reader.ReadInt32();
            BossState = (State)reader.ReadInt32();
            _t = reader.ReadInt32();
            _ai2 = reader.ReadSingle();
            _ai3 = reader.ReadSingle();
        }

        public override void BossHeadSlot(NPC npc, ref int index) => index = -1;

        /// <summary>源文件 CheckActive = false：离玩家再远也不消失。</summary>
        public override bool CheckActive(NPC npc) => false;

        // ================= AI =================

        public override void AI(NPC npc)
        {
            if (!_spawned) OnSpawn(npc, null);

            if (TimerM < 1650 && TimerM % 5 == 0) npc.GivenName = XiheHelper.RandomString(15);

            _setAreaRadius = 2000f;
            _areaCenter = Vector2.Lerp(_areaCenter, npc.Center, 0.05f);
            if (Main.raining)
            {
                try { Main.StopRain(true); } catch { }
            }

            TimerM++;
            npc.TargetClosest(true);
            var player = Main.player[npc.target];
            if (player.dead)
            {
                npc.TargetClosest(true);
                player = Main.player[npc.target];
                if (player.dead) BossState = State.Leave;
            }

            if (!Main.dayTime) Main.dayTime = true;
            if (npc.life > 10)
            {
                Main.time = MathHelper.Lerp((float)Main.time,
                    MathHelper.Lerp(2000f, 53700f, 1f - (float)npc.life / npc.lifeMax), 0.005f);
            }

            _oldPosition = npc.Center;

            if (BossState == State.Spawn) { AI_Spawn(npc, player); }
            if (BossState == State.Roar) { AI_Roar(npc, player); }
            if (BossState == State.Delay) { AI_Delay(npc, player); }
            if (BossState == State.EoLProjShoot1) { AI_ProjShoot1(npc, player); }
            if (BossState == State.EoLProjShoot2) { AI_ProjShoot2(npc, player); }
            if (BossState == State.VLaser) { AI_VLaser(npc, player); }
            if (BossState == State.Dash1) { AI_Dash1(npc, player); }
            if (BossState == State.Lance2) { AI_Lance2(npc, player); }
            if (BossState == State.DashSpin) { AI_DashSpin(npc, player); }
            if (BossState == State.SunDance1) { AI_SunDance1(npc, player); }
            if (BossState == State.SunDance2) { AI_SunDance2(npc, player); }
            if (BossState == State.Lance1) { AI_Lance1(npc, player); }
            if (BossState == State.Dash2) { AI_Dash2(npc, player); }
            if (BossState == State.CrossRay) { AI_CrossRay(npc, player); }
            if (BossState == State.Prepare) { AI_Prepare(npc, player); }
            if (BossState == State.Speacial1) { AI_Speacial1(npc, player); }
            if (BossState == State.Speacial2) { AI_Speacial2(npc, player); }
            if (BossState == State.Transition) { AI_Transition(npc, player); }
            if (BossState == State.Prepare2) { AI_Prepare2(npc, player); }
            if (BossState == State.Speacial3) { AI_Speacial3(npc, player); }
            if (BossState == State.Speacial4) { AI_Speacial4(npc, player); }
            if (BossState == State.Leave) { AI_Leave(npc, player); }

            // 尾段：移动残影 + 场地半径收缩 + 玩家关在场内。
            if (Vector2.Distance(npc.Center + npc.velocity, _oldPosition) > 15f && XiheHelper.RandBool(3))
            {
                var vec = npc.Center + npc.velocity - _oldPosition;
                var line = XiheVisuals.Create(VfxKind.LineWarp,
                    npc.Center - npc.velocity + XiheHelper.NextVector2Unit() * 80f, vec / 2f);
                line.Scale = 1.5f;
            }

            XiheHelper.LerpValue(ref _realAreaRadius, _setAreaRadius, 0.03f);
            if (Vector2.Distance(player.Center, _areaCenter) > _realAreaRadius)
            {
                player.Center = Vector2.Lerp(player.Center,
                    _areaCenter + XiheHelper.DirectionTo(_areaCenter, player.Center) * _realAreaRadius, 0.2f);
            }

            // 把状态写回 npc.ai，保持原生同步包/调试工具看到的口径（MEAC 的 ai[0..3] 就是这个）。
            XiheHelper.WriteAi(npc, (float)BossState, _t, _ai2, _ai3);
        }

        // ---------------- Spawn ----------------
        private void AI_Spawn(NPC npc, Player player)
        {
            GrayScale = true;
            var center = npc.Center;
            var dmg5 = npc.damage / 5;

            if (TimerM < XiheHelper.BeatTime(8))
            {
                var f = 18 - TimerM / 50;
                if (f < 1) f = 1;
                if (TimerM % f == 0)
                {
                    var r = XiheHelper.NextVector2Unit();
                    var p = Spawn(npc, player.Center + r * Main.rand.Next(800, 1000),
                        -XiheHelper.RotateRandom(r, 0.2f) * Main.rand.Next(7, 10), ModContent.ProjectileType<CrystalArrow>(),
                        dmg5, 100f, 50f);
                    if (p != null) p.timeLeft += 100;
                }
            }
            else if (TimerM < XiheHelper.BeatTime(16))
            {
                if (TimerM % 20 == 0)
                {
                    var r2 = XiheHelper.NextVector2Unit();
                    Spawn(npc, player.Center + r2 * Main.rand.Next(300, 400), Vector2.Zero,
                        ProjectileID.FairyQueenLance, dmg5, (float)Math.Atan2(-r2.Y, -r2.X), 50f);
                }
                if (TimerM % 60 == 0)
                {
                    var pos = player.Center + XiheHelper.NextVector2Unit() * Main.rand.Next(500);
                    var r3 = XiheHelper.NextVector2Unit();
                    Spawn(npc, pos + r3 * 400f, Vector2.Zero, ProjectileID.FairyQueenLance, dmg5,
                        (float)Math.Atan2(-r3.Y, -r3.X), 50f);
                    var r3b = XiheHelper.RotatedBy(r3, 1.5700001f);
                    Spawn(npc, pos + r3b * 400f, Vector2.Zero, ProjectileID.FairyQueenLance, dmg5,
                        (float)Math.Atan2(-r3.Y, -r3.X) + 1.57f, 50f);
                }
            }
            else if (TimerM < XiheHelper.BeatTime(24))
            {
                npc.Center = Vector2.Lerp(npc.Center, player.Center - new Vector2(0f, 300f), 0.1f);
                npc.velocity = Vector2.Zero;
                if (TimerM % 10 == 0 && XiheHelper.RandBool(10))
                {
                    XiheVisuals.Create(VfxKind.EolEff, center, Vector2.Zero).Ai0 = 1f;
                }
                if (TimerM % 4 == 0)
                {
                    var p2 = Spawn(npc, npc.Center + XiheHelper.NextVector2Unit() * 50f,
                        new Vector2(Main.rand.Next(-10, 10), -Main.rand.Next(5, 10)),
                        ModContent.ProjectileType<CrystalArrow>(), dmg5, 3f, 50f);
                    if (p2 != null) p2.timeLeft += 150;
                }
            }
            else if (TimerM < XiheHelper.BeatTime(32) - 1)
            {
                if (TimerM % 10 == 0 && XiheHelper.RandBool(10))
                {
                    XiheVisuals.Create(VfxKind.EolEff, center, Vector2.Zero).Ai0 = 1f;
                }
                npc.velocity = Vector2.Zero;
                if (TimerM < XiheHelper.BeatTime(28))
                {
                    if (TimerM % 20 == 0)
                    {
                        var r4 = XiheHelper.NextVector2Unit();
                        var p3 = Spawn(npc, player.Center + r4 * Main.rand.Next(800, 1000),
                            -XiheHelper.RotateRandom(r4, 0.2f) * Main.rand.Next(7, 10),
                            ModContent.ProjectileType<CrystalArrow>(), dmg5, 100f, 50f);
                        if (p3 != null) p3.timeLeft += 100;
                    }
                    npc.Center = Vector2.Lerp(npc.Center, player.Center - new Vector2(0f, 450f), 0.05f);
                    if (TimerOnBeat)
                    {
                        var pos2 = player.Center;
                        var r5 = XiheHelper.NextVector2Unit();
                        Spawn(npc, pos2 + r5 * 400f, Vector2.Zero, ProjectileID.FairyQueenLance, dmg5,
                            (float)Math.Atan2(-r5.Y, -r5.X), 50f);
                        var r5b = XiheHelper.RotatedBy(r5, 1.5700001f);
                        Spawn(npc, pos2 + r5b * 400f, Vector2.Zero, ProjectileID.FairyQueenLance, dmg5,
                            (float)Math.Atan2(-r5.Y, -r5.X) + 1.57f, 50f);
                    }
                }
                else
                {
                    XiheFx.UseScreenPosition((npc.Center + player.Center) / 2f, 0.01f);
                    if (TimerOnBeat)
                    {
                        _ai2 += 1f;
                        for (var i = 0; i < 8; i++)
                        {
                            var r6 = XiheHelper.Vxr(i * (float)Math.PI * 2f / 8f);
                            var start = npc.Center + r6 * (1500f - _ai2 * 200f);
                            Spawn(npc, start, XiheHelper.RotatedBy(-r6, _ai2 * 0.2f),
                                ModContent.ProjectileType<EoLLance>(), dmg5, 50f, 0f);
                            Spawn(npc, npc.Center + XiheHelper.RotatedBy(r6, 0.2f) * (1500f - _ai2 * 200f),
                                XiheHelper.RotatedBy(-r6, -_ai2 * 0.2f), ModContent.ProjectileType<EoLLance>(), dmg5, 50f, 0f);
                        }
                    }
                }
            }

            if (TimerM == XiheHelper.BeatTime(32)) SwitchState();
        }

        // ---------------- Roar ----------------
        private void AI_Roar(NPC npc, Player player)
        {
            XiheHelper.LerpValue(ref _alpha, 1f, 0.1f);

            if (TimerM == 1650)
            {
                GrayScale = false;
                npc.GivenName = "羲和";
                Sound(0);
                XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                XiheFx.RadialGlur(npc.Center, 25, false);
                npc.dontTakeDamage = false;
                XiheFx.Brightness(1.5f, 30);

                for (var j = 0; j < 30; j++)
                {
                    Spawn(npc, npc.Center,
                        XiheHelper.Vxr(j * (float)Math.PI * 2f / 30f) * (6 + 2 * (j % 3)),
                        ModContent.ProjectileType<EoLProj>(), npc.damage / 5, 0f, 0f);
                }
            }
            if (TimerM == XiheHelper.BeatTime(33)) XiheFx.RadialGlur(npc.Center, 50, true);

            if (TimerM <= XiheHelper.BeatTime(35))
            {
                XiheFx.ScreenShake(10f, npc.Center);
                XiheFx.UseScreenPosition((npc.Center + player.Center) / 2f, 0.01f);
                if (TimerM % 15 == 0) XiheVisuals.Create(VfxKind.RingWarp, npc.Center, Vector2.Zero);
            }
            else
            {
                SwitchState();
            }
        }

        // ---------------- Delay ----------------
        private void AI_Delay(NPC npc, Player player)
        {
            if (++_t < 80) XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -250f), 20f, 40f);
            else SwitchState();
        }

        // ---------------- EoLProjShoot1 ----------------
        private void AI_ProjShoot1(NPC npc, Player player)
        {
            var delay = _phase >= 2 ? 10 : 20;
            if (++_t == 30 + delay)
            {
                SoundEngine.PlaySound(SoundID.Item164, npc.Center);
                _ai3 = Main.rand.Next(2);
            }

            if (_t < 30)
            {
                StraightMove30(npc, player, 250f, 125f);
            }
            else if (_t < 100 + delay)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -250f), 8f, 10f);
                _ai2 += 0.1f;

                var f2 = 4;
                var c = 3;
                if (_phase == 2) { f2 = 3; c = 3; }
                if (_phase == 3) { f2 = 3; c = 4; }

                if (_t > 50 && _t % f2 == 0)
                {
                    for (var k = 0; k < c; k++)
                    {
                        Spawn(npc, npc.Center,
                            (5f - (_t - 40f) / 40f) * XiheHelper.Vxr(_ai2 + k * (float)Math.PI * 2f / c),
                            ModContent.ProjectileType<EoLProj>(), npc.damage / 5, 0f, 1f + _ai3);
                    }
                }
            }
            else if (_t < 135 + delay)
            {
                npc.velocity *= 0.9f;
            }

            if (_t > 135 + delay)
            {
                SwitchState();
                _ai2 = 0f;
            }
        }

        // ---------------- EoLProjShoot2 ----------------
        private void AI_ProjShoot2(NPC npc, Player player)
        {
            var delay = _phase >= 2 ? 10 : 20;
            if (++_t == 30 + delay) SoundEngine.PlaySound(SoundID.Item164, npc.Center);

            if (_t < 30)
            {
                StraightMove30(npc, player, 150f, 100f);
            }
            else if (_t < 100 + delay)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -250f), 8f, 0.1f);

                if (_phase == 1)
                {
                    if (_t > 50 && _t % 2 == 0)
                    {
                        var vel = -XiheHelper.RotateRandom(Vector2.UnitY, 1.2f);
                        Spawn(npc, npc.Center, vel * Main.rand.Next(7, 13), ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 5, npc.target, 3f);
                    }
                }
                else if (_phase == 2)
                {
                    if (_t > 50 && _t % 2 == 0)
                    {
                        var vel2 = -XiheHelper.RotateRandom(
                            XiheHelper.VerticalVec(XiheHelper.DirectionTo(npc.Center, player.Center)), 1.0f);
                        Spawn(npc, npc.Center, vel2 * Main.rand.Next(8, 15), ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 5, npc.target, 3f);
                        vel2 *= -1f;
                        Spawn(npc, npc.Center, vel2 * Main.rand.Next(8, 15), ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 5, npc.target, 3f);
                    }
                }
                else if (_phase == 3 && _t > 40 && _t % 3 == 0)
                {
                    for (var l = 0; l < 3; l++)
                    {
                        var rot = (float)Math.Atan2(player.Center.Y - npc.Center.Y, player.Center.X - npc.Center.X);
                        var vel3 = -XiheHelper.RotateRandom(
                            XiheHelper.Vxr(rot + (l + 0.5f) * (float)Math.PI * 2f / 3f), 0.5f);
                        Spawn(npc, npc.Center, vel3 * Main.rand.Next(8, 17), ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 5, npc.target, 3f);
                    }
                }
            }
            else if (_t == 100 + delay)
            {
                var c2 = 33;
                if (_phase == 2) c2 = 36;
                if (_phase == 3) c2 = 45;
                for (var m = 0; m < c2; m++)
                {
                    Spawn(npc, npc.Center,
                        XiheHelper.Vxr(m * (float)Math.PI * 2f / c2) * (6 + 2 * (m % 3)),
                        ModContent.ProjectileType<EoLProj>(), npc.damage / 5, 0f, 0f);
                }
            }
            else if (_t < 130 + delay)
            {
                npc.velocity *= 0.9f;
            }

            if (_t > 130 + delay) SwitchState();
        }

        // ---------------- VLaser ----------------
        private void AI_VLaser(NPC npc, Player player)
        {
            if (++_t < 30) StraightMove30(npc, player, 200f, 50f);

            if (_t >= 30 && _t < 60)
            {
                if (_t < 50) { var vy = npc.velocity.Y; XiheHelper.LerpValue(ref vy, -20f, 0.04f); npc.velocity = new Vector2(npc.velocity.X, vy); }
                else npc.velocity *= 0.97f;

                var dis = 100f;
                var ai2 = 2f;
                if (_phase == 2) { ai2 = 1f; dis = 100f; }
                if (_phase == 3) { ai2 = 1f; dis = 90f; }

                if (_t % 2 == 0)
                {
                    Spawn(npc, npc.Center + new Vector2((_t - 30) * dis + 20f, -1000f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, ai2);
                    Spawn(npc, npc.Center + new Vector2(-(_t - 30) * dis - 20f, -1000f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, ai2);
                }
            }

            if (_t == 30 + (_phase >= 2 ? 45 : 60))
            {
                XiheFx.ScreenShake(20f, npc.Center);
                XiheVisuals.Create(VfxKind.RingWarp, npc.Center, Vector2.Zero);
                var wave = XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                wave.Ai0 -= 0.4f;
            }

            if (_t > 60) npc.velocity *= 0.9f;
            if (_t > 40 + (_phase >= 2 ? 45 : 60)) SwitchState();
        }

        // ---------------- Dash1 ----------------
        private void AI_Dash1(NPC npc, Player player)
        {
            var t1 = _phase == 3 ? 10 : 20;

            if (++_t <= 30)
            {
                StraightMove30(npc, player, 100f, 50f);
                if (_t == 30) _ai2 = npc.Center.X > player.Center.X ? 1 : -1;
            }
            else if (_t <= 50 + t1)
            {
                if (_t == 50) SoundEngine.PlaySound(SoundID.Item160, npc.Center);
                var destination = player.Center + new Vector2(_ai2 * 900f, player.velocity.Y * 10f);
                var speed = MathHelper.Clamp(player.velocity.Length() * 2f, 20f, 40f) * (_phase == 3 ? 1.5f : 1f);
                XiheHelper.MoveTo(npc, destination, speed, 10f);

                if (_t % 5 == 0)
                {
                    var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero, 0f, 1f, npc.whoAmI);
                    eff.Golden = true;
                }
            }
            else if (_t < 110 + t1)
            {
                var f3 = _phase != 1 ? 3 : 4;
                var eff2 = XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero, 0f, 1f, npc.whoAmI);
                eff2.Ai0 = 1f;
                eff2.Golden = true;

                if (_t % f3 == 0)
                {
                    var p8 = Spawn(npc, npc.Center + new Vector2(0f, 50f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 0f);
                    if (p8 != null) p8.timeLeft += 80 - _t;
                    var p9 = Spawn(npc, npc.Center + new Vector2(0f, -50f), -Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 0f);
                    if (p9 != null) p9.timeLeft += 80 - _t;
                }
                if (_phase >= 2 && _t % 9 < 5 && _t % 2 == 0)
                {
                    Spawn(npc, npc.Center + new Vector2(0f, 100f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 1f);
                    Spawn(npc, npc.Center + new Vector2(0f, -100f), -Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 1f);
                }

                npc.velocity = Vector2.Lerp(npc.velocity, new Vector2(-_ai2 * 80f, 0f), 0.04f);
                npc.velocity = new Vector2(npc.velocity.X, npc.velocity.Y * 0.96f);
            }
            else
            {
                npc.velocity *= 0.3f;
                _ai2 = 0f;
                SwitchState();
            }
        }

        // ---------------- Lance2 ----------------
        private void AI_Lance2(NPC npc, Player player)
        {
            if (++_t < 30) StraightMove30(npc, player, 100f, 100f);

            if (_t > 30)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -250f), 8f, 0.1f);

                if (_t == 60)
                {
                    var c3 = 15 + _phase * 2;
                    for (var n = 0; n < c3; n++)
                    {
                        var vec = XiheHelper.RotatedBy(XiheHelper.DirectionTo(npc.Center, player.Center),
                            n * (float)Math.PI * 2f / c3);
                        Spawn(npc, npc.Center + vec * 30f, vec, ModContent.ProjectileType<EoLLance>(), 1, 50f, 1f);
                    }
                }
            }

            if (_t > 103)
            {
                XiheFx.ScreenShake(20f, npc.Center);
                XiheVisuals.Create(VfxKind.RingWarp, npc.Center, Vector2.Zero);
                SwitchState();
            }
        }

        // ---------------- DashSpin ----------------
        private void AI_DashSpin(NPC npc, Player player)
        {
            if (++_t <= 30)
            {
                StraightMove30(npc, player, 200f, 140f);
                if (_t == 30) _ai2 = npc.Center.X > player.Center.X ? 1 : -1;
                if (_t >= 20 && _t % 5 == 0)
                {
                    var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero, 0f, 1f, npc.whoAmI);
                    eff.Golden = true;
                }
            }
            else if (_t < 60)
            {
                if (_t == 41) SoundEngine.PlaySound(SoundID.Item160, npc.Center);
                if (_t > 35)
                {
                    var dir = XiheHelper.RotatedBy(XiheHelper.DirectionTo(npc.Center, player.Center), _ai2 * 1.57f);
                    npc.velocity = Vector2.Lerp(npc.velocity, 30f * dir, 0.1f);
                }
            }

            if (_t > 60 && _t < 100)
            {
                XiheHelper.SpinAI(npc, player.Center, -_ai2 * (float)Math.PI * 2f / 35f);
                MovementEffect(npc);
                var pos3 = npc.Center + XiheHelper.DirectionFrom(npc.Center, player.Center) * 100f;
                if (_t % 2 == 0)
                {
                    var vec2 = XiheHelper.DirectionTo(npc.Center, player.Center);
                    if (_phase != 1)
                    {
                        Spawn(npc, pos3 - vec2 * 30f, vec2, ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 2f);
                        Spawn(npc, pos3 + vec2 * 30f, -vec2, ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 1f);
                    }
                    else
                    {
                        Spawn(npc, pos3 - vec2 * 30f, vec2, ModContent.ProjectileType<EoLLance>(), npc.damage / 6, 50f, 0f);
                        Spawn(npc, pos3 + vec2 * 30f, -vec2, ModContent.ProjectileType<EoLLance>(), npc.damage / 6, 50f, 0f);
                    }
                }
            }

            if (_t == 100) npc.velocity *= 0.5f;

            var t2 = 60 - _phase * 8;
            if (_t > 100 && _t < 100 + t2)
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -300f), 15f, 20f);
            if (_t == 100 + t2) SwitchState();
        }

        // ---------------- SunDance1 ----------------
        private void AI_SunDance1(NPC npc, Player player)
        {
            if (_t == 30)
            {
                SoundEngine.PlaySound(SoundID.Item163, npc.Center);
                _ai2 = XiheHelper.RandBool() ? 1 : -1;
            }

            if (++_t <= 30)
            {
                StraightMove30(npc, player, 100f, 150f);
            }
            else if (_t < 310)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -300f), 7f, 20f);
                var counts = _phase == 1 ? 10 : 12;

                if (_t == 40)
                {
                    SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                    LaserRing(npc, counts, 0f, _ai2, 130);
                }
                if (_t == 100)
                {
                    SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                    LaserRing(npc, counts, 0.5f, -_ai2, 130);
                }
                if (_t == 160)
                {
                    SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                    LaserRing(npc, counts, 0f, _ai2, 150);
                }
            }
            else
            {
                SwitchState();
            }
        }

        // ---------------- SunDance2 ----------------
        private void AI_SunDance2(NPC npc, Player player)
        {
            if (_t == 30)
            {
                SoundEngine.PlaySound(SoundID.Item163, npc.Center);
                _ai2 = XiheHelper.RandBool() ? 1 : -1;
            }

            if (++_t <= 30)
            {
                StraightMove30(npc, player, 100f, 150f);
            }
            else if (_t < 390)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -300f), 7f, 20f);
                const int counts2 = 12;

                if (_t == 35)
                {
                    SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                    LaserRing(npc, counts2, 0.5f, -_ai2, 130);
                }
                if (_t == 100)
                {
                    SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                    for (var num6 = 0; num6 < counts2 / 2; num6++)
                    {
                        var a0 = 2f * num6 * (float)Math.PI * 2f / counts2;
                        var p = Spawn(npc, npc.Center, XiheHelper.Vxr(a0), ModContent.ProjectileType<EoLLaser1>(),
                            npc.damage / 5, npc.whoAmI, _ai2);
                        SetLaserTime(p, 130);
                        var a1 = (2f * num6 + 1f) * (float)Math.PI * 2f / counts2;
                        var q = Spawn(npc, npc.Center, XiheHelper.Vxr(a1), ModContent.ProjectileType<EoLLaser1>(),
                            npc.damage / 5, npc.whoAmI, _ai2);
                        SetLaserTime(q, 290);
                    }
                }
            }
            else
            {
                SwitchState();
            }
        }

        // ---------------- Lance1 ----------------
        private void AI_Lance1(NPC npc, Player player)
        {
            if (++_t < 30) StraightMove30(npc, player, 100f, 100f);

            if (_t > 30 && _t <= 60)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -300f), 10f, 20f);
                if (_t == 35)
                {
                    var r7 = Main.rand.NextFloat() * 2f;
                    for (var num7 = 0; num7 < 3; num7++)
                    {
                        var counts3 = 10 + num7;
                        for (var num8 = 0; num8 < counts3; num8++)
                        {
                            var p = Spawn(npc, npc.Center,
                                XiheHelper.Vxr(num8 * (float)Math.PI * 2f / counts3 + r7) * (num7 * 100 + 100),
                                ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, num7 % 2 == 0 ? 3f : -3f);
                            if (p != null) XiheHelper.WriteLocalAi(p, 0f, npc.whoAmI, 0f, 0f);
                        }
                    }
                }
            }

            if (_t > 60 && _t < 103)
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -100f), 2f, 40f);

            if (_t > 103)
            {
                XiheFx.ScreenShake(20f, npc.Center);
                XiheVisuals.Create(VfxKind.RingWarp, npc.Center, Vector2.Zero);
                for (var num9 = 0; num9 < 8; num9++)
                {
                    var r8 = XiheHelper.NextVector2Unit();
                    var d = XiheVisuals.Create(VfxKind.LightDustCn,
                        npc.Center + r8 * Main.rand.Next(20, 60), r8 * Main.rand.Next(3, 8));
                    d.DrawColor = Color.Yellow;
                }
                SwitchState();
            }
        }

        // ---------------- Dash2 ----------------
        private void AI_Dash2(NPC npc, Player player)
        {
            if (++_t <= 30)
            {
                StraightMove30(npc, player, 100f, 50f);
                if (_t == 30) _ai2 = npc.Center.X > player.Center.X ? 1 : -1;
            }
            else if (_t <= 60)
            {
                if (_t == 45) SoundEngine.PlaySound(SoundID.Item160, npc.Center);
                var destination2 = player.Center + new Vector2(_ai2 * 800f, player.velocity.Y * 5f);
                XiheHelper.MoveTo(npc, destination2, 50f, 15f);
                if (_t % 5 == 0)
                {
                    var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero, 0f, 1f, npc.whoAmI);
                    eff.Golden = true;
                }
            }
            else if (_t <= 90)
            {
                MovementEffect(npc);
                npc.velocity = Vector2.Lerp(npc.velocity, new Vector2(-_ai2 * 115f, 0f), 0.07f);
                npc.velocity = new Vector2(npc.velocity.X, npc.velocity.Y * 0.95f);
                if (_t == 85) _ai2 = npc.Center.X > player.Center.X ? 1 : -1;
            }
            else if (_t < 130)
            {
                if (_t == 105) Sound(0);
                var destination3 = player.Center + new Vector2(_ai2 * 500f, -150f);
                XiheHelper.MoveTo(npc, destination3, 45f, 10f);
                if (_t % 5 == 0)
                {
                    var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero, 0f, 1f, npc.whoAmI);
                    eff.Golden = true;
                }
            }
            else if (_t < 150)
            {
                MovementEffect(npc);
                npc.velocity = Vector2.Lerp(npc.velocity, new Vector2(-_ai2 * 120f, 0f), 0.05f);
                npc.velocity = new Vector2(npc.velocity.X, npc.velocity.Y * 0.95f);
            }
            else if (_t < 180)
            {
                XiheHelper.MoveTo(npc, player.Center, 10f, 15f);
            }
            else
            {
                npc.velocity *= 0.3f;
                _ai2 = 0f;
                SwitchState();
            }

            if (_t > 135 && _t < 160 && _t % 3 == 0)
            {
                SpawnVanillaNpc(npc.Center, 519, new Vector2(XiheHelper.NextFloatDirection() * 8f, -15f));
            }
        }

        // ---------------- CrossRay ----------------
        private void AI_CrossRay(NPC npc, Player player)
        {
            if (++_t <= 30) StraightMove30(npc, player, 120f, 150f);

            if (_t > 30 && _t < 40)
                XiheHelper.MoveTo(npc, npc.Center + new Vector2(0f, -300f), 8f, 20f);

            if (_t > 40 && _t < 80)
            {
                var tPos = player.Center + new Vector2(player.Center.X > npc.Center.X ? -300 : 300, -300f);
                StraightMoveTo(npc, tPos, 0.1f);
            }

            if (_t == 20)
            {
                _ai2 = XiheHelper.RandBool() ? 1 : -1;
                for (var num10 = 0; num10 < 8; num10++)
                {
                    var p = Spawn(npc, npc.Center, Vector2.Zero, ModContent.ProjectileType<EoLLight>(), 0, npc.whoAmI, _ai2);
                    if (p == null) continue;
                    XiheHelper.WriteLocalAi(p, num10 / 8f * 360f, 0f, 0f, 0f);
                    p.rotation = num10 * (float)Math.PI * 2f / 8f;
                    p.timeLeft = 60;
                }
            }

            if (_t == 110)
            {
                XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                for (var num11 = 0; num11 < 8; num11++)
                {
                    var p = Spawn(npc, npc.Center, Vector2.Zero, ModContent.ProjectileType<EoLLight>(),
                        npc.damage / 6, npc.whoAmI, _ai2 * 2f);
                    if (p == null) continue;
                    XiheHelper.WriteLocalAi(p, 50f, 0f, 0f, 0f);
                    p.rotation = num11 * (float)Math.PI * 2f / 8f;
                    if (num11 < 4)
                    {
                        var rot = (float)Math.Atan2(player.Center.Y - npc.Center.Y, player.Center.X - npc.Center.X);
                        Spawn(npc, npc.Center,
                            XiheHelper.Vxr((num11 + 0.5f) * (float)Math.PI * 2f / 4f + rot) * 20f,
                            ModContent.ProjectileType<EoLFire>(), npc.damage / 4, npc.whoAmI, -_ai2 * 0.033f);
                    }
                }
            }

            if (_t >= 110 && _t <= 190)
            {
                XiheFx.ScreenShake(6f, npc.Center);
                XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero).Ai0 = 1f;
                XiheHelper.MoveTo(npc, player.Center, 70f, 90f);
            }

            if (_t == 190)
            {
                for (var a = 0; a < 4; a++)
                {
                    for (var num12 = 0; num12 < 10; num12++)
                    {
                        var rot = (float)Math.Atan2(player.Center.Y - npc.Center.Y, player.Center.X - npc.Center.X);
                        var vel4 = -XiheHelper.RotateRandom(
                            XiheHelper.RotatedBy(XiheHelper.Vxr(rot), (a + 0.5f) * 1.57f), 0.4f);
                        var p = Spawn(npc, npc.Center, vel4 * Main.rand.Next(25, 50),
                            ModContent.ProjectileType<EoLProj>(), npc.damage / 5, npc.target, 3f);
                        if (p != null) p.timeLeft += Main.rand.Next(-10, 30);
                    }
                }
            }

            if (_t > 190) XiheHelper.MoveTo(npc, player.Center - new Vector2(0f, 300f), 10f, 30f);
            if (_t > 260) SwitchState();
        }

        // ---------------- Prepare ----------------
        private void AI_Prepare(NPC npc, Player player)
        {
            _setAreaRadius = 1200f;
            if (TimerM > 3620) _setAreaRadius = 750f;

            if (_t == 30)
            {
                SoundEngine.PlaySound(SoundID.Item163, npc.Center);
                _ai2 = XiheHelper.RandBool() ? 1 : -1;
            }

            if (++_t <= 30)
            {
                StraightMove30(npc, player, 100f, 150f);
            }
            else if (TimerM < 3750)
            {
                const int counts4 = 9;
                if (_t == 40)
                {
                    SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                    for (var num13 = 0; num13 < counts4; num13++)
                    {
                        var p = Spawn(npc, npc.Center, XiheHelper.Vxr(num13 * (float)Math.PI * 2f / counts4),
                            ModContent.ProjectileType<EoLLaser1>(), npc.damage / 5, npc.whoAmI, 2f);
                        SetLaserTime(p, 3750 - TimerM);
                    }
                }
            }

            if (TimerM == 3615) _ai2 = 1f;
            if (TimerM < 3615) XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -500f), 7f, 20f);

            if (TimerM > 3615 && TimerM < 3820)
            {
                npc.velocity *= 0.95f;
                if (TimerOnBeat)
                {
                    _ai2 *= -1f;
                    for (var num14 = 0; num14 < 14; num14++)
                    {
                        var vec3 = XiheHelper.Vxr(num14 * (float)Math.PI * 2f / 14f);
                        Spawn(npc, npc.Center + vec3 * 1500f, -XiheHelper.RotatedBy(vec3, _ai2) * 5f,
                            ModContent.ProjectileType<EoLProj>(), npc.damage / 6, npc.whoAmI, 4f);
                    }
                }
            }

            if (TimerM > 3820 && TimerM < 3980)
            {
                npc.velocity *= 0.95f;
                if (TimerOnHalfBeat)
                {
                    _ai2 *= -1f;
                    for (var num15 = 0; num15 < 12; num15++)
                    {
                        var vec4 = XiheHelper.Vxr(num15 * (float)Math.PI * 2f / 12f);
                        Spawn(npc, npc.Center + vec4 * 1500f, -XiheHelper.RotatedBy(vec4, _ai2) * 5f,
                            ModContent.ProjectileType<EoLProj>(), npc.damage / 6, npc.whoAmI, 4f);
                    }
                }
            }

            if (TimerM == 4000) XiheVisuals.Create(VfxKind.RingWarp2, npc.Center, Vector2.Zero);

            if (TimerM > 3920 && TimerM < 4080 && TimerOnHalfBeat)
            {
                var v = XiheVisuals.Create(VfxKind.VLight, npc.Center, Vector2.Zero);
                v.BloomColor = Color.Yellow;
                v.Scale = 2f;
            }

            if (TimerM > 4080) SwitchState();
        }

        // ---------------- Speacial1 ----------------
        private void AI_Speacial1(NPC npc, Player player)
        {
            _t = TimerM - 4080;

            if (_t == 0) _tPos = player.Center + player.velocity * 20f;

            if (_t == 10)
            {
                SoundEngine.PlaySound(SoundID.Item160, npc.Center);
                SoundEngine.PlaySound(SoundID.Item163, npc.Center);
                var v = XiheVisuals.Create(VfxKind.VLight, npc.Center, Vector2.Zero, 0f, 3f, npc.whoAmI);
                v.BloomColor = Color.Yellow;
                v.MaxTime *= 2;
                v.TimeLeft = v.MaxTime;
            }

            if (_t < 30) StraightMove30(npc, player, 0f, 300f);
            if (_t == 30) npc.velocity = Vector2.Zero;

            if (_t == 50)
            {
                XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                XiheFx.RadialGlur(npc.Center, 50, true);
                SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                XiheSounds.PlayLaser(npc.Center);

                var p = Spawn(npc, npc.Center, Vector2.Zero, ModContent.ProjectileType<EoLBigLaser>(),
                    npc.damage / 3, 50f, 0f);
                if (p != null) p.timeLeft = 210;
            }

            if (_t > 50 && _t < 75)
            {
                var d = _t - 50;
                if (_t % 2 == 0)
                {
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(d * 100, -700f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 5, 50f, 3f), 103);
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(-d * 100, -700f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 5, 50f, 3f), 103);
                }
            }

            var tt = (_t - 130) % 35;
            if (_t > 130 && _t < 230)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -300f), 10f, 20f);
                if (tt < 25)
                {
                    var d2 = tt;
                    if (_t % 2 == 0)
                    {
                        Spawn(npc, npc.Center + new Vector2(d2 * 120 + Main.rand.Next(-50, 50), -700f), Vector2.UnitY,
                            ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 1f);
                        Spawn(npc, npc.Center + new Vector2(-d2 * 120 + Main.rand.Next(-50, 50), -700f), Vector2.UnitY,
                            ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 1f);
                    }
                }
            }

            if (TimerM == 4330)
            {
                npc.velocity = Vector2.Zero;
                for (var num16 = 0; num16 < 24; num16++)
                {
                    var vec5 = XiheHelper.Vxr(num16 * (float)Math.PI * 2f / 24f);
                    Spawn(npc, npc.Center + vec5 * 30f, vec5, ModContent.ProjectileType<EoLLance>(), 1, 50f, 0f);
                }
            }

            if (TimerM > 4330 && TimerM < 4500) _setAreaRadius = 900f;

            if (TimerM > 4350 && TimerM < 4440)
            {
                npc.velocity = Vector2.Zero;
                const int counts5 = 12;
                if (_t % 16 == 0)
                {
                    for (var num17 = 0; num17 < counts5; num17++)
                    {
                        var vec6 = XiheHelper.Vxr(num17 * (float)Math.PI * 2f / counts5);
                        var p = Spawn(npc, npc.Center + vec6 * 50f, vec6 * 10f, ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 6, npc.whoAmI, 5f);
                        if (p != null) p.timeLeft = 200;
                    }
                }
                if (_t % 16 == 8)
                {
                    for (var num18 = 0; num18 < counts5; num18++)
                    {
                        var vec7 = XiheHelper.Vxr((num18 + 0.5f) * (float)Math.PI * 2f / counts5);
                        var p = Spawn(npc, npc.Center + vec7 * 50f, vec7 * 10f, ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 6, npc.whoAmI, 5f);
                        if (p != null) p.timeLeft = 200;
                    }
                }
            }

            if (TimerM == 4450) XiheVisuals.Create(VfxKind.Circle, npc.Center, Vector2.Zero);

            if (TimerM > 4450 && TimerM < 4540)
            {
                XiheFx.ScreenShake((TimerM - 4450) / 4f, npc.Center);
                if (TimerOnHalfBeat)
                {
                    var v = XiheVisuals.Create(VfxKind.VLight, npc.Center, Vector2.Zero);
                    v.BloomColor = Color.Yellow;
                    v.Scale = 2f;
                }
                if (TimerM == 4500)
                {
                    var p = Spawn(npc, new Vector2(npc.Center.X, npc.Center.Y - 200f), Vector2.Zero,
                        ModContent.ProjectileType<EoLBigLaser>(), npc.damage / 3, npc.whoAmI, 1f);
                    if (p != null) p.timeLeft = 350;
                }
                XiheFx.UseScreenPosition(player.Center + (npc.Center - player.Center) * 0.5f, 0.05f);
            }

            if (TimerM > 4600 && TimerM < 4610)
            {
                var d3 = (int)Math.Pow(TimerM - 4600, 1.3) * 100;
                if (_t % 2 == 0)
                {
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(d3, -700f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 5, 50f, 3f), 51);
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(-d3, -700f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 5, 50f, 3f), 51);
                }
            }

            if (TimerM == 4540)
            {
                var v = XiheVisuals.Create(VfxKind.VLight, npc.Center, Vector2.Zero, 0f, 3f, npc.whoAmI);
                v.BloomColor = Color.Yellow;
                v.MaxTime *= 2;
                v.TimeLeft = v.MaxTime;
            }

            if (TimerM > 4540 && TimerM < 4580)
                StraightMoveTo(npc, player.Center + new Vector2(0f, -300f), 0.2f);

            if (TimerM > 4850)
            {
                BossState = State.Speacial2;
                _t = 0;
                npc.dontTakeDamage = true;
            }
        }

        private Vector2 _tPos;

        // ---------------- Speacial2 ----------------
        private void AI_Speacial2(NPC npc, Player player)
        {
            _setAreaRadius = 750f;
            _t++;

            if (_phase == 2 && _t < 60)
                XiheFx.UseScreenPosition(player.Center + (_tPos - player.Center) * 0.4f, 0.2f);

            if (TimerM == 11420) XiheVisuals.Create(VfxKind.Circle, npc.Center, Vector2.Zero);
            if (TimerM == 11495) XiheVisuals.Create(VfxKind.VLight, npc.Center, Vector2.Zero);
            if (TimerM > 11440) XiheHelper.LerpValue(ref _alpha, 1f, 0.02f);
            if (TimerM == 11460) npc.dontTakeDamage = false;

            if (_t < 30)
            {
                const int x = 150;
                const int y = 150;
                var tPos2 = player.Center + new Vector2(
                    player.Center.X + player.velocity.X * 30f > npc.Center.X ? -x : x, -y);
                if (_alpha > 0.1f) MovementEffect(npc);
                StraightMoveTo(npc, tPos2, 0.15f);
                if (_t < 15) XiheHelper.LerpValue(ref _alpha, 0f, 0.25f);
                else XiheHelper.LerpValue(ref _alpha, 0.6f, 0.25f);
            }
            else if (_t < 120)
            {
                XiheHelper.MoveTo(npc, player.Center + new Vector2(0f, -250f), 20f, 25f);
            }
            if (_t > 120) npc.velocity *= 0.95f;

            if (_t == 100)
            {
                var p = Spawn(npc, npc.Center, Vector2.Zero, ModContent.ProjectileType<Cycloid>(), npc.damage / 3, npc.whoAmI, _phase == 2 ? 1f : 0f);
            }

            if (_t > 930)
            {
                BossState = State.Transition;
                if (_phase == 2) BossState = State.Speacial4;
                _t = 0;
                _alpha = 0f;
                if (BossState == State.Speacial4) npc.dontTakeDamage = true;
            }
        }

        // ---------------- Transition ----------------
        private void AI_Transition(NPC npc, Player player)
        {
            if (++_t < XiheHelper.BeatTime(2)) npc.Center = player.Center - new Vector2(0f, 300f);

            if (_t == 30)
            {
                for (var num19 = 0; num19 < 3; num19++)
                {
                    var counts6 = 10 + num19;
                    for (var num20 = 0; num20 < counts6; num20++)
                    {
                        var p = Spawn(npc, npc.Center,
                            XiheHelper.Vxr(num20 * (float)Math.PI * 2f / counts6) * (num19 * 100 + 100),
                            ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, num19 % 2 == 0 ? 3f : -3f);
                        if (p != null) XiheHelper.WriteLocalAi(p, 0f, npc.whoAmI, 0f, 0f);
                    }
                }
            }

            if (_t == XiheHelper.BeatTime(2))
            {
                _alpha = 1f;
                Sound(0);
                XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                XiheFx.RadialGlur(npc.Center, 30, true);
                npc.dontTakeDamage = false;
            }

            if (_t > XiheHelper.BeatTime(2) && _t <= XiheHelper.BeatTime(4))
            {
                XiheFx.ScreenShake(10f, npc.Center);
                if (TimerM % 15 == 0) XiheVisuals.Create(VfxKind.RingWarp, npc.Center, Vector2.Zero);
            }

            if (_t == XiheHelper.BeatTime(4))
            {
                _phase = 2;
                SwitchState();
            }
        }

        // ---------------- Prepare2 ----------------
        private void AI_Prepare2(NPC npc, Player player)
        {
            _setAreaRadius = 1300f;

            if (++_t == 30)
            {
                SoundEngine.PlaySound(SoundID.Item163, npc.Center);
                _ai2 = XiheHelper.RandBool() ? 1 : -1;
            }

            if (_t <= 30)
            {
                StraightMove30(npc, player, 100f, 150f);
            }
            else if (TimerM < 9540)
            {
                const int counts7 = 15;
                if (_t == 40)
                {
                    SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                    for (var num21 = 0; num21 < counts7; num21++)
                    {
                        var p = Spawn(npc, npc.Center, XiheHelper.Vxr(num21 * (float)Math.PI * 2f / counts7),
                            ModContent.ProjectileType<EoLLaser1>(), npc.damage / 5, npc.whoAmI, 2f);
                        SetLaserTime(p, 9540 - TimerM);
                    }
                }
            }

            if (TimerM > 9420 && TimerM < 9750)
            {
                npc.velocity *= 0.99f;
                _ai2 += _ai3;
                _ai3 += XiheHelper.NextFloatDirection() * 0.0005f;
                if (Math.Abs(_ai3) > 0.0045f) _ai3 = Math.Sign(_ai3) * 0.0045f;

                var f4 = 20;
                if (TimerM > 9480) f4 = 15;
                if (TimerM > 9520) f4 = 11;
                if (TimerM > 9600) f4 = 8;
                var speed = (TimerM - 9420) / 200f;

                if (TimerM % (2 * f4) == 0)
                {
                    for (var num22 = 0; num22 < 9; num22++)
                    {
                        var vec8 = XiheHelper.Vxr(num22 * (float)Math.PI * 2f / 9f + _ai2);
                        Spawn(npc, npc.Center + vec8 * 1800f, -vec8 * speed, ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 5, npc.whoAmI, 7f);
                    }
                }
                if (TimerM % (2 * f4) == f4)
                {
                    for (var num23 = 0; num23 < 9; num23++)
                    {
                        var vec9 = XiheHelper.Vxr((num23 + 0.5f) * (float)Math.PI * 2f / 9f + _ai2);
                        Spawn(npc, npc.Center + vec9 * 1800f, -vec9 * speed, ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 5, npc.whoAmI, 7f);
                    }
                }
            }

            if (TimerM > 9600)
                XiheFx.UseScreenPosition((npc.Center + player.Center) / 2f, 0.05f);
            if (TimerM > 9710)
                XiheFx.ScreenShake((TimerM - 9710) / 4f, npc.Center);

            if (TimerM == 9815)
            {
                var r9 = Main.rand.NextFloat() * 2f;
                const int counts8 = 18;
                for (var num24 = 0; num24 < counts8; num24++)
                {
                    var p = Spawn(npc, npc.Center,
                        XiheHelper.Vxr(num24 * (float)Math.PI * 2f / counts8 + r9) * 100f,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, 3f);
                    if (p != null) XiheHelper.WriteLocalAi(p, 0f, npc.whoAmI, 0f, 0f);
                }
            }

            if (TimerM > 9825)
            {
                BossState = State.Speacial3;
                _t = 0;
            }
        }

        // ---------------- Speacial3 ----------------
        private void AI_Speacial3(NPC npc, Player player)
        {
            if (_t == 1) XiheVisuals.Create(VfxKind.Circle, npc.Center, Vector2.Zero);
            _setAreaRadius = 1200f;
            XiheFx.UseScreenPosition(player.Center + (npc.Center - player.Center) * 0.4f, 0.2f);

            if (++_t < 30) StraightMove30(npc, player, 0f, 300f);

            if (_t == 60)
            {
                npc.dontTakeDamage = true;
                _tPos = npc.Center;
                var r10 = Main.rand.NextFloat();
                for (var num25 = 0; num25 < 36; num25++)
                {
                    Spawn(npc, npc.Center,
                        XiheHelper.Vxr(r10 + num25 * (float)Math.PI * 2f / 36f) * (14f - 3f * (num25 % 3)),
                        ModContent.ProjectileType<EoLProj>(), npc.damage / 5, 0f, 0f);
                }
                _ai2 = 1f;
                XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                XiheFx.ScreenShake(30f, npc.Center);
                XiheFx.RadialGlur(npc.Center, 10, true);
                SoundEngine.PlaySound(SoundID.Item159, npc.Center);
                XiheSounds.PlayLaser(npc.Center);
                Spawn(npc, npc.Center, Vector2.Zero, ModContent.ProjectileType<SunProj>(), npc.damage, 0f, 0f);
            }

            if (_t > 70 && _t < 412)
            {
                if (_t % 15 == 0)
                {
                    var rot = (float)Math.Atan2(player.Center.Y - npc.Center.Y, player.Center.X - npc.Center.X);
                    var vec10 = XiheHelper.Vxr(rot + (float)XiheHelper.Gaussian(0.0, 1.0));
                    Spawn(npc, npc.Center + vec10 * 30f, vec10, ModContent.ProjectileType<EoLLance>(),
                        npc.damage / 4, 50f, 5f);
                }
                if (_t % 40 == 0)
                {
                    _ai2 *= -1f;
                    var r11 = Main.rand.NextFloat() * 10f;
                    for (var num26 = 0; num26 < 60; num26++)
                    {
                        if (num26 % 10 < 3)
                        {
                            var vec11 = XiheHelper.Vxr(r11 + num26 * (float)Math.PI * 2f / 60f);
                            Spawn(npc, npc.Center + vec11 * 30f, vec11 * 4f, ModContent.ProjectileType<EoLProj>(),
                                npc.damage / 5, _ai2, 8f);
                        }
                    }
                }
            }

            if (_t > 412 && _t < 665)
            {
                if (_t % 20 == 0)
                {
                    var rot = (float)Math.Atan2(player.Center.Y - npc.Center.Y, player.Center.X - npc.Center.X);
                    var vec12 = XiheHelper.Vxr(rot + (float)XiheHelper.Gaussian(0.0, 1.5));
                    Spawn(npc, npc.Center + vec12 * 30f, vec12, ModContent.ProjectileType<EoLLance>(),
                        npc.damage / 4, 50f, 5f);
                }
                if (_t % 30 == 0)
                {
                    _ai2 *= -1f;
                    var r12 = Main.rand.NextFloat() * 10f;
                    for (var num27 = 0; num27 < 20; num27++)
                    {
                        var vec13 = XiheHelper.Vxr(r12 + num27 * (float)Math.PI * 2f / 20f);
                        Spawn(npc, npc.Center + vec13 * 30f, vec13 * 4f, ModContent.ProjectileType<EoLProj>(),
                            npc.damage / 5, _ai2, 8f);
                    }
                }
            }

            if (TimerM > 10600)
            {
                _t = 0;
                BossState = State.Speacial2;
            }
        }

        // ---------------- Speacial4 ----------------
        private void AI_Speacial4(NPC npc, Player player)
        {
            if (TimerM < 11580)
            {
                XiheHelper.LerpValue(ref _alpha, 0.2f, 0.01f);
                if (TimerM == 11570)
                {
                    var c = XiheVisuals.Create(VfxKind.CircleWarp, npc.Center, Vector2.Zero, 3.14f);
                    c.Ai0 *= 2f;
                }
                if (XiheHelper.RandBool(3))
                {
                    var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center,
                        XiheHelper.NextVector2Unit() * 5f, 0f, 1f, npc.whoAmI);
                    eff.Ai0 = 1f;
                    eff.Alpha = _alpha + 0.1f;
                    eff.Golden = true;
                }
                npc.position = npc.position + XiheHelper.NextVector2Unit() * 5f;
                npc.velocity = Vector2.Zero;
            }
            else if (TimerM < 11900)
            {
                if (TimerM > 11650) GrayScale = false;
                _alpha = 0f;
                StraightMoveTo(npc, player.Center - new Vector2(0f, 600f), 0.1f);
                if (TimerM % 4 == 0)
                {
                    var p = Spawn(npc, player.Center + new Vector2(Main.rand.Next(-1600, 1600), -800f),
                        new Vector2(0f, Main.rand.Next(7, 13)), ModContent.ProjectileType<CrystalArrow>(),
                        npc.damage / 5, 1145f, 50f);
                    if (p != null) p.timeLeft += 150;
                }
                _ai2 = 1f;
            }
            else if (TimerM < 12200)
            {
                StraightMoveTo(npc, player.Center - new Vector2(0f, 600f), 0.1f);
                if (TimerM % 70 == 0)
                {
                    _ai2 *= -1f;
                    var pos4 = player.Center + new Vector2(1100f * _ai2, 50f);
                    var p = Spawn(npc, pos4 + XiheHelper.NextVector2Unit() * 200f,
                        XiheHelper.DirectionTo(pos4, npc.Center) * 6f, ModContent.ProjectileType<EoLProj>(),
                        npc.damage / 5, 0f, 9f);
                    if (p != null) p.timeLeft -= 50;
                }
            }

            _t = TimerM - 12350;
            var shootLance = _t % 103 == 0;
            if (_t >= XiheHelper.BeatTime(8) && _t < XiheHelper.BeatTime(12)) shootLance = TimerOnBeat;
            if (_t >= XiheHelper.BeatTime(12)) shootLance = TimerOnHalfBeat;

            if (_t >= 0 && shootLance && _t <= XiheHelper.BeatTime(14))
            {
                var r13 = XiheHelper.NextVector2Unit();
                for (var num28 = -6; num28 <= 6; num28++)
                {
                    var v = XiheHelper.VerticalVec(r13) * num28 * 200f;
                    var pos5 = player.Center + r13 * 700f + v;
                    var p = Spawn(npc, pos5, -r13, ModContent.ProjectileType<EoLLance>(),
                        npc.damage / 5, 60f, 6f);
                    if (p == null) continue;
                    p.netUpdate2 = true;
                    p.timeLeft = 325 + num28 * 2;
                    SetLanceTime(p, 325 + num28 * 2);
                }
            }

            for (var num29 = 0; num29 <= 8; num29 += 4)
            {
                if (_t == XiheHelper.BeatTime(num29))
                {
                    npc.dontTakeDamage = false;
                    if (num29 == 8) Sound(0);
                    else SoundEngine.PlaySound(SoundID.Item160, npc.Center);

                    _alpha = 1f;
                    if (num29 == 0)
                    {
                        npc.Center = player.Center - new Vector2(0f, 500f) + XiheHelper.NextVector2Unit() * 100f;
                    }
                    XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                    XiheFx.RadialGlur(npc.Center, 20, false);
                    npc.dontTakeDamage = false;
                    XiheFx.Brightness(1.5f, 30);
                    for (var num30 = 0; num30 < 33; num30++)
                    {
                        Spawn(npc, npc.Center,
                            XiheHelper.Vxr(num30 * (float)Math.PI * 2f / 33f) * (6 + 2 * (num30 % 3)),
                            ModContent.ProjectileType<EoLProj>(), npc.damage / 5, 0f, 0f);
                    }
                    break;
                }

                if (_t > XiheHelper.BeatTime(2 + num29) - 30 && _t < XiheHelper.BeatTime(3 + num29) - 30)
                {
                    XiheHelper.LerpValue(ref _alpha, 0f, 0.1f);
                    if (XiheHelper.RandBool(3))
                    {
                        var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center,
                            XiheHelper.NextVector2Unit() * 8f, 0f, 1f, npc.whoAmI);
                        eff.Ai0 = 1f;
                        eff.Alpha = _alpha + 0.2f;
                        eff.Golden = true;
                    }
                    npc.position = npc.position + XiheHelper.NextVector2Unit() * 10f;
                    npc.velocity = Vector2.Zero;
                    break;
                }

                if (_t == XiheHelper.BeatTime(3 + num29) - 30)
                {
                    npc.dontTakeDamage = true;
                    _alpha = 0f;
                    break;
                }
            }

            var start = XiheHelper.BeatTime(4) - 60;
            if (_t > start - 30 && _t < start)
                StraightMoveTo(npc, player.Center - new Vector2(0f, 500f), 0.2f);

            if (_t >= start && _t < start + 30)
            {
                const float dis2 = 100f;
                const float ai3 = 2f;
                if (_t % 2 == 0)
                {
                    Spawn(npc, npc.Center + new Vector2((_t - start) * dis2 + 20f, -1000f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, ai3);
                    Spawn(npc, npc.Center + new Vector2(-(_t - start) * dis2 - 20f, -1000f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLance>(), npc.damage / 5, 50f, ai3);
                }
            }

            if (_t >= XiheHelper.BeatTime(12) && _t < XiheHelper.BeatTime(16) && XiheHelper.RandBool(15))
            {
                var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero, 0f, 1f, npc.whoAmI);
                eff.Ai0 = 1f;
                eff.Alpha = 0.4f;
                eff.Golden = true;
            }

            if (_t == XiheHelper.BeatTime(8) - 45)
            {
                npc.Center = player.Center - new Vector2(0f, 350f) + XiheHelper.NextVector2Unit() * 100f;
                var c4 = 15 + _phase * 2;
                for (var num31 = 0; num31 < c4; num31++)
                {
                    var vec14 = XiheHelper.RotatedBy(XiheHelper.DirectionTo(npc.Center, player.Center),
                        num31 * (float)Math.PI * 2f / c4);
                    Spawn(npc, npc.Center + vec14 * 30f, vec14, ModContent.ProjectileType<EoLLance>(), 1, 50f, 1f);
                }
            }
            else if (_t >= XiheHelper.BeatTime(12) && _t < XiheHelper.BeatTime(14))
            {
                XiheHelper.MoveTo(npc, player.Center - new Vector2(0f, 450f), 10f, 30f);
            }
            else if (_t >= XiheHelper.BeatTime(14) && _t < XiheHelper.BeatTime(14) + 30)
            {
                npc.velocity = Vector2.Zero;
                if (_t % 2 == 0)
                {
                    var d4 = _t - XiheHelper.BeatTime(14);
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(d4 * 90, -1000f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 4, 50f, 4f), 145);
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(-d4 * 90, -1000f), Vector2.UnitY,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 4, 50f, 4f), 145);
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(1200f, (15 - d4) * 180 + 90), -Vector2.UnitX,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 4, 50f, 4f), 145);
                    SetLaserTime(Spawn(npc, npc.Center + new Vector2(-1200f, -(15 - d4) * 180 - 90), Vector2.UnitX,
                        ModContent.ProjectileType<EoLLaser1>(), npc.damage / 4, 50f, 4f), 145);
                }
            }

            if (_t == XiheHelper.BeatTime(16))
            {
                SoundEngine.PlaySound(SoundID.Item160, npc.Center);
                _alpha = 1f;
                XiheVisuals.Create(VfxKind.Wave, npc.Center, Vector2.Zero);
                XiheFx.RadialGlur(npc.Center, 50, true);
                npc.dontTakeDamage = false;
                XiheFx.Brightness(1.5f, 30);
            }

            if (_t > XiheHelper.BeatTime(16) && _t < XiheHelper.BeatTime(16) + 50)
                XiheFx.ScreenShake(10f, npc.Center);

            if (_t == XiheHelper.BeatTime(16) + 50)
            {
                _t = 0;
                _phase = 3;
                BossState = State.EoLProjShoot1;
            }
        }

        // ---------------- Leave ----------------
        private void AI_Leave(NPC npc, Player player)
        {
            npc.velocity = Vector2.Zero;

            if (++_t < 360)
            {
                npc.position = new Vector2(npc.position.X, npc.position.Y - 1f);
                if (_t % 4 == 0)
                {
                    XiheVisuals.Create(VfxKind.LightDust,
                        npc.Center + new Vector2(XiheHelper.NextFloatDirection() * 200f,
                            -50f + XiheHelper.NextFloatDirection() * 120f),
                        new Vector2(XiheHelper.NextFloatDirection() * 1f, 4f));
                }
                if (_t > 50 && _t % 5 == 0 && npc.life == 1)
                {
                    var p = Spawn(npc, npc.Center, XiheHelper.NextVector2Unit() * 4f,
                        ModContent.ProjectileType<EoLProj>(), npc.damage / 5, npc.whoAmI, 114514f);
                    if (p != null) p.timeLeft += 100;
                }
                if (_t > 50) XiheFx.UseScreenPosition((npc.Center + player.Center) / 2f, 0.05f);
                _alpha -= 0.0027777778f;
            }
            else if (npc.life == 1)
            {
                XiheFx.RadialGlur(npc.Center, 50, true);
                npc.life = 0;
                npc.checkDead();
                for (var num32 = 0; num32 < 20; num32++)
                {
                    Spawn(npc, npc.Center, XiheHelper.Vxr(num32 * (float)Math.PI * 2f / 20f) * 10f,
                        ModContent.ProjectileType<CrystalArrow>(), 1, 0f, 50f);
                }
            }
            else
            {
                npc.active = false;
            }

            if (npc.life != 1) _t += 5;
        }

        // ================= 状态切换（照抄 SwitchState） =================

        internal void SwitchState()
        {
            _t = 0;
            var npc = NPC;
            

            if (BossState == State.Spawn) BossState = State.Roar;
            else if (BossState == State.Roar)
            {
                BossState = XiheHelper.RandBool() ? State.EoLProjShoot1 : State.EoLProjShoot2;
            }
            else if (TimerM < 2300)
            {
                if (BossState != State.Delay) BossState = State.Delay;
                else BossState = State.SunDance1;
            }
            else
            {
                if (BossState == State.Prepare)
                    BossState = _phase == 1 ? State.Speacial1 : State.Speacial3;

                if (BossState == State.Delay) BossState = State.Dash1;

                var state = BossState;
                if (state >= State.EoLProjShoot1 && state <= State.EoLProjShoot2)
                {
                    BossState = XiheHelper.RandBool() ? State.Dash1 : State.DashSpin;
                    if (_phase == 2 && XiheHelper.RandBool(5) && TimerM > 6500)
                        BossState = State.CrossRay;
                }
                else if (state >= State.VLaser && state <= State.Lance2)
                {
                    BossState = XiheHelper.RandBool() ? State.EoLProjShoot1 : State.EoLProjShoot2;
                }
                else if (state >= State.Dash1 && state <= State.DashSpin)
                {
                    BossState = Main.rand.Next(5) >= 2 || _phase != 2
                        ? (XiheHelper.RandBool() ? State.Lance2 : State.VLaser)
                        : (Main.rand.Next(5) < 3 ? State.SunDance1 : State.CrossRay);
                }
                else if (state == State.SunDance1 || state == State.SunDance2 || state == State.CrossRay)
                {
                    BossState = XiheHelper.RandBool() ? State.Lance2 : State.VLaser;
                }
                else if (BossState == State.Transition)
                {
                    BossState = XiheHelper.RandBool() ? State.EoLProjShoot1 : State.EoLProjShoot2;
                    return;
                }
            }

            if (TimerM > 3300 && _phase == 1 && BossState != State.Speacial1)
            {
                BossState = State.Prepare;
                return;
            }

            if (_phase == 3)
            {
                if (BossState == State.Lance2) BossState = State.Lance1;
                if (BossState == State.SunDance1)
                    BossState = XiheHelper.RandBool() ? State.Lance2 : State.VLaser;
                if (NPC != null && NPC.life == 1) BossState = State.Leave;
            }

            if (_phase == 2)
            {
                if (BossState == State.Lance2) BossState = State.Lance1;
                if (BossState == State.SunDance1) BossState = State.SunDance2;
                if (BossState == State.Dash1 && XiheHelper.RandBool(4)) BossState = State.Dash2;
                if (TimerM > 8600 && BossState == State.SunDance2)
                    BossState = XiheHelper.RandBool() ? State.Lance2 : State.VLaser;
                if (TimerM > 9120 && BossState != State.Speacial3) BossState = State.Prepare2;
            }
        }

        // ================= 小动作 =================

        private bool TimerOnBeat => Math.Abs(TimerM % 51.5f) < 1f;
        private bool TimerOnHalfBeat => Math.Abs(TimerM % 25.7f) < 1f;

        private void StraightMove30(NPC npc, Player player, float x, float y)
        {
            var tPos = player.Center + new Vector2(
                player.Center.X + player.velocity.X * 30f > npc.Center.X ? -x : x, -y);
            if (_alpha > 0.1f) MovementEffect(npc);
            StraightMoveTo(npc, tPos, 0.16f);
            if (_t < 15) XiheHelper.LerpValue(ref _alpha, 0f, 0.25f);
            else XiheHelper.LerpValue(ref _alpha, 1f, 0.2f);
        }

        private void MovementEffect(NPC npc)
        {
            var eff = XiheVisuals.Create(VfxKind.EolEff, npc.Center, Vector2.Zero, 0f, 1f, npc.whoAmI);
            eff.Ai0 = 1f;
            eff.Alpha = _alpha;
            eff.Golden = true;
        }

        private static void StraightMoveTo(NPC npc, Vector2 tPos, float n)
        {
            npc.velocity *= 0.8f;
            npc.Center = Vector2.Lerp(npc.Center, tPos, n);
        }

        internal void Sound(int type)
        {
            if (type == 0) SoundEngine.PlaySound(SoundID.Item161, Center);
            if (type == 1) SoundEngine.PlaySound(SoundID.Item160, Center);
            if (type == 2) SoundEngine.PlaySound(SoundID.Item164, Center);
        }

        // ================= 生成工具 =================

        private static Projectile Spawn(NPC owner, Vector2 pos, Vector2 vel, int type, int damage,
            float ai0, float ai1)
        {
            if (type <= 0) return null;
            var idx = Projectile.NewProjectile(null, pos, vel, type, damage, 0f, Main.myPlayer, ai0, ai1, 0f);
            if (idx < 0 || idx >= Main.projectile.Length) return null;
            return Main.projectile[idx];
        }

        /// <summary>EoLLaser1 的寿命门槛（原版是 public maxTime 字段，这里用 ai[2] 传）。</summary>
        private static void SetLaserTime(Projectile p, int maxTime)
        {
            if (p == null) return;
            var ai = p.ai;
            ai[2] = maxTime;
            p.ai = ai;
        }

        /// <summary>EoLLance 的寿命（原版 maxtime 字段，ai[2] 传）。</summary>
        private static void SetLanceTime(Projectile p, int maxTime)
        {
            if (p == null) return;
            var ai = p.ai;
            ai[2] = maxTime;
            p.ai = ai;
        }

        private void LaserRing(NPC npc, int counts, float offset, float spin, int maxTime)
        {
            for (var i = 0; i < counts; i++)
            {
                var p = Spawn(npc, npc.Center,
                    XiheHelper.Vxr((i + offset) * (float)Math.PI * 2f / counts),
                    ModContent.ProjectileType<EoLLaser1>(), npc.damage / 5, npc.whoAmI, spin);
                SetLaserTime(p, maxTime);
            }
        }

        private static void SpawnVanillaNpc(Vector2 pos, int type, Vector2 velocity)
        {
            try
            {
                var idx = NPC.NewNPC(null, (int)pos.X, (int)pos.Y, type);
                if (idx >= 0 && idx < Main.npc.Length) Main.npc[idx].velocity = velocity;
            }
            catch { }
        }

        // ================= 绘制 =================

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            try
            {
                DrawBody(npc, spriteBatch, screenPos);
            }
            catch (Exception e)
            {
                if (!_drawLogged) { _drawLogged = true; XiheBossMod.Log?.Warn("[boss] 绘制异常：" + e.Message); }
            }
            return false;
        }

        private bool _drawLogged;
        private void DrawBody(NPC npc, SpriteBatch sb, Vector2 screenPos)
        {
            var a = MathHelper.Clamp(_alpha, 0f, 1f);
            if (a <= 0.002f) return;
            var pos = npc.Center - screenPos;

            const int wingFrames = 11;
            var wingTime = (int)Main.timeForVisualEffects / 3;
            var wingFrame = Utils.Frame(WingTexture, 1, wingFrames, 0, wingTime % wingFrames, 0, 0);
            var wingOrigin = new Vector2(WingTexture.Width, WingTexture.Height / wingFrames) / 2f;

            // 只画 wing1（Extra_157，带正常 alpha）。wing2 / Extra_187 / 手臂都是“黑底加色”贴图，
            // SpriteBatch 只有 AlphaBlend，画上去就是黑矩形（就是那条黑块）。要恢复它们必须走着色器
            // 加色混合（贴图进图集），否则永远会带一块黑底。
            XiheDraw.Sprite(sb, WingTexture, pos, wingFrame, Color.White * a, 0f, wingOrigin, 2f);

            // 身体（NPC_636 第 2 帧）
            if (BodyTexture != null)
            {
                var bframe = Utils.Frame(BodyTexture, 1, 2, 0, 1, 0, 0);
                XiheDraw.Sprite(sb, BodyTexture, pos, bframe, new Color(1f, 1f, 0.8f) * a, 0f,
                    new Vector2(BodyTexture.Width, BodyTexture.Height / 2) / 2f, 1.1f);
            }
            else
            {
                var vanilla = TextureAssets.Npc[NPCID.HallowBoss]?.Value;
                if (vanilla != null)
                {
                    var bframe = Utils.Frame(vanilla, 1, 2, 0, 1, 0, 0);
                    XiheDraw.Sprite(sb, vanilla, pos, bframe, new Color(1f, 1f, 0.8f) * a, 0f,
                        Utils.Size(bframe) / 2f, 1.1f);
                }
            }
        }

        /// <summary>源 DrawArea：场地环，半径 realAreaRadius、宽 25、颜色 (255,255,150)*alpha、Additive。</summary>
        internal void PushAura()
        {
            var npc = NPC;
            if (npc == null || !npc.active) return;

            var player = npc.target >= 0 && npc.target < Main.player.Length ? Main.player[npc.target] : null;
            if (player == null || !player.active) return;

            var dist = Vector2.Distance(player.Center, _areaCenter);
            var dis = _realAreaRadius - dist;
            var a = MathHelper.Clamp(1f - dis / 300f, 0f, 1f);
            if (a <= 0.001f) return;

            XiheFx.Ring2(_areaCenter, _realAreaRadius, 12f, Tex.Wave, new Color(255, 255, 150) * a, true);
        }
    }
}


















