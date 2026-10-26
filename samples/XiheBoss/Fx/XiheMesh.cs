// 羲和 Boss —— 硬件几何批（Pylon mesh 通道）。
//
// 为什么有这层：旧的实现把每个精灵/三角形/拖尾段都塞进 uniform 数组，再用一个“全屏 pass”
// 让每个像素循环测试 64 个三角形。同屏几十发 EoLProj / 25 圈太阳法阵时，每帧要跑几十个
// 屏幕拷贝 + 每像素上百次重心计算——这就是第二阶段光柱、太阳、法阵掉帧的根因。
// MEAC 用的是 SpriteBatch / DrawUserPrimitives（GPU 光栅化）；这里用 Pylon 新增的
// PylonFx_SubmitAddMesh 走同一条路：C# 只提交顶点，混合/采样由 GPU 完成，不读屏幕、不拷贝。
//
// 视觉口径与旧的 XiheTri / XiheTrail2 完全一致（同一套局部 uv → 图集 cell、顶点色、Colorize、
// Trail.fx pass 0），所以画面不变，只是不再逐像素循环。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;

namespace XiheBoss
{
    internal static class XiheMesh
    {
        internal static ScreenShader GeoShader;
        internal static ScreenShader TrailShader;

        private sealed class GeoBatch
        {
            public Tex Tex;
            public bool TexOn;
            public bool Colorize;
            public bool WrapU;
            public ScreenMeshBlend Blend;
            public readonly List<float> Verts = new();
        }

        private sealed class TrailBatch
        {
            public Tex Shape;
            public Tex ColorTex;
            public Tex Overlay;
            public ScreenMeshBlend Blend;
            public readonly List<float> Verts = new();
        }

        private static readonly List<GeoBatch> GeoBatches = new();
        private static readonly List<TrailBatch> TrailBatches = new();

        private static float _sw = 1f, _sh = 1f;
        private static Vector2 _sp;

        internal static void Init(Mod mod, ModLogger log)
        {
            try
            {
                GeoShader = ScreenShader.FromFileMesh(mod, "assets/effects/XiheGeo.frag");
                TrailShader = ScreenShader.FromFileMesh(mod, "assets/effects/XiheTrailFx.frag");
                log?.Info("[mesh] XiheGeo/XiheTrailFx 已注册（硬件几何：不读屏幕、不做屏幕拷贝）");
            }
            catch (Exception e)
            {
                log?.Error("[mesh] 几何着色器注册失败，退回旧的全屏图元通道：" + e.Message);
            }
        }

        internal static void Reset()
        {
            GeoBatches.Clear();
            TrailBatches.Clear();
        }

        /// <summary>每帧在收集图元之前调一次：把世界坐标换成屏幕分数。</summary>
        internal static void BeginFrame()
        {
            _sw = Math.Max(1, Main.screenWidth);
            _sh = Math.Max(1, Main.screenHeight);
            _sp = Main.screenPosition;
        }

        // ================= 收集 =================

        private static GeoBatch GeoFor(Tex tex, bool texOn, bool colorize, bool wrapU, ScreenMeshBlend blend)
        {
            for (var i = 0; i < GeoBatches.Count; i++)
            {
                var b = GeoBatches[i];
                if (b.Tex == tex && b.TexOn == texOn && b.Colorize == colorize
                    && b.WrapU == wrapU && b.Blend == blend)
                    return b;
            }

            var nb = new GeoBatch
            {
                Tex = tex, TexOn = texOn, Colorize = colorize, WrapU = wrapU, Blend = blend,
            };
            GeoBatches.Add(nb);
            return nb;
        }

        private static TrailBatch TrailFor(Tex shape, Tex colorTex, Tex overlay, ScreenMeshBlend blend)
        {
            for (var i = 0; i < TrailBatches.Count; i++)
            {
                var b = TrailBatches[i];
                if (b.Shape == shape && b.ColorTex == colorTex && b.Overlay == overlay && b.Blend == blend)
                    return b;
            }

            var nb = new TrailBatch
            {
                Shape = shape, ColorTex = colorTex, Overlay = overlay, Blend = blend,
            };
            TrailBatches.Add(nb);
            return nb;
        }

        private static void Vertex(List<float> v, Vector2 world, float u, float vv, Color color,
            float w = 1f, float t = 0f)
        {
            v.Add((world.X - _sp.X) / _sw);
            v.Add((world.Y - _sp.Y) / _sh);
            v.Add(u);
            v.Add(vv);
            v.Add(color.R / 255f);
            v.Add(color.G / 255f);
            v.Add(color.B / 255f);
            v.Add(color.A / 255f);
            v.Add(w);
            v.Add(t);
            v.Add(0f);
            v.Add(0f);
        }

        /// <summary>MEAC 的一次 SpriteBatch.Draw（位置/旋转/缩放/origin/源矩形 照旧）。</summary>
        internal static void AddSprite(SpritePrim s)
        {
            var texW = XiheTextures.WidthOf(s.Tex);
            var texH = XiheTextures.HeightOf(s.Tex);
            if (texW <= 0 || texH <= 0 || s.Source.Width <= 0 || s.Source.Height <= 0) return;

            var blend = s.Additive ? ScreenMeshBlend.Additive : ScreenMeshBlend.Alpha;
            var batch = GeoFor(s.Tex, true, s.Colorize, s.WrapU, blend);

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

            var verts = batch.Verts;
            var c = s.Color;
            Vertex(verts, tl, u0, v0, c);
            Vertex(verts, tr, u1, v0, c);
            Vertex(verts, bl, u0, v1, c);
            Vertex(verts, tr, u1, v0, c);
            Vertex(verts, br, u1, v1, c);
            Vertex(verts, bl, u0, v1, c);
        }

