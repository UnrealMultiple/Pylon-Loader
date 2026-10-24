using System;
using System.IO;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Pylon.ModLoader.Textures;
using Terraria;
using UVector2 = UnityEngine.Vector2;
using XVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;
using UColor = UnityEngine.Color;

namespace XiheMagic
{
    internal static class XiheFx
    {
        internal static ScreenShader Shader;

        // MEAC 原素材（从 MEAC 文件夹拷进来的那张图，不是新画的）。
        private static ScreenTexture _beamTex;   // Images/tex3（外层柱）
        private static ScreenTexture _coreTex;   // Images/tex0（内层柱）
        private static ScreenTexture _arcTex;    // Items/ModWeapons/Arc（蓄力耀斑）
        private static ScreenTexture _starTex;   // Images/Extra_89（十字辉光）
        private static ScreenTexture _ringTex;   // Projectiles/Circle（落点柔光）
        private static ScreenTexture _ringStripTex; // Images/MagicCircle/tex1（原版环带条）
        private static ScreenTexture _ringAnnulus;  // tex1 卷成的环形图（文字/白线已烘焙）

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

        // MagicRing 是**生成时**在世界里定死的；不能跟着柱子/鼠标动。
        private static XVector2 _ringEarlyWorld;
        private static XVector2 _ringLateWorld;
        private static bool _ringLateCaptured;

        private static int _submittedFrame = -1;
        private static bool _loggedError;
        private static bool _loggedFirstSubmit;
        private static bool _probeChecked;

        /// <summary>启动时提交一个 1px 的 pass，让内核立刻编译着色器；错误在 Update 里读出来。</summary>
        internal static void Probe()
        {
            try
            {
                if (Shader == null) return;
                var fx = new ScreenEffect(Shader)
                {
                    Layer = ScreenEffectLayer.World,
                    Box = new[] { 0f, 0f, 0.001f, 0.001f },
                    Center = new UVector2(0.5f, 0.5f),
                    Intensity = 0f,
                };
                fx.Uniform("uTexOn", 0f);
                var frame = new ScreenFrame();
                frame.Add(fx);
                frame.Submit();
            }
            catch (Exception e) { XiheMagicMod.Log?.Warn("[fx] 着色器探针提交失败：" + e.Message); }
        }

        /// <summary>把 MEAC 的贴图登记进屏幕效果纹理表（只能游戏线程：SetupContent）。</summary>
        internal static void BindTextures(Mod mod)
        {
            try { _beamTex = XiheTextures.Load(mod, "assets/textures/tex3.png", repeat: true); }
            catch (Exception e) { mod.Logger.Warn("[fx] tex3 绑定失败：" + e.Message); }
            try { _coreTex = XiheTextures.Load(mod, "assets/textures/tex0.png", repeat: true); }
            catch (Exception e) { mod.Logger.Warn("[fx] tex0 绑定失败：" + e.Message); }
            try { _arcTex = XiheTextures.Load(mod, "assets/textures/Arc.png", repeat: false); }
            catch (Exception e) { mod.Logger.Warn("[fx] Arc 绑定失败：" + e.Message); }
            try { _starTex = XiheTextures.Load(mod, "assets/textures/Extra89.png", repeat: false); }
            catch (Exception e) { mod.Logger.Warn("[fx] Extra_89 绑定失败：" + e.Message); }
            try { _ringTex = XiheTextures.Load(mod, "assets/textures/Circle.png", repeat: false); }
            catch (Exception e) { mod.Logger.Warn("[fx] Circle 绑定失败：" + e.Message); }
            try { _ringStripTex = XiheTextures.Load(mod, "assets/textures/MagicRing.png", repeat: true); }
            catch (Exception e) { mod.Logger.Warn("[fx] MagicRing 绑定失败：" + e.Message); }
            try { _ringAnnulus = BuildRingAnnulus(mod, "assets/textures/MagicRing.png", 1024); }
            catch (Exception e) { mod.Logger.Warn("[fx] 环带卷图失败：" + e.Message); }

            mod.Logger.Info("[fx] MEAC 贴图：" +
                $"tex3={(_beamTex != null ? "ok" : "失败")}、tex0={(_coreTex != null ? "ok" : "失败")}、" +
                $"Arc={(_arcTex != null ? "ok" : "失败")}、Extra89={(_starTex != null ? "ok" : "失败")}、" +
                $"Circle={(_ringTex != null ? "ok" : "失败")}、MagicRing={(_ringStripTex != null ? "ok" : "失败")}、" +
                $"Annulus={(_ringAnnulus != null ? "ok" : "失败")}");
        }

