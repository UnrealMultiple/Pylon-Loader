// 羲和 Boss —— 屏幕效果总控。
//
// 和 XiheMagic 同一条路：不 patch SpriteBatch、不在绘制中途换混合模式，
// 所有发光图元（球 / 光束 / 环）在 Main.DoDraw 之前提交成若干步全屏 shader pass（World 层，界面盖在上面）。
// 每帧把 active 的弹幕 + VisualEffect + Boss 场地环收进批次，按 28 orb / 8 beam / 6 ring 分 pass，
// 每个 pass 只画自己的包围盒（Box），手机端不会整屏跑十几次循环。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using ScreenShader = Pylon.ModLoader.Effects.ScreenShader;
using Terraria;
using Terraria.ID;
using UVector2 = UnityEngine.Vector2;

namespace XiheBoss
{
    internal struct OrbPrim
    {
        public Vector2 World;
        public float Radius;
        public Color Color;
        public float Alpha;
        public float Rot;
        public Tex Tex;
        public float YScale;
        public float Core;
    }

    internal struct BeamPrim
    {
        public Vector2 Start;
        public Vector2 Dir;
        public float Length;
        public float HalfWidth;
        public Color Color;
        public float Alpha;
        public float Scroll;
        public float Repeat;
        public Tex Tex;
        public float Core;
    }

    internal struct RingPrim
    {
        public Vector2 World;
        public float Radius;
        public float Width;
        public Color Color;
        public float Alpha;
        public float Rot;
        public float Arc;
        public Tex Tex;
    }

    internal sealed class GlurFx
    {
        public Vector2 Pos;
        public int TimeLeft;
        public int MaxTime;
        public bool Additive;
    }

    /// <summary>MEAC PreDraw 里的一次 SpriteBatch.Draw（原样搬运）。</summary>
    internal struct SpritePrim
    {
        public Vector2 World;
        public Tex Tex;
        public Rectangle Source;       // 纹理像素；全图时 = (0,0,W,H)
        public Color Color;
        public float Rot;
        public Vector2 Scale;
        public Vector2 Origin;         // 纹理像素
        public bool Additive;
        public bool Colorize;
        public bool WrapU;             // EoLLaser1 的 tex4：Rectangle 宽 = 5*texW，PointWrap
        public float SrcRepeat;        // 1 或 5
        public float WrapShift;        // PointWrap 的相位（源矩形 x / texW）
    }

    /// <summary>Trail.fx 的显式左右顶点（MEAC 的 triangle strip 原样）。</summary>
    internal struct TrailStripPoint
    {
        public Vector2 Left;
        public Vector2 Right;
        public float UvX;
        public float W;
    }

    /// <summary>MEAC triangle strip 的一个顶点（VertexPositionColorTexture 同口径）。</summary>
    internal struct TriVertex
    {
        public Vector2 World;
        public Vector2 Uv;       // 贴图局部 uv（0..1，v=0 顶边）
        public Color Color;
    }

    /// <summary>一次 DrawUserPrimitives 的三角形列表。</summary>
    internal sealed class TriPrim
    {
        public Tex Tex;
        public bool UseTexture;
        public bool Additive;
        public bool Colorize;
        public bool WrapU;
        public TriVertex[] Verts;
    }

    /// <summary>Trail.fx 的逐段几何：左/右两个顶点 + coord.x/coord.z。</summary>
    internal struct TrailQuad
    {
        public Vector2 L0, R0, L1, R1;
        public float U0, W0, U1, W1;
        public float T;
        public float Alpha;
    }

    internal sealed class TrailQuadGroup
    {
        public Tex Shape;
        public Tex ColorTex;
        public Tex Overlay;
        public bool Additive;
        public readonly List<TrailQuad> Quads = new();
    }

    internal struct Ring2Prim
    {
        public Vector2 World;
        public float Radius;
        public float Width;
        public Tex Tex;
        public Color Color;
        public bool Additive;
    }

    internal struct SunFx
    {
        public Vector2 World;
        public float Scale;
        public float RayAlpha;
        public float Alpha;
    }

    /// <summary>Cycloid 的全屏黄色闪光（源 PreDraw 的 screenTargetSwap + Hide 圆洞）。</summary>
    internal struct CycloidOverlayFx
    {
        public Vector2 World;
        public float Alpha;
        public float HoleRadius;
    }

    /// <summary>MEAC 的 isWarp 特效：不是可见精灵，而是用 screenTargetSwap 当遮罩扭曲屏幕。</summary>
    internal struct WarpFx
    {
        public Vector2 World;
        public float Radius;
        public float Width;
        public float Strength;
    }

    internal sealed class LinePrim
    {
        public Vector2[] Points;
        public float HalfWidth;
        public Color Color;
        public bool Additive;
    }

    internal static class XiheFx
    {
        internal const int OrbPerPass = 28;
        internal const int BeamPerPass = 8;
        internal const int RingPerPass = 6;
        internal const int SpritePerPass = 64;

        internal static ScreenShader PrimsShader;
        internal static ScreenShader PostShader;
        internal static ScreenShader RingShader;
        internal static ScreenShader LineShader;

        private static readonly List<OrbPrim> Orbs = new();
        private static readonly List<BeamPrim> Beams = new();
        private static readonly List<RingPrim> Rings = new();
        private static readonly List<SpritePrim> Sprites = new();
        private static readonly List<TrailQuadGroup> TrailQuads = new();
        private static readonly List<TriPrim> Tris = new();
        private static readonly List<Ring2Prim> Rings2 = new();
        private static readonly List<SunFx> Suns = new();
        private static readonly List<LinePrim> Lines = new();
        private static readonly List<CycloidOverlayFx> CycloidOverlays = new();
        private static readonly List<WarpFx> Warps = new();
        private static readonly Dictionary<int, Action<Projectile>> Visuals = new();

        private static readonly List<GlurFx> Glurs = new();

        private static int _submittedFrame = -1;
        private static bool _loggedError;
        private static bool _loggedOverflow;
        private static readonly HashSet<int> _missingVisual = new();