        /// <summary>MEAC DrawUserPrimitives(VertexPositionColorTexture) 的三角形列表。</summary>
        internal static void AddGeometry(Tex tex, bool texOn, bool colorize, bool wrapU, bool additive,
            IReadOnlyList<TriVertex> verts)
        {
            if (verts == null || verts.Count < 3) return;
            var batch = GeoFor(tex, texOn, colorize, wrapU,
                additive ? ScreenMeshBlend.Additive : ScreenMeshBlend.Alpha);
            for (var i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                Vertex(batch.Verts, v.World, v.Uv.X, v.Uv.Y, v.Color);
            }
        }

        /// <summary>Trail.fx pass 0 的显式左右顶点（每点两个顶点）。同贴图/同混合自动合成一次画。</summary>
        internal static void AddTrail(IReadOnlyList<TrailStripPoint> points, Tex shape, Tex colorTex,
            Tex overlay, float t, bool additive, float alpha = 1f)
        {
            if (points == null || points.Count < 2) return;
            var batch = TrailFor(shape, colorTex, overlay,
                additive ? ScreenMeshBlend.Additive : ScreenMeshBlend.Alpha);
            var verts = batch.Verts;
            var color = new Color(255, 255, 255, (byte)MathHelper.Clamp(alpha * 255f, 0f, 255f));

            for (var i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];
                var b = points[i + 1];

                // 源顶点顺序：+normal (v=1) 然后 -normal (v=0)；三角形条带按对展开。
                Vertex(verts, a.Left, a.UvX, 1f, color, a.W, t);
                Vertex(verts, a.Right, a.UvX, 0f, color, a.W, t);
                Vertex(verts, b.Left, b.UvX, 1f, color, b.W, t);

                Vertex(verts, a.Right, a.UvX, 0f, color, a.W, t);
                Vertex(verts, b.Right, b.UvX, 0f, color, b.W, t);
                Vertex(verts, b.Left, b.UvX, 1f, color, b.W, t);
            }
        }

        /// <summary>已经展开好的 Trail.fx 四边形（C# 侧按源顶点对算好 L/R）。</summary>
        internal static void AddTrailQuads(TrailQuadGroup group)
        {
            if (group == null || group.Quads.Count == 0) return;
            var batch = TrailFor(group.Shape, group.ColorTex, group.Overlay,
                group.Additive ? ScreenMeshBlend.Additive : ScreenMeshBlend.Alpha);
            var verts = batch.Verts;
            foreach (var q in group.Quads)
            {
                var color = new Color(255, 255, 255,
                    (byte)MathHelper.Clamp(q.Alpha * 255f, 0f, 255f));

                Vertex(verts, q.L0, q.U0, 1f, color, q.W0, q.T);
                Vertex(verts, q.R0, q.U0, 0f, color, q.W0, q.T);
                Vertex(verts, q.L1, q.U1, 1f, color, q.W1, q.T);

                Vertex(verts, q.R0, q.U0, 0f, color, q.W0, q.T);
                Vertex(verts, q.R1, q.U1, 0f, color, q.W1, q.T);
                Vertex(verts, q.L1, q.U1, 1f, color, q.W1, q.T);
            }
        }

        // ================= 提交 =================

        internal static void Submit(ScreenFrame frame)
        {
            if (frame == null) return;

            // 拖尾先画（源里 trail 在精灵/环之前），再画三角形/精灵，最后太阳。
            if (TrailShader != null)
            {
                foreach (var b in TrailBatches)
                {
                    if (b.Verts.Count < 6) continue;
                    var fx = new ScreenEffect(TrailShader)
                    {
                        Layer = ScreenEffectLayer.World,
                        Mesh = true,
                        MeshBlend = b.Blend,
                        MeshVertices = b.Verts.ToArray(),
                    };
                    var shape = XiheTextures.Cell(b.Shape);
                    var color = XiheTextures.Cell(b.ColorTex);
                    var overlay = XiheTextures.Cell(b.Overlay);
                    fx.Uniform("uCellShape", shape.U0, shape.V0, shape.DU, shape.DV);
                    fx.Uniform("uCellImage", 0f, 0f, 1f, 1f);
                    fx.Uniform("uCellColor", color.U0, color.V0, color.DU, color.DV);
                    if (XiheTextures.Atlas != null) fx.Texture("uAtlas", XiheTextures.Atlas);
                    frame.Add(fx);
                }
            }

            if (GeoShader != null)
            {
                foreach (var b in GeoBatches)
                {
                    if (b.Verts.Count < 6) continue;
                    var fx = new ScreenEffect(GeoShader)
                    {
                        Layer = ScreenEffectLayer.World,
                        Mesh = true,
                        MeshBlend = b.Blend,
                        MeshVertices = b.Verts.ToArray(),
                    };
                    var cell = XiheTextures.Cell(b.Tex);
                    fx.Uniform("uCell", cell.U0, cell.V0, cell.DU, cell.DV);
                    fx.Uniform("uTexOn", b.TexOn ? 1f : 0f);
                    fx.Uniform("uWrapU", b.WrapU ? 1f : 0f);
                    fx.Uniform("uColorize", b.Colorize ? 1f : 0f);
                    if (XiheTextures.Atlas != null) fx.Texture("uAtlas", XiheTextures.Atlas);
                    frame.Add(fx);
                }
            }
        }
    }
}