        /// <summary>
        /// 把 MEAC 的 MagicCircle/tex1（600x70 横条）卷成一张环形图（文字 + 上下白线一起烘焙）。
        /// 这样 shader 只需要按屏幕坐标采一张环图，不会再把文字拉成矩形。
        /// </summary>
        private static ScreenTexture BuildRingAnnulus(Mod mod, string path, int size)
        {
            var full = PylonTexture.Resolve(mod, path);
            var src = XiheTextures.DecodePng(File.ReadAllBytes(full), out var sw, out var sh);
            var rgba = new byte[size * size * 4];

            var c = (size - 1) * 0.5f;
            const float inner = 0.86f;   // 环带加粗（原版 0.10R，这里 0.14R）
            const float band = 0.14f;

            for (var y = 0; y < size; y++)
            {
                var dy = (y - c) / c;
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - c) / c;
                    var r = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (r < inner - 0.003f || r > 1.003f) continue;

                    var ang = (float)Math.Atan2(dy, dx);
                    if (ang < 0) ang += (float)(Math.PI * 2.0);
                    var u = ang / (float)(Math.PI * 2.0) * 3f;   // MagicRing: sX = 3
                    u -= (float)Math.Floor(u);
                    var v = Math.Min(1f, Math.Max(0f, (r - inner) / band));

                    var sx = u * (sw - 1);
                    var sy = v * (sh - 1);
                    var x0 = (int)sx;
                    var y0 = (int)sy;
                    var x1 = Math.Min(sw - 1, x0 + 1);
                    var y1 = Math.Min(sh - 1, y0 + 1);
                    var fx = sx - x0;
                    var fy = sy - y0;

                    var p00 = (y0 * sw + x0) * 4;
                    var p10 = (y0 * sw + x1) * 4;
                    var p01 = (y1 * sw + x0) * 4;
                    var p11 = (y1 * sw + x1) * 4;

                    var o = (y * size + x) * 4;
                    for (var k = 0; k < 3; k++)
                    {
                        var a = src[p00 + k] * (1 - fx) + src[p10 + k] * fx;
                        var b = src[p01 + k] * (1 - fx) + src[p11 + k] * fx;
                        rgba[o + k] = (byte)Math.Min(255f, a * (1 - fy) + b * fy);
                    }
                    rgba[o + 3] = 255;
                }
            }