        // 屏幕亮度 / 灰度
        private static float _brightness;
        private static int _brightnessTime;
        private static int _brightnessMax = 1;
        private static float _gray;
        private static float _warp = 0.03f;

        // 屏幕效果里的像素坐标 = GL viewport（例如 2608x1200）；游戏可能开着动态分辨率
        // （Main.screenWidth/Height 只有 1920x882）。像素量先乘 viewport/screen，
        // 否则会被 shader 的 uResolution 放大、超出自己的 Box，表现就是“矩形裁剪”。
        private static float _pxScaleX = 1f, _pxScaleY = 1f;

        // 相机
        private static float _shake;
        private static bool _useSc;
        private static bool _cancelSc;
        private static Vector2 _scTarget;
        private static Vector2 _scCurrent;
        private static float _scAmount = 0.3f;
        private static int _scTtl;

        internal static float Gray => _gray;
        internal static int SpriteCount => Sprites.Count;
        internal static int TriCount => Tris.Count;
        internal static int VisualCount => Visuals.Count;

        internal static void Init(Mod mod, ModLogger log)
        {
            try
            {
                PrimsShader = ScreenShader.FromFile(mod, "assets/effects/XihePrims.frag");
                PostShader = ScreenShader.FromFile(mod, "assets/effects/XihePost.frag");
                RingShader = ScreenShader.FromFile(mod, "assets/effects/XiheRing.frag");
                LineShader = ScreenShader.FromFile(mod, "assets/effects/XiheLine.frag");
                XiheMesh.Init(mod, log);
                log?.Info("[fx] XihePrims/XihePost/XiheRing/XiheLine 已注册");
            }
            catch (Exception e)
            {
                log?.Error("[fx] 着色器注册失败，Boss 将只有精灵画面：" + e.Message);
            }
        }

        internal static void Reset()
        {
            Orbs.Clear();
            Beams.Clear();
            Rings.Clear();
            Sprites.Clear();
            TrailQuads.Clear();
            Tris.Clear();
            Rings2.Clear();
            Suns.Clear();
            Lines.Clear();
            CycloidOverlays.Clear();
            Warps.Clear();
            Glurs.Clear();
            _brightness = 0f;
            _brightnessTime = 0;
            _gray = 0f;
            _warp = 0.03f;
            _shake = 0f;
            _useSc = false;
            _cancelSc = false;
            _scCurrent = Vector2.Zero;
            _scTtl = 0;
            _submittedFrame = -1;
            XiheBigLaserFx.Reset();
            XiheMesh.Reset();
        }

        // ================= 图元 API（弹幕 / 特效调） =================

        private static void UpdatePixelScale()
        {
            try
            {
                var uw = UnityEngine.Screen.width;
                var uh = UnityEngine.Screen.height;
                var sw = Math.Max(1, Main.screenWidth);
                var sh = Math.Max(1, Main.screenHeight);
                _pxScaleX = uw > 0 ? (float)uw / sw : 1f;
                _pxScaleY = uh > 0 ? (float)uh / sh : 1f;
                if (_pxScaleX <= 0.01f || _pxScaleX > 16f) _pxScaleX = 1f;
                if (_pxScaleY <= 0.01f || _pxScaleY > 16f) _pxScaleY = 1f;
            }
            catch { _pxScaleX = _pxScaleY = 1f; }
        }

        internal static void Orb(Vector2 world, float radius, Tex tex, Color color, float alpha,
            float rot = 0f, float yScale = 1f, float core = 0f)
        {
            if (alpha <= 0.002f || radius <= 0.05f) return;
            Orbs.Add(new OrbPrim
            {
                World = world, Radius = radius, Color = color, Alpha = alpha,
                Rot = rot, Tex = tex, YScale = yScale, Core = core,
            });
        }

        internal static void Beam(Vector2 start, Vector2 dir, float length, float halfWidth, Tex tex,
            Color color, float alpha, float scroll = 0f, float repeatLen = 0f, float core = 0.35f)
        {
            if (alpha <= 0.002f || length <= 1f || halfWidth <= 0.05f) return;
            Beams.Add(new BeamPrim
            {
                Start = start, Dir = dir, Length = length, HalfWidth = halfWidth,
                Color = color, Alpha = alpha, Scroll = scroll, Repeat = repeatLen,
                Tex = tex, Core = core,
            });
        }

        internal static void Ring(Vector2 world, float radius, float width, Tex tex, Color color,
            float alpha, float rot = 0f, float arc = 0f)
        {
            if (alpha <= 0.002f || radius <= 0.05f) return;
            Rings.Add(new RingPrim
            {
                World = world, Radius = radius, Width = width, Color = color,
                Alpha = alpha, Rot = rot, Arc = arc, Tex = tex,
            });
        }

        private static void Overflow(string kind)
        {
            if (_loggedOverflow) return;
            _loggedOverflow = true;
            XiheBossMod.Log?.Warn($"[fx] 图元超过上限，后续 {kind} 丢弃（本帧）");
        }

        // ================= MEAC 精灵 / Trail / 环（逐次 Draw 原样搬） =================

        internal static void Sprite(Vector2 world, Tex tex, Color color, float rot = 0f,
            Vector2? scale = null, Vector2? origin = null, bool additive = false, bool colorize = false)
        {
            var w = XiheTextures.WidthOf(tex);
            var h = XiheTextures.HeightOf(tex);
            SpriteRect(world, tex, new Rectangle(0, 0, w, h), color, rot,
                scale ?? Vector2.One, origin ?? new Vector2(w, h) / 2f, additive, colorize);
        }

        internal static void SpriteRect(Vector2 world, Tex tex, Rectangle source, Color color, float rot,
            Vector2 scale, Vector2 origin, bool additive, bool colorize, bool wrapU = false, float srcRepeat = 1f,
            float wrapShift = 0f)
        {
            if (color.A == 0 && !additive) return;
            if (source.Width <= 0 || source.Height <= 0) return;
            // 不再用固定 1024 截断：开场 CrystalArrow 雨会超过这个数，截断就是“一部分弹幕消失”。
            // 真正的性能控制放在后面的空间分桶/可见性剔除。
            Sprites.Add(new SpritePrim
            {
                World = world,
                Tex = tex,
                Source = source,
                Color = color,
                Rot = rot,
                Scale = scale,
                Origin = origin,
                Additive = additive,
                Colorize = colorize,
                WrapU = wrapU,
                SrcRepeat = srcRepeat,
                WrapShift = wrapShift,
            });
        }

