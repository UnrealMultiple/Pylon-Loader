using System;
using System.Linq;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;
using UVector2 = UnityEngine.Vector2;
using XVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;
using UColor = UnityEngine.Color;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss.Wand
{

    internal static class XiheWandFx
    {
        internal static ScreenGraphics.Shader Shader;

        private static ScreenGraphics.Texture _beamTex;
        private static ScreenGraphics.Texture _coreTex;
        private static ScreenGraphics.Texture _arcTex;
        private static ScreenGraphics.Texture _starTex;
        private static ScreenGraphics.Texture _ringStripTex;

        private static int _controllerType;
        private static int _pillarType;
        private static bool _typesReady;

        private static bool _hasCharge;
        private static bool _hasBeam;
        private static XVector2 _chargeCenter;
        private static XVector2 _beamCenter;
        private static XVector2 _impactWorld;
        private static float _chargeAlpha;
        private static float _beamScale;
        private static int _beamTimeLeft;
        private static int _controllerTimeLeft = 500;

        private static float _flash;
        private static float _sky;

        private const int MaxRings = 8;
        private const int PillarLife = 360;

        private struct RingFx
        {
            public bool Alive;
            public bool LaserOwned;
            public int TimeLeft;
            public int MaxTimeLeft;
            public float Ai0;
            public float OffsetY;
            public float Scale;
            public float WorldX;
            public float WorldY;
        }

        private static readonly RingFx[] _rings = new RingFx[MaxRings];

        private static readonly int[] RingSpawnAges = { 0, 20, 40, 110 };

        private static int _ringPillar = -1;
        private static int _ringAge = -1;
        private static int _ringSpawnMask;

        private static int _submittedFrame = -1;
        private static bool _loggedError;
        private static bool _loggedFirstSubmit;
        private static bool _probeChecked;
        private static bool _warmed;

        internal static void Probe()
        {
            try
            {
                if (Shader == null) return;
                var fx = new ScreenEffect(Shader)
                {
                    Layer = ScreenEffects.Layer.World,
                    Box = new[] { 0f, 0f, 0.001f, 0.001f },
                    Center = new UVector2(0.5f, 0.5f),
                    Intensity = 0f,
                };
                fx.Uniform("uTexOn", 0f);
                var frame = new ScreenFrame();
                frame.Add(fx);
                frame.Submit();
            }
            catch (Exception e) { XiheBossMod.Log?.Warn("[fx] 着色器探针提交失败：" + e.Message); }
        }

        internal static void BindTextures(Mod mod)
        {
            try { _beamTex = XiheWandTextures.Load(mod, "assets/textures/tex3.png", repeat: true); }
            catch (Exception e) { mod.Logger.Warn("[fx] tex3 绑定失败：" + e.Message); }
            try { _coreTex = XiheWandTextures.Load(mod, "assets/textures/tex0.png", repeat: true); }
            catch (Exception e) { mod.Logger.Warn("[fx] tex0 绑定失败：" + e.Message); }
            try { _arcTex = XiheWandTextures.Load(mod, "assets/textures/Arc.png", repeat: false); }
            catch (Exception e) { mod.Logger.Warn("[fx] Arc 绑定失败：" + e.Message); }
            try { _starTex = XiheWandTextures.Load(mod, "assets/textures/Extra89.png", repeat: false); }
            catch (Exception e) { mod.Logger.Warn("[fx] Extra_89 绑定失败：" + e.Message); }

            try { _ringStripTex = XiheWandTextures.Load(mod, "assets/textures/MagicRing.png", repeat: true); }
            catch (Exception e) { mod.Logger.Warn("[fx] MagicRing 绑定失败：" + e.Message); }

            mod.Logger.Info("[fx] MEAC 贴图：" +
                $"tex3={(_beamTex != null ? "ok" : "失败")}、tex0={(_coreTex != null ? "ok" : "失败")}、" +
                $"Arc={(_arcTex != null ? "ok" : "失败")}、Extra89={(_starTex != null ? "ok" : "失败")}、" +
                $"MagicRing={(_ringStripTex != null ? "ok" : "失败")}");
        }

        internal static void PushControllerVisual(Projectile p)
        {
            var lai = p.localAI;
            var alpha = MathHelper.Clamp(lai[0], 0f, 1f);
            if (alpha <= 0.01f) return;

            var tPos = new Vector2(lai[1], lai[2]);
            if (tPos == Vector2.Zero) return;

            var t = (float)Main.timeForVisualEffects;
            var center = p.Center;

            var c = new Color(1f, 0.9f, 0.7f, 0f) * 0.2f * (alpha + MathF.Sin(t * 0.6f) * 0.2f);
            var scaleFactor = 1f + alpha * 1.5f + MathF.Sin(t * 0.4f) * 0.2f + MathF.Sin(t * 0.5f) * 0.15f;
            var originStar = new Vector2(XiheTextures.WidthOf(Tex.Extra89), XiheTextures.HeightOf(Tex.Extra89)) / 2f;
            for (var i = -3; i < 3; i++)
            {
                XiheFx.Sprite(center + new Vector2(0f, i * 3f), Tex.Extra89, c, 0f,
                    new Vector2(0.5f + alpha * 0.1f, scaleFactor * 0.6f), originStar, additive: true);
                XiheFx.Sprite(center + new Vector2(i * 3f, 0f), Tex.Extra89, c, 1.57f,
                    new Vector2(0.5f + alpha * 0.1f, scaleFactor), originStar, additive: true);
            }

            var dir = XiheHelper.DirectionTo(center, tPos);
            if (dir == Vector2.Zero) dir = -Vector2.UnitY;
            var flareAlpha = (alpha + MathF.Sin(t * 0.8f) * 0.1f) * 0.4f;
            var rot = (float)Math.Atan2(dir.Y, dir.X);
            var originArc = new Vector2(XiheTextures.WidthOf(Tex.Arc), XiheTextures.HeightOf(Tex.Arc)) / 2f;

            var flareColors = new[]
            {
                new Color(1f, 0f, 0f, 0f),
                new Color(0f, 0.8f, 0f, 0f),
                new Color(0f, 0f, 0.6f, 0f),
            };
            for (var j = 0; j < 3; j++)
            {
                XiheFx.Sprite(center + dir * (90f + j * 3f), Tex.Arc, flareColors[j] * flareAlpha,
                    rot, Vector2.One, originArc, additive: true);
                XiheFx.Sprite(center - dir * (90f + j * 3f), Tex.Arc, flareColors[j] * flareAlpha,
                    rot + 3.14f, Vector2.One, originArc, additive: true);
            }
        }

        internal static void Reset()
        {
            _hasCharge = false;
            _hasBeam = false;
            _chargeAlpha = 0f;
            _beamScale = 0f;
            _beamTimeLeft = 0;
            _controllerTimeLeft = 500;
            _flash = 0f;
            _sky = 0f;
            _submittedFrame = -1;
            _warmed = false;
            if (ScreenEffects.Find(EffectKey) is { } live) live.Kill();
            ResetRings(-1);
        }

        private static void WarmupSubmit()
        {
            try
            {
                if (Shader == null) return;
                var fx = new ScreenEffect(Shader)
                {
                    Layer = ScreenEffects.Layer.World,
                    Box = new[] { 0f, 0f, 0.5f, 0.5f },
                    Center = new UVector2(0.5f, 0.5f),
                    Intensity = 0f,
                };
                fx.Uniform("uCharge", 0f);
                fx.Uniform("uBeam", 0f);
                fx.Uniform("uFlash", 0f);
                fx.Uniform("uSky", 0f);
                fx.Uniform("uShake", 0f);
                fx.Uniform("uImpact", 0f);
                fx.Uniform("uBeamWidth", 0.01f);
                fx.Uniform("uBeamX", 0.5f);
                fx.Uniform("uRingCount", 0);
                fx.Uniform("uRingViewZ", 0.5f);
                fx.Uniform("uRingHalo", 0.005f);
                fx.Uniform("uTexOn", _beamTex != null && _coreTex != null && _arcTex != null &&
                                     _starTex != null && _ringStripTex != null ? 1f : 0f);
                if (_beamTex != null) fx.Texture("uBeamTex", _beamTex);
                if (_coreTex != null) fx.Texture("uCoreTex", _coreTex);
                if (_arcTex != null) fx.Texture("uArcTex", _arcTex);
                if (_starTex != null) fx.Texture("uStarTex", _starTex);
                if (_ringStripTex != null) fx.Texture("uRingStrip", _ringStripTex);

                var frame = new ScreenFrame();
                frame.Add(fx);
                frame.Submit();
            }
            catch (Exception e) { XiheBossMod.Log?.Warn("[fx] 预热提交失败：" + e.Message); }
        }

        internal static void Update()
        {
            try
            {
                if (Main.gameMenu) { Reset(); return; }

                if (!_warmed)
                {
                    _warmed = true;
                    WarmupSubmit();
                }

                if (!_probeChecked && Shader != null)
                {
                    var state = Shader.Status;
                    if (state != ScreenGraphics.ShaderStatus.Pending)
                    {
                        _probeChecked = true;
                        if (state == ScreenGraphics.ShaderStatus.Failed)
                            XiheBossMod.Log?.Error("[fx] 着色器编译失败：" + Shader.Error);
                        else
                            XiheBossMod.Log?.Info("[fx] 着色器编译通过");
                    }
                }

                ResolveTypes();
                _hasCharge = false;
                _hasBeam = false;
                _chargeAlpha = 0f;
                _beamScale = 0f;

                var chosen = -1;
                if (_typesReady)
                {
                    var local = Main.myPlayer;

                    for (var i = 0; i < Main.projectile.Length; i++)
                    {
                        var p = Main.projectile[i];
                        if (p == null || !p.active) continue;
                        if (p.type != _controllerType || p.owner != local) continue;

                        _chargeCenter = p.Center;
                        var alpha = p.localAI[0];
                        if (alpha > _chargeAlpha) _chargeAlpha = alpha;
                        _controllerTimeLeft = p.timeLeft;
                        _hasCharge = true;
                    }

                    var best = -1f;
                    for (var i = 0; i < Main.projectile.Length; i++)
                    {
                        var p = Main.projectile[i];
                        if (p == null || !p.active || p.type != _pillarType || p.owner != local) continue;
                        if (p.scale <= best) continue;
                        best = p.scale;
                        chosen = i;
                    }
                    if (chosen < 0)
                    {
                        best = -1f;
                        for (var i = 0; i < Main.projectile.Length; i++)
                        {
                            var p = Main.projectile[i];
                            if (p == null || !p.active || p.type != _pillarType) continue;
                            if (p.scale <= best) continue;
                            best = p.scale;
                            chosen = i;
                        }
                    }
                }

                if (chosen >= 0)
                {
                    var p = Main.projectile[chosen];
                    _beamCenter = p.Center;
                    _impactWorld = new XVector2(p.Center.X, p.Center.Y + 300f);
                    _beamScale = p.scale;
                    _beamTimeLeft = p.timeLeft;
                    _hasBeam = true;

                    AdvanceRings(chosen, p);
                }
                else
                {

                    StepRingsWithoutOwner();
                }

                var skyTarget = _hasCharge ? MathHelper.Clamp((500f - _controllerTimeLeft) / 200f, 0f, 1f) : 0f;
                if (_hasBeam) skyTarget = 1f;
                _sky += (skyTarget - _sky) * 0.06f;
                if (_sky < 0.002f) _sky = 0f;

                if (_flash > 0f)
                {
                    _flash -= 0.05f;
                    if (_flash < 0f) _flash = 0f;
                }
            }
            catch (Exception e)
            {
                if (!_loggedError) { _loggedError = true; XiheBossMod.Log?.Warn("[fx] 状态更新异常：" + e.Message); }
            }
        }

        private static void AdvanceRings(int index, Projectile pillar)
        {
            var age = PillarLife - pillar.timeLeft;
            if (age < 0) age = 0;

            if (_ringPillar != index || (_ringAge >= 0 && age + 5 < _ringAge))
                ResetRings(index);
            _ringAge = age;

            var center = pillar.Center;
            var timeLeft = pillar.timeLeft;

            for (var k = 0; k < RingSpawnAges.Length; k++)
            {
                if ((_ringSpawnMask & (1 << k)) != 0) continue;
                if (age < RingSpawnAges[k]) continue;

                _ringSpawnMask |= 1 << k;
                SpawnRings(k, center);
            }

            StepRings(center, timeLeft, true);
        }

        private static void ResetRings(int pillarIndex)
        {
            Array.Clear(_rings, 0, _rings.Length);
            _ringPillar = pillarIndex;
            _ringAge = -1;
            _ringSpawnMask = 0;
        }

        private static void StepRingsWithoutOwner()
        {
            if (_ringPillar < 0) return;

            var any = false;
            for (var i = 0; i < _rings.Length; i++)
            {
                if (!_rings[i].Alive) continue;
                any = true;
                break;
            }
            if (!any)
            {
                ResetRings(-1);
                return;
            }

            StepRings(default, 0, false);
        }

        private static void SpawnRings(int schedule, XVector2 center)
        {
            switch (schedule)
            {
                case 0:
                    AddRing(0f, -0.3f, 110, false, center);
                    break;
                case 1:
                    AddRing(200f, -0.6f, 90, false, center);
                    break;
                case 2:
                    AddRing(400f, -0.1f, 70, false, center);
                    break;
                case 3:
                    AddRing(0f, -0.15f, 250, true, center);
                    AddRing(200f, -0.4f, 250, true, center);
                    AddRing(400f, 0.1f, 250, true, center);
                    break;
            }
        }

        private static void AddRing(float offsetY, float ai0Delta, int life, bool laserOwned, XVector2 center)
        {
            for (var i = 0; i < _rings.Length; i++)
            {
                if (_rings[i].Alive) continue;

                _rings[i] = new RingFx
                {
                    Alive = true,
                    LaserOwned = laserOwned,
                    TimeLeft = life,
                    MaxTimeLeft = life,
                    Ai0 = 1f + ai0Delta,
                    OffsetY = offsetY,
                    Scale = 0f,
                    WorldX = center.X,
                    WorldY = center.Y + offsetY,
                };
                return;
            }
        }

        private static void StepRings(XVector2 ownerCenter, int pillarTimeLeft, bool pillarAlive)
        {
            for (var i = 0; i < _rings.Length; i++)
            {
                var r = _rings[i];
                if (!r.Alive) continue;

                if (r.LaserOwned && pillarAlive)
                {
                    if (r.TimeLeft > r.MaxTimeLeft - 20)
                        r.Scale = SmoothStep(r.Scale, 4f, 0.2f);
                    else if (pillarTimeLeft > 20 && r.TimeLeft > 15)
                        r.TimeLeft = 20;
                    if (pillarTimeLeft <= 20)
                        r.TimeLeft = 15;

                    var num = r.TimeLeft;
                    if (num > 10 && num < 15)
                        r.Scale = SmoothStep(r.Scale, 5f, 0.2f);
                    if (r.TimeLeft < 10)
                        r.Scale = SmoothStep(r.Scale, 0f, 0.35f);
                }
                else
                {
                    if (r.TimeLeft > r.MaxTimeLeft - 20)
                        r.Scale = SmoothStep(r.Scale, 4f, 0.2f);

                    var num = r.TimeLeft;
                    if (num > 10 && num < 20)
                        r.Scale = SmoothStep(r.Scale, 5f, 0.2f);
                    if (r.TimeLeft < 10)
                        r.Scale = SmoothStep(r.Scale, 0f, 0.35f);
                }

                if (pillarAlive)
                {
                    r.WorldX = ownerCenter.X;
                    r.WorldY = ownerCenter.Y + r.OffsetY;
                }

                r.TimeLeft--;
                if (r.TimeLeft <= 0) r.Alive = false;

                _rings[i] = r;
            }
        }

        private static float SmoothStep(float from, float to, float amount)
        {
            if (amount < 0f) amount = 0f;
            else if (amount > 1f) amount = 1f;
            amount = amount * amount * (3f - 2f * amount);
            return from + (to - from) * amount;
        }

        private static bool HasRings()
        {
            for (var i = 0; i < _rings.Length; i++)
            {
                if (!_rings[i].Alive) continue;
                if (_rings[i].Scale > 0.001f) return true;
            }
            return false;
        }

        private static void ResolveTypes()
        {
            if (_typesReady) return;
            _controllerType = ModContent.ProjectileType<XiheMagicControler>();
            _pillarType = ModContent.ProjectileType<XiheLaserPillar>();
            _typesReady = _controllerType > 0 && _pillarType > 0;
        }

        private const string EffectKey = "XiheBoss:beam";

        internal static void Submit()
        {
            try
            {
                if (Main.gameMenu || Shader == null) return;

                var active = _hasCharge || _hasBeam || HasRings() || _flash > 0.001f || _sky > 0.01f;
                if (!active)
                {
                    if (ScreenEffects.Find(EffectKey) is { } stale) stale.Kill();
                    return;
                }

                var frame = UnityEngine.Time.frameCount;
                if (_submittedFrame == frame) return;
                _submittedFrame = frame;

                if (!_loggedFirstSubmit)
                {
                    _loggedFirstSubmit = true;
                    try
                    {
                        var status = Shader.Status;
                        XiheBossMod.Log?.Info($"[fx] 首次提交：shader={status}");
                        if (status == ScreenGraphics.ShaderStatus.Failed)
                            XiheBossMod.Log?.Error("[fx] 着色器编译失败：" + Shader.Error);
                    }
                    catch (Exception e) { XiheBossMod.Log?.Warn("[fx] 查询着色器状态失败：" + e.Message); }
                }

                if (ScreenEffects.Find(EffectKey) is XiheBeamEffect { Active: true }) return;
                ScreenEffects.Spawn<XiheBeamEffect>(fx =>
                {
                    fx.Key = EffectKey;
                    fx.Layer = ScreenEffects.Layer.World;
                    fx.Fullscreen = true;
                });
            }
            catch (Exception e)
            {
                if (!_loggedError) { _loggedError = true; XiheBossMod.Log?.Warn("[fx] 提交失败：" + e.Message); }
            }
        }

        private static void DrawBeam(ScreenCommands cmd, ScreenGraphics.Shader shader)
        {
            var sw = Main.screenWidth;
            var sh = Main.screenHeight;
            if (sw <= 0 || sh <= 0) return;

            var screen = Main.screenPosition;
            var beamX = _hasBeam ? (_beamCenter.X - screen.X) / sw : 0f;
            var impact = _hasBeam ? 1f - (_impactWorld.Y - screen.Y) / sh : 0f;

            var chargeX = _hasCharge ? (_chargeCenter.X - screen.X) / sw : beamX;
            var chargeY = _hasCharge ? 1f - (_chargeCenter.Y - screen.Y) / sh : 0.5f;

            var shake = 0f;
            if (_hasBeam && _beamTimeLeft < 245 && _beamTimeLeft > 20)
            {
                var screenCenter = new XVector2(screen.X + sw * 0.5f, screen.Y + sh * 0.5f);
                var distToCenter = Vector2.Distance(_beamCenter, screenCenter);
                shake = 8f * Math.Max(0f, 1f - distToCenter / 2000f) / sh;
            }

            var beam = 0f;
            if (_hasBeam && _beamScale > 0.01f)
                beam = 0.55f + 0.45f * MathHelper.Clamp(_beamScale / 3.16f, 0f, 1f);
            var charge = _hasCharge ? MathHelper.Clamp(_chargeAlpha, 0f, 1f) : 0f;

            var options = new ScreenDrawOptions();
            options.Uniforms["uCharge"] = charge;
            options.Uniforms["uBeam"] = beam;
            options.Uniforms["uFlash"] = _flash;
            options.Uniforms["uSky"] = _sky;
            options.Uniforms["uShake"] = shake;
            options.Uniforms["uImpact"] = MathHelper.Clamp(impact, -0.5f, 1.5f);
            options.Uniforms["uBeamWidth"] = MathHelper.Clamp(_beamScale * 128f / sh, 0.002f, 0.5f);
            options.Uniforms["uBeamX"] = MathHelper.Clamp(beamX, -0.5f, 1.5f);
            options.Uniforms["uTexOn"] = _beamTex != null && _coreTex != null && _arcTex != null &&
                                         _starTex != null && _ringStripTex != null ? 1f : 0f;
            options.Uniforms["uCenter"] = new XVector2(
                MathHelper.Clamp(chargeX, 0f, 1f), MathHelper.Clamp(chargeY, 0f, 1f));

            var ringData = new float[MaxRings * 4];
            var ringCount = 0;
            for (var i = 0; i < _rings.Length && ringCount < MaxRings; i++)
            {
                var r = _rings[i];
                if (!r.Alive) continue;

                var radius = 110f * r.Scale * r.Ai0;
                if (radius < 0.5f) continue;

                ringData[ringCount * 4 + 0] = (r.WorldX - screen.X) / sw;
                ringData[ringCount * 4 + 1] = 1f - (r.WorldY - screen.Y) / sh;
                ringData[ringCount * 4 + 2] = radius / sh;
                ringData[ringCount * 4 + 3] = 10f * r.Scale * r.Ai0 / sh;
                ringCount++;
            }

            if (ringCount > 0)
                options.Uniforms["uRings"] = ScreenGraphics.Array(4, ringData.Take(ringCount * 4));
            options.Uniforms["uRingCount"] = ringCount;
            options.Uniforms["uRingViewZ"] = 1000f / sh;

            options.Uniforms["uRingHalo"] = 3f / sh;

            if (_beamTex != null) options.Textures["uBeamTex"] = _beamTex;
            if (_coreTex != null) options.Textures["uCoreTex"] = _coreTex;
            if (_arcTex != null) options.Textures["uArcTex"] = _arcTex;
            if (_starTex != null) options.Textures["uStarTex"] = _starTex;
            if (_ringStripTex != null) options.Textures["uRingStrip"] = _ringStripTex;

            cmd.DrawShader(shader, options);
        }

        public sealed class XiheBeamEffect : ModScreenEffect
        {
            public override string ShaderPath => "assets/effects/XiheBeam.frag";

            public override void SetDefaults()
            {
                Layer = ScreenEffects.Layer.World;
                Fullscreen = true;
            }

            public override void Draw(ScreenCommands cmd) => DrawBeam(cmd, Shader!);
        }
    }
}
