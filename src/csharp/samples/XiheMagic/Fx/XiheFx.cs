using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
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
        private static ScreenTexture _ringStripTex; // Images/MagicCircle/tex1（MagicRing 的 600x70 环带条）

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

        // ---------------- MagicRing 的独立视觉特效（与 MEAC 的 VisualEffect 列表同构）----------------
        //
        // MEAC 里 XiheMagicControler 只在两个时刻往 EoLBigLaser_Friendly 的 AI 里塞环：
        //   timeLeft 360 / 340 / 320：CreateRing(0/200/400, ai0 0.7/0.4/0.9, 寿命 110/90/70)
        //   timeLeft 250：CreateRing(0/200/400, ai0 0.85/0.6/1.1, 寿命 250, laserOwner=柱)
        // 每个环是一个独立实体：自己 AI（SmoothStep 展开/收尾）、跟着柱子走、柱子死了还会自己
        // 收尾 10 多 tick。所以这里照着 MEAC.Entities.MagicRing 的状态机逐 tick 复刻，
        // 而不是在 shader 里用 age 猜。

        private const int MaxRings = 8;
        private const int PillarLife = 360;

        private struct RingFx
        {
            public bool Alive;
            public bool LaserOwned;
            public int TimeLeft;
            public int MaxTimeLeft;
            public float Ai0;        // MagicRing.ai0 = 1 + CreateRing 的 scale 参数
            public float OffsetY;    // 相对柱心的世界 y 偏移
            public float Scale;      // MagicRing.scale
            public float WorldX;
            public float WorldY;
        }

        private static readonly RingFx[] _rings = new RingFx[MaxRings];

        // MEAC EoLBigLaser_Friendly.AI 里 CreateRing 的四个时刻（age = 360 - timeLeft）。
        private static readonly int[] RingSpawnAges = { 0, 20, 40, 110 };

        private static int _ringPillar = -1;
        private static int _ringAge = -1;
        private static int _ringSpawnMask;

        private static int _submittedFrame = -1;
        private static bool _loggedError;
        private static bool _loggedFirstSubmit;
        private static bool _probeChecked;
        private static bool _warmed;

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
            // MagicRing 用的是 MEAC/Images/MagicCircle/tex1（600x70）：横条按 sX=3 绕圆，
            // 现在直接把原图交给 shader，由 shader 按 MEAC 的 3D 投影反解出 (θ, v) 再采样。
            try { _ringStripTex = XiheTextures.Load(mod, "assets/textures/MagicRing.png", repeat: true); }
            catch (Exception e) { mod.Logger.Warn("[fx] MagicRing 绑定失败：" + e.Message); }

            mod.Logger.Info("[fx] MEAC 贴图：" +
                $"tex3={(_beamTex != null ? "ok" : "失败")}、tex0={(_coreTex != null ? "ok" : "失败")}、" +
                $"Arc={(_arcTex != null ? "ok" : "失败")}、Extra89={(_starTex != null ? "ok" : "失败")}、" +
                $"MagicRing={(_ringStripTex != null ? "ok" : "失败")}");
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
            _submittedFrame = -1;
            _warmed = false;
            ResetRings(-1);
        }

        /// <summary>
        /// 预热：半屏「原样拷贝」的 pass —— uBeam/uCharge/uSky/uFlash 全 0、没有环，
        /// 输出就是屏幕本身（看不出任何变化），但会把着色器和 5 张贴图真正跑一遍。
        /// </summary>
        private static void WarmupSubmit()
        {
            try
            {
                if (Shader == null) return;
                var fx = new ScreenEffect(Shader)
                {
                    Layer = ScreenEffectLayer.World,
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
            catch (Exception e) { XiheMagicMod.Log?.Warn("[fx] 预热提交失败：" + e.Message); }
        }

        /// <summary>每 tick 一次（世界更新之后）：找弹幕、推进环/闪白/天幕。</summary>
        internal static void Update()
        {
            try
            {
                if (Main.gameMenu) { Reset(); return; }

                // 进世界的第一个 tick：先跑一次「什么都没加」的 pass，把着色器和 5 张贴图
                // 在渲染线程上热起来（纹理是首次绑定才上传的）。不做的话，第一次放技能那一帧
                // 才去建纹理/走动渲染路径，会出现「闪一下光环就没了」。
                if (!_warmed)
                {
                    _warmed = true;
                    WarmupSubmit();
                }

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

                    // 优先画自己的激光柱；没有才画别人的（多人大招同时在场时只画最粗的一根）。
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
                    // 柱子没了：MagicRing 是独立实体，还要自己收尾（MEAC 里它们不属于柱子的绘制）。
                    StepRingsWithoutOwner();
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

        // ---------------- MagicRing 状态机（MEAC.Entities.MagicRing）----------------

        /// <summary>把柱子这一 tick 的状态喂给环的模拟（含首次观测到的补发）。</summary>
        private static void AdvanceRings(int index, Projectile pillar)
        {
            var age = PillarLife - pillar.timeLeft;
            if (age < 0) age = 0;

            // 同一个 whoAmI 上换了一根新柱子（timeLeft 从 360 重来）→ 环重新来。
            // 注意 age 从 130 跳到 340（控制器断掉 → 柱子 timeLeft=20）不是重置，不能清环。
            if (_ringPillar != index || (_ringAge >= 0 && age + 5 < _ringAge))
                ResetRings(index);
            _ringAge = age;

            var center = pillar.Center;
            var timeLeft = pillar.timeLeft;

            // 1) 生成：按时刻表补发（MEAC 的时间点一到就 CreateRing）。
            for (var k = 0; k < RingSpawnAges.Length; k++)
            {
                if ((_ringSpawnMask & (1 << k)) != 0) continue;
                if (age < RingSpawnAges[k]) continue;

                _ringSpawnMask |= 1 << k;
                SpawnRings(k, center);
            }

            // 2) 每 tick 一步 AI。VisualEffect.Update 是每 tick 都跑的，与柱子 timeLeft 无关
            //    （柱子续命时 timeLeft 会一直停在 229/230，这里不能按时长差推进）。
            StepRings(center, timeLeft, true);
        }

        private static void ResetRings(int pillarIndex)
        {
            Array.Clear(_rings, 0, _rings.Length);
            _ringPillar = pillarIndex;
            _ringAge = -1;
            _ringSpawnMask = 0;
        }

        /// <summary>柱子已经消失：环不再跟随，但继续按自己的 AI 收尾，直到 timeLeft 归零。</summary>
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

        /// <summary>MEAC EoLBigLaser_Friendly.AI 里的四组 CreateRing。</summary>
        private static void SpawnRings(int schedule, XVector2 center)
        {
            switch (schedule)
            {
                case 0:   // timeLeft 360：CreateRing(0, -0.3, 110)
                    AddRing(0f, -0.3f, 110, false, center);
                    break;
                case 1:   // timeLeft 340：CreateRing(200, -0.6, 90)
                    AddRing(200f, -0.6f, 90, false, center);
                    break;
                case 2:   // timeLeft 320：CreateRing(400, -0.1, 70)
                    AddRing(400f, -0.1f, 70, false, center);
                    break;
                case 3:   // timeLeft 250：三个大环，laserOwner = 柱子
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
                    Ai0 = 1f + ai0Delta,      // MagicRing.SetDefault: ai0 = 1，再 += scale
                    OffsetY = offsetY,
                    Scale = 0f,
                    WorldX = center.X,
                    WorldY = center.Y + offsetY,
                };
                return;
            }
        }

        /// <summary>一步 AI + 一步 timeLeft--（VisualEffect.Update 的顺序）。</summary>
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

                // owner 还活着时环跟着柱子走（VisualEffect.AIWithOwner 的 offset）。
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

        /// <summary>XNA MathHelper.SmoothStep 的同一套平滑（0.2 / 0.35 的 amount 决定展开速度）。</summary>
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

        /// <summary>提交这一帧（Main.DoDraw 之前；或 PostUpdateEverything 兜底）。</summary>
        internal static void Submit()
        {
            try
            {
                if (Main.gameMenu || Shader == null) return;
                if (!_hasCharge && !_hasBeam && !HasRings() && _flash <= 0.001f && _sky <= 0.01f) return;

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

                var chargeX = _hasCharge ? (_chargeCenter.X - screen.X) / sw : beamX;
                var chargeY = _hasCharge ? 1f - (_chargeCenter.Y - screen.Y) / sh : 0.5f;

                // 震屏：MEAC EoLBigLaser_Friendly.AI 每 tick 调 EffectPlayer.ScreenShake(8f, 柱心)，
                // 强度按「柱心到屏幕中心的距离」线性衰减（dis=2000），覆盖整个 245..20 窗口。
                var shake = 0f;
                if (_hasBeam && _beamTimeLeft < 245 && _beamTimeLeft > 20)
                {
                    var screenCenter = new XVector2(screen.X + sw * 0.5f, screen.Y + sh * 0.5f);
                    var distToCenter = Vector2.Distance(_beamCenter, screenCenter);
                    shake = 8f * Math.Max(0f, 1f - distToCenter / 2000f) / sh;
                }

                var beam = 0f;
                if (_hasBeam && _beamScale > 0.01f)
                    beam = 0.55f + 0.45f * MathHelper.Clamp(_beamScale / 3.16f, 0f, 1f);   // 细柱阶段也要看得见
                var charge = _hasCharge ? MathHelper.Clamp(_chargeAlpha, 0f, 1f) : 0f;

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
                fx.Uniform("uBeamWidth", MathHelper.Clamp(_beamScale * 128f / sh, 0.002f, 0.5f));
                fx.Uniform("uBeamX", MathHelper.Clamp(beamX, -0.5f, 1.5f));
                fx.Uniform("uTexOn", _beamTex != null && _coreTex != null && _arcTex != null &&
                                     _starTex != null && _ringStripTex != null ? 1f : 0f);

                // 每个环一个 vec4：屏幕 uv 中心 + 半径/半宽（都以屏幕高度为单位，和 MEAC 的
                // 世界 px 等价，投影常数 viewZ=1000 也换算进 uRingViewZ）。
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
                {
                    var packed = new float[ringCount * 4];
                    Array.Copy(ringData, packed, packed.Length);
                    fx.Uniform("uRings", packed);
                }
                fx.Uniform("uRingCount", ringCount);
                fx.Uniform("uRingViewZ", 1000f / sh);
                // bloom 半径：MEAC 是三次 Kawase 模糊（很小），大了会把 4px 的描边糊成白带。
                fx.Uniform("uRingHalo", 3f / sh);

                if (_beamTex != null) fx.Texture("uBeamTex", _beamTex);
                if (_coreTex != null) fx.Texture("uCoreTex", _coreTex);
                if (_arcTex != null) fx.Texture("uArcTex", _arcTex);
                if (_starTex != null) fx.Texture("uStarTex", _starTex);
                if (_ringStripTex != null) fx.Texture("uRingStrip", _ringStripTex);

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