        /// <summary>
        /// MEAC Trail.fx 的精确几何：传显式左右顶点（每点两个顶点），逐段生成 quad。
        /// 同一帧里同贴图/同混合的 trail 会自动合并到尽量少的 pass。
        /// </summary>
        internal static void TrailStrip(IReadOnlyList<TrailStripPoint> points, Tex shape, Tex colorTex, Tex overlay,
            float t, bool additive, float alpha = 1f)
        {
            if (points == null || points.Count < 2) return;
            TrailQuadGroup group = null;
            for (var i = 0; i < TrailQuads.Count; i++)
            {
                var g = TrailQuads[i];
                if (g.Shape == shape && g.ColorTex == colorTex && g.Overlay == overlay && g.Additive == additive)
                {
                    group = g;
                    break;
                }
            }
            if (group == null)
            {
                group = new TrailQuadGroup
                {
                    Shape = shape,
                    ColorTex = colorTex,
                    Overlay = overlay,
                    Additive = additive,
                };
                TrailQuads.Add(group);
            }

            for (var i = 0; i + 1 < points.Count; i++)
            {
                group.Quads.Add(new TrailQuad
                {
                    L0 = points[i].Left, R0 = points[i].Right,
                    L1 = points[i + 1].Left, R1 = points[i + 1].Right,
                    U0 = points[i].UvX, W0 = points[i].W,
                    U1 = points[i + 1].UvX, W1 = points[i + 1].W,
                    T = t, Alpha = alpha,
                });
            }
        }

        /// <summary>
        /// MEAC DrawUserPrimitives(VertexPositionColorTexture) 的三角形列表。
        /// uv 用贴图局部坐标（v=0 顶边）；useTexture=false 时等价 MagicPixel。
        /// </summary>
        internal static void Tri(Tex tex, bool useTexture, bool additive, bool colorize, bool wrapU,
            TriVertex[] verts)
        {
            if (verts == null || verts.Length < 3) return;
            Tris.Add(new TriPrim
            {
                Tex = tex,
                UseTexture = useTexture,
                Additive = additive,
                Colorize = colorize,
                WrapU = wrapU,
                Verts = verts,
            });
        }

        internal static void Ring2(Vector2 world, float radius, float width, Tex tex, Color color,
            bool additive)
        {
            if (radius <= 0.1f || width <= 0.01f || color.A == 0) return;
            Rings2.Add(new Ring2Prim
            {
                World = world, Radius = radius, Width = width, Tex = tex,
                Color = color, Additive = additive,
            });
        }

        /// <summary>EoLSky.DrawSun：uScale=0.067*scale、uRayAlpha=ai[1]、整体乘 alpha=ai[0]*2.1。</summary>
        internal static void SunFxAt(Vector2 world, float scale, float rayAlpha, float alpha)
        {
            if (alpha <= 0.001f || scale <= 0.0001f) return;
            Suns.Add(new SunFx { World = world, Scale = 0.067f * scale, RayAlpha = rayAlpha, Alpha = alpha });
        }

        internal static void Line(Vector2[] points, float halfWidth, Color color, bool additive)
        {
            if (points == null || points.Length < 2 || halfWidth <= 0.01f || color.A == 0) return;
            Lines.Add(new LinePrim { Points = points, HalfWidth = halfWidth, Color = color, Additive = additive });
        }

        /// <summary>Cycloid 的黄色全屏闪光；holeRadius 是源 Hide.fx 圆洞的半径（屏幕高度分数）。</summary>
        internal static void CycloidOverlay(Vector2 world, float alpha, float holeRadius)
        {
            if (alpha <= 0.002f) return;
            if (CycloidOverlays.Count >= 64) return;
            CycloidOverlays.Add(new CycloidOverlayFx
            {
                World = world,
                Alpha = MathHelper.Clamp(alpha, 0f, 1f),
                HoleRadius = MathHelper.Clamp(holeRadius, 0f, 2f),
            });
        }

        /// <summary>
        /// MEAC 的 isWarp 特效（RingWarp / RingWarp2 / CircleWarp / LineWarp）：源先把这些
        /// 精灵画进 screenTargetSwap，再用 KScreen0 把红/绿通道当方向/强度扭曲屏幕。
        /// 它们**不是可见的环**。这里按同一个语义提交屏幕扭曲。
        /// </summary>
        internal static void Warp(Vector2 world, float radius, float width, float strength)
        {
            if (strength <= 0.0001f) return;
            if (Warps.Count >= 64) return;
            Warps.Add(new WarpFx
            {
                World = world,
                Radius = MathHelper.Max(0f, radius),
                Width = MathHelper.Max(0.001f, width),
                Strength = strength,
            });
        }

        internal static void Brightness(float value, int time)
        {
            if (value <= _brightness && time <= _brightnessTime) return;
            _brightness = value;
            _brightnessTime = time;
            _brightnessMax = Math.Max(1, time);
        }

        internal static void RadialGlur(Vector2 pos, int time, bool additive)
        {
            Glurs.Add(new GlurFx { Pos = pos, TimeLeft = time, MaxTime = time, Additive = additive });
        }

        internal static void ScreenShake(float strength, Vector2 center, int dis = 2000)
        {
            var len = Vector2.Distance(XiheHelper.ScreenCenter, center);
            if (len >= dis) return;
            var intense = strength * (1f - len / dis);
            if (_shake < intense) _shake = intense;
        }

        internal static void UseScreenPosition(Vector2 target, float amount)
        {
            _useSc = true;
            _cancelSc = false;
            _scTarget = target;
            _scAmount = amount;
            _scTtl = 10;   // 真机可能 >60 帧；按帧递减要留够两次 Boss tick 的余量
        }

        internal static void CancelScreenPosition()
        {
            if (_useSc)
            {
                _useSc = false;
                _cancelSc = true;
            }
        }