            return XiheTextures.LoadRaw("xihe-ring-annulus", size, size, rgba, repeat: false);
        }

        /// <summary>激光柱「炸开」的那一帧调一次（闪白）。</summary>
        internal static void NotifyFlash() => _flash = 1f;

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
            _ringLateCaptured = false;
            _ringEarlyWorld = default;
            _ringLateWorld = default;
            _submittedFrame = -1;
        }

        /// <summary>每帧一次（世界更新之后）：找弹幕、推进闪白/天幕。</summary>
        internal static void Update()
        {
            try
            {
                if (Main.gameMenu) { Reset(); return; }

                if (!_probeChecked && Shader != null)
                {
                    var state = Shader.Status;
                    if (state != ScreenShaderStatus.Pending)
                    {
                        _probeChecked = true;
                        if (state == ScreenShaderStatus.Failed)
                            XiheMagicMod.Log?.Error("[fx] 着色器编译失败：" + Shader.Error);
                        else
                            XiheMagicMod.Log?.Info("[fx] 着色器编译通过");
                    }
                }

                ResolveTypes();
                var hadBeam = _hasBeam;
                _hasCharge = false;
                _hasBeam = false;
                _chargeAlpha = 0f;
                _beamScale = 0f;

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

                    // 优先画自己的激光柱；没有才画别人的（多人大招同时在场时只画最粗的一根）。
                    var chosen = -1;
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

                    if (chosen >= 0)
                    {
                        var p = Main.projectile[chosen];
                        _beamCenter = p.Center;
                        _impactWorld = new XVector2(p.Center.X, p.Center.Y + 300f);
                        _beamScale = p.scale;
                        _beamTimeLeft = p.timeLeft;
                        _hasBeam = true;

                        // 环在生成时固定：小环 = 刚发现柱子那一帧；大环 = 点火那一帧。
                        var age = 360 - p.timeLeft;
                        if (!hadBeam)
                        {
                            _ringEarlyWorld = p.Center;
                            _ringLateWorld = p.Center;
                            _ringLateCaptured = false;
                        }
                        if (!_ringLateCaptured && age >= 110)
                        {
                            _ringLateWorld = p.Center;
                            _ringLateCaptured = true;
                        }
                    }
                }

                // 引导越久天幕越亮（对应 MEAC 的 AmazingBugSky.intensity += 0.005）。
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
                if (!_loggedError) { _loggedError = true; XiheMagicMod.Log?.Warn("[fx] 状态更新异常：" + e.Message); }
            }
        }

        private static void ResolveTypes()
        {
            if (_typesReady) return;
            _controllerType = ModContent.ProjectileType<XiheMagicControler>();
            _pillarType = ModContent.ProjectileType<XiheLaserPillar>();
            _typesReady = _controllerType > 0 && _pillarType > 0;
        }

        /// <summary>提交这一帧（Main.DoDraw 之前；或 PostUpdateEverything 兜底）。</summary>
        internal static void Submit()
        {
            try
            {
                if (Main.gameMenu || Shader == null) return;
                if (!_hasCharge && !_hasBeam && _flash <= 0.001f && _sky <= 0.01f) return;

                var frame = UnityEngine.Time.frameCount;
                if (_submittedFrame == frame) return;
                _submittedFrame = frame;

                if (!_loggedFirstSubmit)
                {
                    _loggedFirstSubmit = true;
                    try
                    {
                        var status = Shader.Status;
                        XiheMagicMod.Log?.Info($"[fx] 首次提交：shader={status}");
                        if (status == ScreenShaderStatus.Failed)
                            XiheMagicMod.Log?.Error("[fx] 着色器编译失败：" + Shader.Error);
                    }
                    catch (Exception e) { XiheMagicMod.Log?.Warn("[fx] 查询着色器状态失败：" + e.Message); }
                }

                var sw = Main.screenWidth;
                var sh = Main.screenHeight;
                if (sw <= 0 || sh <= 0) return;

                var screen = Main.screenPosition;
                var beamX = _hasBeam ? (_beamCenter.X - screen.X) / sw : 0f;
                var impact = _hasBeam ? 1f - (_impactWorld.Y - screen.Y) / sh : 0f;
                var half = _hasBeam ? Math.Max(0f, _beamScale * 90f / sh) : 0f;

                var chargeX = _hasCharge ? (_chargeCenter.X - screen.X) / sw : beamX;
                var chargeY = _hasCharge ? 1f - (_chargeCenter.Y - screen.Y) / sh : 0.5f;

                var shake = 0f;
                if (_hasBeam && _beamTimeLeft < 245 && _beamTimeLeft > 20) shake = 8f / sh;
                else if (_flash > 0.01f) shake = 3f * _flash / sh;

                var beam = 0f;
                if (_hasBeam && _beamScale > 0.01f)
                    beam = 0.55f + 0.45f * MathHelper.Clamp(_beamScale / 3.16f, 0f, 1f);   // 细柱阶段也要看得见
                var charge = _hasCharge ? MathHelper.Clamp(_chargeAlpha, 0f, 1f) : 0f;
                var beamCenterV = _hasBeam ? 1f - (_beamCenter.Y - screen.Y) / sh : 0.5f;
                var age = _hasBeam ? MathHelper.Clamp(360 - _beamTimeLeft, 0f, 400f) : 0f;

                var fx = new ScreenEffect(Shader)
                {
                    Layer = ScreenEffectLayer.World,
                    Center = new UVector2(MathHelper.Clamp(chargeX, 0f, 1f), MathHelper.Clamp(chargeY, 0f, 1f)),
                    Radius = 0.4f,
                    Intensity = 1f,
                    Color = new UColor(1f, 0.8f, 0.35f, 1f),
                };
                fx.Uniform("uCharge", charge);
                fx.Uniform("uBeam", beam);
                fx.Uniform("uFlash", _flash);
                fx.Uniform("uSky", _sky);
                fx.Uniform("uShake", shake);
                fx.Uniform("uImpact", MathHelper.Clamp(impact, -0.5f, 1.5f));
                fx.Uniform("uHalfWidth", MathHelper.Clamp(half, 0.0015f, 0.35f));
                fx.Uniform("uBeamWidth", MathHelper.Clamp(_beamScale * 128f / sh, 0.002f, 0.5f));
                fx.Uniform("uBeamX", MathHelper.Clamp(beamX, -0.5f, 1.5f));
                fx.Uniform("uBeamCenterV", MathHelper.Clamp(beamCenterV, -0.5f, 1.5f));
                fx.Uniform("uBeamAge", age);
                var earlyX = _hasBeam ? (_ringEarlyWorld.X - screen.X) / sw : beamX;
                var earlyV = _hasBeam ? 1f - (_ringEarlyWorld.Y - screen.Y) / sh : 0.5f;
                var lateX = _hasBeam ? (_ringLateWorld.X - screen.X) / sw : beamX;
                var lateV = _hasBeam ? 1f - (_ringLateWorld.Y - screen.Y) / sh : 0.5f;
                fx.Uniform("uRingEarlyX", MathHelper.Clamp(earlyX, -0.5f, 1.5f));
                fx.Uniform("uRingEarlyV", MathHelper.Clamp(earlyV, -0.5f, 1.5f));
                fx.Uniform("uRingLateX", MathHelper.Clamp(lateX, -0.5f, 1.5f));
                fx.Uniform("uRingLateV", MathHelper.Clamp(lateV, -0.5f, 1.5f));
                fx.Uniform("uTexOn", _beamTex != null && _coreTex != null && _arcTex != null &&
                                     _starTex != null && _ringAnnulus != null ? 1f : 0f);

                if (_beamTex != null) fx.Texture("uBeamTex", _beamTex);
                if (_coreTex != null) fx.Texture("uCoreTex", _coreTex);
                if (_arcTex != null) fx.Texture("uArcTex", _arcTex);
                if (_starTex != null) fx.Texture("uStarTex", _starTex);
                if (_ringAnnulus != null) fx.Texture("uRingAnnulus", _ringAnnulus);

                var screenFrame = new ScreenFrame();
                screenFrame.Add(fx);
                screenFrame.Submit();
            }
            catch (Exception e)
            {
                if (!_loggedError) { _loggedError = true; XiheMagicMod.Log?.Warn("[fx] 提交失败：" + e.Message); }
            }
        }
    }
}