        /// <summary>Mod.ModifyScreenPosition 里调：震屏 / 镜头推拉（等价 MEAC 在 Player.PreUpdate 里改 Main.screenPosition）。</summary>
        internal static void ApplyCamera()
        {
            if (Main.gameMenu) return;
            var half = new Vector2(Main.screenWidth, Main.screenHeight) / 2f;

            if (_useSc)
            {
                if (_scCurrent == Vector2.Zero) _scCurrent = Main.screenPosition;
                Main.screenPosition = Vector2.Lerp(_scCurrent, _scTarget - half, _scAmount);
                _scCurrent = Main.screenPosition;
            }
            else if (_cancelSc)
            {
                if (_scCurrent == Vector2.Zero) _scCurrent = Main.screenPosition;
                var player = Main.player[Main.myPlayer];
                Main.screenPosition = Vector2.Lerp(_scCurrent, player.Center - half, 0.4f);
                _scCurrent = Main.screenPosition;
                if (Vector2.Distance(_scCurrent, player.Center - half) < 5f)
                {
                    _scCurrent = Vector2.Zero;
                    _cancelSc = false;
                }
            }

            if (_shake > 0f)
            {
                Main.screenPosition += XiheHelper.NextVector2Unit() * _shake;
                _shake -= 1f;
            }
            else
            {
                _shake = 0f;
            }
        }

        // ================= 弹幕视觉注册 =================

        internal static void RegisterVisual<T>(Action<Projectile> push) where T : ModProjectile
        {
            var type = ModContent.ProjectileType<T>();
            if (type > 0) Visuals[type] = push;
        }

        // ================= 每 tick =================

        internal static void Tick()
        {
            try
            {
                if (Main.gameMenu) { Reset(); return; }

                XiheVisuals.Tick();

                if (_brightnessTime > 0) _brightnessTime--;
                for (var i = Glurs.Count - 1; i >= 0; i--)
                {
                    if (--Glurs[i].TimeLeft <= 0) Glurs.RemoveAt(i);
                }
                if (_scTtl > 0 && --_scTtl == 0) CancelScreenPosition();

                var boss = XiheBossNpc.Current;
                var grayTarget = boss != null && boss.NpcAlive && boss.GrayScale ? 1f : 0f;
                _gray += (grayTarget - _gray) * 0.03f;
                if (_gray < 0.004f) _gray = 0f;
            }
            catch (Exception e)
            {
                if (!_loggedError) { _loggedError = true; XiheBossMod.Log?.Warn("[fx] Tick 异常：" + e.Message); }
            }
        }

        // ================= 提交这一帧 =================

        internal static void Submit()
        {
            try
            {
                if (Main.gameMenu) return;
                var frameCount = UnityEngine.Time.frameCount;
                if (_submittedFrame == frameCount) return;
                _submittedFrame = frameCount;

                Orbs.Clear();
                Beams.Clear();
                Rings.Clear();
                Sprites.Clear();
                TrailQuads.Clear();
                Tris.Clear();
                Rings2.Clear();
                Suns.Clear();
                Lines.Clear();
                CycloidOverlays.Clear();
                Warps.Clear();
                UpdatePixelScale();
                XiheMesh.Reset();
                XiheMesh.BeginFrame();

                var boss = XiheBossNpc.Current;
                if (boss != null && boss.NpcAlive) boss.PushAura();
                XiheVisuals.PushAll();

                for (var i = 0; i < Main.projectile.Length; i++)
                {
                    var p = Main.projectile[i];
                    if (p == null || !p.active) continue;
                    if (Visuals.TryGetValue(p.type, out var push))
                    {
                        try { push(p); }
                        catch (Exception e)
                        {
                            if (!_loggedError) { _loggedError = true; XiheBossMod.Log?.Warn("[fx] 弹幕视觉异常：" + e.Message); }
                        }
                    }
                    else if (p.type > Terraria.ID.ProjectileID.Count && _missingVisual.Add(p.type))
                    {
                        XiheBossMod.Log?.Warn($"[fx] 弹幕 type={p.type} 没有登记 PushVisual（hostile={p.hostile} friendly={p.friendly}）");
                    }
                }

                var frame = new ScreenFrame();

                // 黎明只靠 Main.dayTime + Main.time（原版天空/光照），不叠全屏天空蒙版。
                // Pylon 没有“世界之下”的天空层，全屏叠色会变成一层蒙版，画面发灰发黄。
                AddRingPasses(frame);
                AddTrail2Passes(frame);
                AddLinePasses(frame);
                AddSpritePasses(frame);
                AddTriPasses(frame);
                AddWarpPasses(frame);
                AddPrimPasses(frame);
                AddCycloidOverlays(frame);
                XiheBigLaserFx.Submit(frame);
                AddSuns(frame);
                XiheMesh.Submit(frame);

                if (_gray > 0.004f)   // gray=0 时这一步是纯 no-op（源只在入场/转场用），别再每帧跑一遍全屏 pass
                    AddPost(frame, 1f, boss != null && boss.NpcAlive ? boss.Center : XiheHelper.ScreenCenter,
                        _gray, _warp);
                AddGlur(frame);
                if (_brightnessTime > 0 && _brightness > 1f)
                {
                    // MEAC: progress = (max-timeleft)/max; progress2 = 2*(p<0.5?p:1-p)
                    var p = 1f - (float)_brightnessTime / _brightnessMax;
                    var p2 = 2f * (p < 0.5f ? p : 1f - p);
                    AddPost(frame, 3f, default, (_brightness - 1f) * p2, 0f);
                }

                if (frame.Draws.Count > 0)
                {
                    frame.Submit();
                }
            }
            catch (Exception e)
            {
                if (!_loggedError) { _loggedError = true; XiheBossMod.Log?.Warn("[fx] Submit 异常：" + e.Message); }
            }
        }

        private static void AddPost(ScreenFrame frame, float mode, Vector2 centerWorld, float strength, float extra)
        {
            if (PostShader == null) return;
            var center = centerWorld == default ? new Vector2(0.5f, 0.5f) : ToUv(centerWorld);
            var fx = new ScreenEffect(PostShader)
            {
                Layer = ScreenEffectLayer.World,
                Center = new UVector2(MathHelper.Clamp(center.X, 0f, 1f), MathHelper.Clamp(center.Y, 0f, 1f)),
                Intensity = 1f,
            };
            fx.Uniform("uMode", mode);
            fx.Uniform("uPos", MathHelper.Clamp(center.X, 0f, 1f), MathHelper.Clamp(center.Y, 0f, 1f));
            fx.Uniform("uStrength", MathHelper.Clamp(strength, 0f, 2f));
            fx.Uniform("uExtra", MathHelper.Clamp(extra, 0f, 2f));
            if (XiheTextures.Noise != null) fx.Texture("uNoise", XiheTextures.Noise);
            frame.Add(fx);
        }

        private static void AddSuns(ScreenFrame frame)
        {
            // 手机端硬件 mesh 的全屏四边形在上下边缘会出现半屏矩形色差；改回 XihePost 的全屏
            // Sun.fx pass（mode 6）。一个太阳一帧一次 pass，开销可接受。
            if (PostShader == null) return;
            for (var i = 0; i < Suns.Count; i++)
            {
                var s = Suns[i];
                if (s.Alpha <= 0.001f || s.Scale <= 0.0001f) continue;
                var uv = ToUv(s.World);
                var fx = new ScreenEffect(PostShader)
                {
                    Layer = ScreenEffectLayer.World,
                    Center = new UVector2(MathHelper.Clamp(uv.X, 0f, 1f), MathHelper.Clamp(uv.Y, 0f, 1f)),
                    Intensity = s.Alpha,
                };
                fx.Uniform("uMode", 6f);
                fx.Uniform("uPos", MathHelper.Clamp(uv.X, 0f, 1f), MathHelper.Clamp(uv.Y, 0f, 1f));
                fx.Uniform("uStrength", s.Scale);
                fx.Uniform("uExtra", s.RayAlpha);
                fx.Uniform("uIntensity", s.Alpha);
                frame.Add(fx);
            }
        }

        private static void AddGlur(ScreenFrame frame)
        {
            if (PostShader == null || Glurs.Count == 0) return;
            foreach (var f in Glurs)
            {
                var t = MathHelper.Clamp(1f - (float)f.TimeLeft / Math.Max(1, f.MaxTime), 0f, 1f);
                var progress2 = 2f * (t < 0.5f ? t : 1f - t);
                var uv = ToUv(f.Pos);
                var fx = new ScreenEffect(PostShader)
                {
                    Layer = ScreenEffectLayer.World,
                    Center = new UVector2(MathHelper.Clamp(uv.X, 0f, 1f), MathHelper.Clamp(uv.Y, 0f, 1f)),
                    Intensity = 1f,
                };
                fx.Uniform("uMode", 2f);
                fx.Uniform("uPos", MathHelper.Clamp(uv.X, 0f, 1f), MathHelper.Clamp(uv.Y, 0f, 1f));
                fx.Uniform("uStrength", progress2);
                fx.Uniform("uExtra", f.Additive ? 1f : 0f);
                if (XiheTextures.Noise != null) fx.Texture("uNoise", XiheTextures.Noise);
                frame.Add(fx);
            }
        }

        private static void AddCycloidOverlays(ScreenFrame frame)
        {
            if (PostShader == null || CycloidOverlays.Count == 0) return;
            foreach (var o in CycloidOverlays)
            {
                AddPost(frame, 7f, o.World, o.Alpha, o.HoleRadius);
            }
        }

        private static void AddWarpPasses(ScreenFrame frame)
        {
            if (PostShader == null || Warps.Count == 0) return;
            foreach (var w in Warps)
            {
                var uv = ToUv(w.World);
                var fx = new ScreenEffect(PostShader)
                {
                    Layer = ScreenEffectLayer.World,
                    Box = new[] { 0f, 0f, 1f, 1f },
                    Center = new UVector2(MathHelper.Clamp(uv.X, 0f, 1f), MathHelper.Clamp(uv.Y, 0f, 1f)),
                    Radius = w.Radius,
                    Intensity = 1f,
                };
                fx.Uniform("uMode", 8f);
                fx.Uniform("uPos", MathHelper.Clamp(uv.X, 0f, 1f), MathHelper.Clamp(uv.Y, 0f, 1f));
                fx.Uniform("uStrength", MathHelper.Clamp(w.Strength, -0.5f, 0.5f));
                fx.Uniform("uExtra", MathHelper.Clamp(w.Width, 0f, 1f));
                fx.Uniform("uRadius", MathHelper.Clamp(w.Radius, 0f, 2f));
                frame.Add(fx);
            }
        }

        // ================= 新图元：精灵 / Trail / 环 =================

        private static void AddRingPasses(ScreenFrame frame)
        {
            if (RingShader == null || Rings2.Count == 0) return;
            var sw = Math.Max(1, Main.screenWidth);
            var sh = Math.Max(1, Main.screenHeight);
            var sp = Main.screenPosition;

            for (var start = 0; start < Rings2.Count; start += 8)
            {
                var n = Math.Min(8, Rings2.Count - start);
                var a = new float[n * 4];
                var b = new float[n * 4];
                var c = new float[n * 4];
                var d = new float[n * 4];
                var x0 = 1f; var y0 = 1f; var x1 = 0f; var y1 = 0f;
                for (var k = 0; k < n; k++)
                {
                    var p = Rings2[start + k];
                    var pixX = (p.World.X - sp.X - sw / 2f) * _pxScaleX;
                    var pixY = (p.World.Y - sp.Y - sh / 2f) * _pxScaleY;
                    var cell = XiheTextures.Cell(p.Tex);
                    a[k * 4 + 0] = pixX; a[k * 4 + 1] = pixY;
                    a[k * 4 + 2] = p.Radius * _pxScaleY; a[k * 4 + 3] = p.Width * _pxScaleY;
                    b[k * 4 + 0] = p.Color.R / 255f; b[k * 4 + 1] = p.Color.G / 255f;
                    b[k * 4 + 2] = p.Color.B / 255f; b[k * 4 + 3] = p.Color.A / 255f;
                    c[k * 4 + 0] = cell.U0; c[k * 4 + 1] = cell.V0;
                    c[k * 4 + 2] = cell.DU; c[k * 4 + 3] = cell.DV;
                    d[k * 4 + 0] = p.Additive ? 1f : 0f;
                    var uv = ToUv(p.World);
                    var r = (p.Radius + p.Width) / sh;
                    Include(ref x0, ref y0, ref x1, ref y1, uv.X - r, uv.Y - r, uv.X + r, uv.Y + r);
                }

                var fx = new ScreenEffect(RingShader)
                {
                    Layer = ScreenEffectLayer.World,
                    Box = new[]
                    {
                        MathHelper.Clamp(x0 - 0.02f, 0f, 1f), MathHelper.Clamp(y0 - 0.02f, 0f, 1f),
                        MathHelper.Clamp(x1 + 0.02f, 0f, 1f), MathHelper.Clamp(y1 + 0.02f, 0f, 1f),
                    },
                    Center = new UVector2(0.5f, 0.5f),
                    Intensity = 1f,
                };
                fx.Uniform("uRingA", a);
                fx.Uniform("uRingB", b);
                fx.Uniform("uRingC", c);
                fx.Uniform("uRingD", d);
                fx.Uniform("uRingCount", n);
                if (XiheTextures.Atlas != null) fx.Texture("uAtlas", XiheTextures.Atlas);
                frame.Add(fx);
            }
        }

        private static void AddTrail2Passes(ScreenFrame frame)
        {
            if (TrailQuads.Count == 0) return;

            // 硬件几何：同 shape/color/overlay/混合的 trail 合成一次画；逐像素公式仍在 XiheTrailFx.frag 里
            // （和 MEAC Trail.fx pass 0 逐行一致），但只着色实际覆盖的像素。
            foreach (var group in TrailQuads)
            {
                XiheMesh.AddTrailQuads(group);
            }
        }

        private static void AddTriPasses(ScreenFrame frame)
        {
            if (Tris.Count == 0) return;

            // 硬件几何：每个 TriPrim 是源 DrawUserPrimitives 的一次顶点列表。
            // 同贴图/同混合会自动合并；不再按屏幕 4x4 分区，也不再有每像素 64 三角形的循环。
            foreach (var p in Tris)
            {
                XiheMesh.AddGeometry(p.Tex, p.UseTexture, p.Colorize, p.WrapU, p.Additive, p.Verts);
            }
        }

        private static void AddLinePasses(ScreenFrame frame)
        {
            if (LineShader == null || Lines.Count == 0) return;
            var sw = Math.Max(1, Main.screenWidth);
            var sh = Math.Max(1, Main.screenHeight);
            var sp = Main.screenPosition;

            foreach (var line in Lines)
            {
                for (var start = 0; start < line.Points.Length - 1; start += 63)
                {
                    var count = Math.Min(64, line.Points.Length - start);
                    if (count < 2) break;
                    var pts = new float[count * 4];
                    var x0 = 1f; var y0 = 1f; var x1 = 0f; var y1 = 0f;
                    for (var i = 0; i < count; i++)
                    {
                        var w = line.Points[start + i];
                        pts[i * 4 + 0] = (w.X - sp.X - sw / 2f) * _pxScaleX;
                        pts[i * 4 + 1] = (w.Y - sp.Y - sh / 2f) * _pxScaleY;
                        var uv = ToUv(w);
                        var r = line.HalfWidth / sh;
                        Include(ref x0, ref y0, ref x1, ref y1, uv.X - r, uv.Y - r, uv.X + r, uv.Y + r);
                    }

                    var fx = new ScreenEffect(LineShader)
                    {
                        Layer = ScreenEffectLayer.World,
                        Box = new[]
                        {
                            MathHelper.Clamp(x0 - 0.02f, 0f, 1f), MathHelper.Clamp(y0 - 0.02f, 0f, 1f),
                            MathHelper.Clamp(x1 + 0.02f, 0f, 1f), MathHelper.Clamp(y1 + 0.02f, 0f, 1f),
                        },
                        Center = new UVector2(0.5f, 0.5f),
                        Intensity = 1f,
                    };
                    fx.Uniform("uPts", pts);
                    fx.Uniform("uCount", count);
                    fx.Uniform("uWidth", line.HalfWidth * _pxScaleY);
                    fx.Uniform("uLineColor", line.Color.R / 255f, line.Color.G / 255f,
                        line.Color.B / 255f, line.Color.A / 255f);
                    fx.Uniform("uAdditive", line.Additive ? 1f : 0f);
                    frame.Add(fx);
                }
            }
        }

        private static void AddSpritePasses(ScreenFrame frame)
        {
            if (Sprites.Count == 0) return;

            // 直接交给硬件几何批：同贴图/同混合的精灵合成一次画，GPU 只光栅化实际覆盖的像素。
            // 屏幕外的精灵仍然不提交（省顶点，行为与旧版一致）。
            var sh = Math.Max(1, Main.screenHeight);
            foreach (var s in Sprites)
            {
                var uv = ToUv(s.World);
                var ext = Math.Max(s.Source.Width * Math.Abs(s.Scale.X),
                    s.Source.Height * Math.Abs(s.Scale.Y)) / sh;
                if (uv.X + ext < 0f || uv.X - ext > 1f || uv.Y + ext < 0f || uv.Y - ext > 1f)
                    continue;
                XiheMesh.AddSprite(s);
            }
        }

        private static void SplitSpriteGroup(List<SpritePrim> group, ScreenFrame frame)
        {
            if (group.Count == 0) return;
            if (group.Count <= SpritePerPass)
            {
                AddSpritePass(group, frame);
                return;
            }

            var minX = float.MaxValue; var maxX = float.MinValue;
            var minY = float.MaxValue; var maxY = float.MinValue;
            foreach (var s in group)
            {
                if (s.World.X < minX) minX = s.World.X;
                if (s.World.X > maxX) maxX = s.World.X;
                if (s.World.Y < minY) minY = s.World.Y;
                if (s.World.Y > maxY) maxY = s.World.Y;
            }

            var byX = maxX - minX >= maxY - minY;
            group.Sort((a, b) => byX ? a.World.X.CompareTo(b.World.X) : a.World.Y.CompareTo(b.World.Y));
            var half = group.Count / 2;
            SplitSpriteGroup(group.GetRange(0, half), frame);
            SplitSpriteGroup(group.GetRange(half, group.Count - half), frame);
        }

        private static void AddSpritePass(List<SpritePrim> group, ScreenFrame frame)
        {
            // 经验：手机 Adreno 上 XiheSprite 的逐像素 uv 反解会整批不显示（MagicPixel 正常、
            // 带源矩形的图集贴图不显示）。这里不再走 XiheSprite，直接把每个 SpriteBatch.Draw
            // 展开成两个三角形（与 MEAC 的 VertexPositionColorTexture 同一套几何），
            // 复用已经验证可用的 XiheTri.frag。
            var sw = Math.Max(1, Main.screenWidth);
            var sh = Math.Max(1, Main.screenHeight);
            var sp = Main.screenPosition;

            foreach (var s in group)
            {
                var texW = XiheTextures.WidthOf(s.Tex);
                var texH = XiheTextures.HeightOf(s.Tex);
                var cell = XiheTextures.Cell(s.Tex);
                if (s.Source.Width <= 0 || s.Source.Height <= 0) continue;

                var w = s.Source.Width * s.Scale.X;
                var h = s.Source.Height * s.Scale.Y;
                var ox = s.Origin.X * s.Scale.X;
                var oy = s.Origin.Y * s.Scale.Y;
                var cr = (float)Math.Cos(s.Rot);
                var sr = (float)Math.Sin(s.Rot);

                Vector2 Corner(float lx, float ly) => s.World + new Vector2(
                    lx * cr - ly * sr, lx * sr + ly * cr);

                var tl = Corner(-ox, -oy);
                var tr = Corner(w - ox, -oy);
                var bl = Corner(-ox, h - oy);
                var br = Corner(w - ox, h - oy);

                var u0 = (float)s.Source.X / texW + s.WrapShift;
                var v0 = (float)s.Source.Y / texH;
                var u1 = (float)(s.Source.X + s.Source.Width) / texW + s.WrapShift;
                var v1 = (float)(s.Source.Y + s.Source.Height) / texH;

                var verts = new[]
                {
                    new TriVertex { World = tl, Uv = new Vector2(u0, v0), Color = s.Color },
                    new TriVertex { World = tr, Uv = new Vector2(u1, v0), Color = s.Color },
                    new TriVertex { World = bl, Uv = new Vector2(u0, v1), Color = s.Color },
                    new TriVertex { World = tr, Uv = new Vector2(u1, v0), Color = s.Color },
                    new TriVertex { World = br, Uv = new Vector2(u1, v1), Color = s.Color },
                    new TriVertex { World = bl, Uv = new Vector2(u0, v1), Color = s.Color },
                };
                XiheFx.Tri(s.Tex, true, s.Additive, s.Colorize, s.WrapU, verts);
            }
        }

        private static void AddPrimPasses(ScreenFrame frame)

        {
            if (PrimsShader == null) return;
            if (Orbs.Count == 0 && Beams.Count == 0 && Rings.Count == 0) return;

            var maxPasses = 256;   // 只是防失控：正常一帧远到不了，别再静默丢 beam/orb
            var pass = 0;
            var oi = 0; var bi = 0; var ri = 0;
            while ((oi < Orbs.Count || bi < Beams.Count || ri < Rings.Count) && pass < maxPasses)
            {
                var oc = Math.Min(OrbPerPass, Orbs.Count - oi);
                var bc = Math.Min(BeamPerPass, Beams.Count - bi);
                var rc = Math.Min(RingPerPass, Rings.Count - ri);

                var oa = new float[oc * 4];
                var ob = new float[oc * 4];
                var occ = new float[oc * 4];
                var od = new float[oc * 4];
                for (var k = 0; k < oc; k++)
                {
                    var p = Orbs[oi + k];
                    var uv = ToUv(p.World);
                    var cell = XiheTextures.Cell(p.Tex);
                    oa[k * 4 + 0] = uv.X; oa[k * 4 + 1] = uv.Y;
                    oa[k * 4 + 2] = p.Radius / Math.Max(1, Main.screenHeight);
                    oa[k * 4 + 3] = p.Rot;
                    ob[k * 4 + 0] = p.Color.R / 255f; ob[k * 4 + 1] = p.Color.G / 255f;
                    ob[k * 4 + 2] = p.Color.B / 255f; ob[k * 4 + 3] = p.Alpha;
                    occ[k * 4 + 0] = cell.U0; occ[k * 4 + 1] = cell.V0;
                    occ[k * 4 + 2] = cell.DU; occ[k * 4 + 3] = cell.DV;
                    od[k * 4 + 0] = p.YScale; od[k * 4 + 1] = p.Core; od[k * 4 + 2] = 0f; od[k * 4 + 3] = 0f;
                }

                var ba = new float[bc * 4];
                var bb = new float[bc * 4];
                var bc3 = new float[bc * 4];
                var bd = new float[bc * 4];
                var be = new float[bc * 4];
                for (var k = 0; k < bc; k++)
                {
                    var p = Beams[bi + k];
                    var uv = ToUv(p.Start);
                    var dir = XiheHelper.Normalize2(p.Dir);
                    var cell = XiheTextures.Cell(p.Tex);
                    ba[k * 4 + 0] = uv.X; ba[k * 4 + 1] = uv.Y;
                    ba[k * 4 + 2] = dir.X; ba[k * 4 + 3] = dir.Y;
                    bb[k * 4 + 0] = p.Length / Math.Max(1, Main.screenHeight);
                    bb[k * 4 + 1] = p.HalfWidth / Math.Max(1, Main.screenHeight);
                    bb[k * 4 + 2] = 0f; bb[k * 4 + 3] = 0f;
                    bc3[k * 4 + 0] = p.Color.R / 255f; bc3[k * 4 + 1] = p.Color.G / 255f;
                    bc3[k * 4 + 2] = p.Color.B / 255f; bc3[k * 4 + 3] = p.Alpha;
                    bd[k * 4 + 0] = cell.U0; bd[k * 4 + 1] = cell.V0;
                    bd[k * 4 + 2] = cell.DU; bd[k * 4 + 3] = cell.DV;
                    be[k * 4 + 0] = p.Scroll;
                    be[k * 4 + 1] = (p.Repeat > 0f ? p.Repeat : 1280f) / Math.Max(1, Main.screenHeight);
                    be[k * 4 + 2] = p.Core; be[k * 4 + 3] = 0f;
                }

                var ra = new float[rc * 4];
                var rb = new float[rc * 4];
                var rc3 = new float[rc * 4];
                var rd = new float[rc * 4];
                for (var k = 0; k < rc; k++)
                {
                    var p = Rings[ri + k];
                    var uv = ToUv(p.World);
                    var cell = XiheTextures.Cell(p.Tex);
                    ra[k * 4 + 0] = uv.X; ra[k * 4 + 1] = uv.Y;
                    ra[k * 4 + 2] = p.Radius / Math.Max(1, Main.screenHeight);
                    ra[k * 4 + 3] = p.Width / Math.Max(1, Main.screenHeight);
                    rb[k * 4 + 0] = p.Color.R / 255f; rb[k * 4 + 1] = p.Color.G / 255f;
                    rb[k * 4 + 2] = p.Color.B / 255f; rb[k * 4 + 3] = p.Alpha;
                    rc3[k * 4 + 0] = cell.U0; rc3[k * 4 + 1] = cell.V0;
                    rc3[k * 4 + 2] = cell.DU; rc3[k * 4 + 3] = cell.DV;
                    rd[k * 4 + 0] = p.Rot; rd[k * 4 + 1] = p.Arc; rd[k * 4 + 2] = 0f; rd[k * 4 + 3] = 0f;
                }

                var box = new[] { 0f, 0f, 1f, 1f };
                ComputeBox(oi, oc, bi, bc, ri, rc, out var x0, out var y0, out var x1, out var y1);
                box[0] = MathHelper.Clamp(x0 - 0.02f, 0f, 1f);
                box[1] = MathHelper.Clamp(y0 - 0.02f, 0f, 1f);
                box[2] = MathHelper.Clamp(x1 + 0.02f, 0f, 1f);
                box[3] = MathHelper.Clamp(y1 + 0.02f, 0f, 1f);

                var fx = new ScreenEffect(PrimsShader)
                {
                    Layer = ScreenEffectLayer.World,
                    Box = box,
                    Center = new UVector2(0.5f, 0.5f),
                    Intensity = 1f,
                };
                if (oc > 0)
                {
                    fx.Uniform("uOrbA", oa);
                    fx.Uniform("uOrbB", ob);
                    fx.Uniform("uOrbC", occ);
                    fx.Uniform("uOrbD", od);
                }
                if (bc > 0)
                {
                    fx.Uniform("uBeamA", ba);
                    fx.Uniform("uBeamB", bb);
                    fx.Uniform("uBeamC", bc3);
                    fx.Uniform("uBeamD", bd);
                    fx.Uniform("uBeamE", be);
                }
                if (rc > 0)
                {
                    fx.Uniform("uRingA", ra);
                    fx.Uniform("uRingB", rb);
                    fx.Uniform("uRingC", rc3);
                    fx.Uniform("uRingD", rd);
                }
                fx.Uniform("uOrbCount", oc);
                fx.Uniform("uBeamCount", bc);
                fx.Uniform("uRingCount", rc);
                fx.Uniform("uGlobalAlpha", 1f);
                if (XiheTextures.Atlas != null) fx.Texture("uAtlas", XiheTextures.Atlas);
                frame.Add(fx);

                oi += oc; bi += bc; ri += rc;
                pass++;
            }
        }

        private static void ComputeBox(int oi, int oc, int bi, int bc, int ri, int rc,
            out float x0, out float y0, out float x1, out float y1)
        {
            x0 = 1f; y0 = 1f; x1 = 0f; y1 = 0f;
            var h = Math.Max(1, Main.screenHeight);
            for (var k = 0; k < oc; k++)
            {
                var p = Orbs[oi + k];
                var uv = ToUv(p.World);
                var r = p.Radius / h;
                var ry = r * Math.Max(p.YScale, 0.05f);
                Include(ref x0, ref y0, ref x1, ref y1, uv.X - r, uv.Y - ry, uv.X + r, uv.Y + ry);
            }
            for (var k = 0; k < bc; k++)
            {
                var p = Beams[bi + k];
                var uv = ToUv(p.Start);
                var dir = XiheHelper.Normalize2(p.Dir);
                var end = ToUv(p.Start + dir * p.Length);
                var w = p.HalfWidth / h;
                Include(ref x0, ref y0, ref x1, ref y1,
                    Math.Min(uv.X, end.X) - w, Math.Min(uv.Y, end.Y) - w,
                    Math.Max(uv.X, end.X) + w, Math.Max(uv.Y, end.Y) + w);
            }
            for (var k = 0; k < rc; k++)
            {
                var p = Rings[ri + k];
                var uv = ToUv(p.World);
                var r = (p.Radius + p.Width) / h;
                Include(ref x0, ref y0, ref x1, ref y1, uv.X - r, uv.Y - r, uv.X + r, uv.Y + r);
            }
        }

        private static void Include(ref float x0, ref float y0, ref float x1, ref float y1,
            float ax, float ay, float bx, float by)
        {
            if (ax < x0) x0 = ax;
            if (ay < y0) y0 = ay;
            if (bx > x1) x1 = bx;
            if (by > y1) y1 = by;
        }

        internal static Vector2 ToUv(Vector2 world)
        {
            var sw = Math.Max(1, Main.screenWidth);
            var sh = Math.Max(1, Main.screenHeight);
            var sp = Main.screenPosition;
            return new Vector2((world.X - sp.X) / sw, 1f - (world.Y - sp.Y) / sh);
        }    }
}














