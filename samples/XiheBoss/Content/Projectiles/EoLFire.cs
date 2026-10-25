// EoLFire（MEAC：MEAC.Bosses.EoL.Projectiles.EoLFire）—— 羲和 CrossRay 阶段绕 Boss 旋转的「火环」：
// 本体钉在 Boss 身上，velocity 每 tick 旋转 ai[1]，并沿途不停留下节点（Node）拖尾；命中沿节点折线判定。
//
// 源文件：.work\meac\MEAC.Bosses.EoL.Projectiles\EoLFire.cs —— AI / Colliding / PreDraw 逐行照抄。
// ai 约定（PORTING.md）：ai[0] = 宿主 NPC 下标，ai[1] = 每 tick 的旋转弧度（Boss 传 -ai2*0.033）。
//
// 视觉（源 PreDraw / DrawWarp）：把 nodes 当 3D 顶点条带（贴图 tex4 / Extra1），半宽 = node.width*3，
//   加色混合，逐段按 factor 淡出。这里沿节点画若干段 beam（Tex.Tex4）近似。
// 近似点：
//   1) 源的 MEAC.Entities.Node 没有出现在提取树里（.work\meac 下只有用到它的 EoLFire.cs）：
//      · 构造参数按 (position, velocity, int timeleft, float width, int type) 解释，即 timeleft=10、width=50f
//        （原调用是 new Node(center, velocity*10, 10, 50f, 1) { type = 2 }；50f 只能是 float 形参，
//          所以第 4 个参数是 width；这一条无法从提取源验证）；
//      · Update 按「position += velocity；velocity *= 0.9；timeleft--」补的 —— 这是本文件最大的不确定点；
//   2) 源的 3D 顶点条带用 fragment 着色器做颜色渐变，这里改成每段一根 beam + 按段 alpha 淡出；
//   3) 源 IWarpProj.DrawWarp（Extra1 贴图 + EoLWarp 全屏 warp）在 Pylon 没有全局 warp 弹幕接口，未接；
//   4) 节点的数组按 whoAmI 存在静态表里 —— PushVisual 是 static，按约定不能强转 ModProjectile。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace XiheBoss
{
    public sealed class EoLFire : ModProjectile
    {
        /// <summary>MEAC.Entities.Node 的移植（按 MEAC.dll 的 Node.Update IL 还原）。</summary>
        private sealed class Node
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public int TimeLeft;
            public int MaxTimeLeft;
            public float Width;
            public int Type;

            public Node(Vector2 position, Vector2 velocity, int maxTimeLeft, float width, int type)
            {
                Position = position;
                Velocity = velocity;
                TimeLeft = maxTimeLeft;
                MaxTimeLeft = maxTimeLeft;
                Width = width;
                Type = type;
            }

            public void Update()
            {
                // MEAC.Node.Update：type 0 在 timeleft < 0.6*max 时 velocity*=0.93、width*=0.92；
                // type 1 无衰减；type 2 在 timeleft < 0.7*max 时 velocity*=0.92。
                if (Type == 0)
                {
                    if (TimeLeft < MaxTimeLeft * 0.6f)
                    {
                        Velocity *= 0.93f;
                        Width *= 0.92f;
                    }
                }
                else if (Type == 2)
                {
                    if (TimeLeft < MaxTimeLeft * 0.7f)
                    {
                        Velocity *= 0.92f;
                    }
                }

                Position += Velocity;
                TimeLeft--;
            }
        }

        /// <summary>按 whoAmI 索引的节点表（见文件头近似点 4）。</summary>
        private static readonly Dictionary<int, List<Node>> NodeTable = new();

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 15;
            proj.height = 15;
            proj.aiStyle = -1;
            proj.hostile = true;
            proj.friendly = false;
            proj.timeLeft = 100;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 1.5f;
        }

        public override void OnSpawn(Projectile proj)
        {
            NodeTable[proj.whoAmI] = new List<Node>();
        }

        public override void OnKill(Projectile proj, int timeLeft)
        {
            NodeTable.Remove(proj.whoAmI);
        }

        // 源：位置完全由 AI 钉在宿主身上。
        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override void AI(Projectile proj)
        {
            var ai = proj.ai;

            var idx = (int)ai[0];
            if (idx < 0 || idx >= Main.npc.Length)
            {
                proj.Kill();
                return;
            }

            var npc = Main.npc[idx];
            if (npc.active) proj.Center = npc.Center;
            else
            {
                proj.Kill();
                return;
            }

            var nodes = NodesOf(proj);
            if (nodes == null)
            {
                nodes = new List<Node>();
                NodeTable[proj.whoAmI] = nodes;
            }

            for (var i = 0; i < nodes.Count; i++) nodes[i].Update();

            // 源就是这种写法（RemoveAt 之后不回头，可能漏掉一个同 tick 到期的节点），照抄。
            for (var j = 0; j < nodes.Count; j++)
            {
                if (nodes[j].TimeLeft <= 0) nodes.RemoveAt(j);
            }

            // 源：velocity = Utils.RotatedBy(velocity, ai[1])。
            proj.velocity = XiheHelper.RotatedBy(proj.velocity, ai[1]);
            if (proj.timeLeft < 20) proj.velocity *= 0.8f;

            for (var k = 0; k < nodes.Count; k++)
            {
                var n = nodes[k];
                if (XiheHelper.RandBool(8) && n.TimeLeft > 3)
                {
                    var pos = n.Position + proj.velocity + n.Velocity * 0.2f
                              + XiheHelper.NextVector2Unit() * n.Width;
                    XiheVisuals.Create(VfxKind.LightDust, pos, n.Velocity * 0.05f);
                }
            }

            // 源：new Node(Center, velocity*10, timeleft 10, width 50, type 2)。
            nodes.Add(new Node(proj.Center, proj.velocity * 10f, 10, 50f, 2));
        }

        // 源：沿节点折线每 2 个节点取一段，半宽 node.width*0.7；其余交给原生矩形。
        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            var nodes = NodesOf(proj);
            if (nodes != null)
            {
                var point = 0f;
                for (var i = 0; i < nodes.Count - 2; i += 2)
                {
                    if (Collision.CheckAABBvLineCollision(Utils.TopLeft(targetHitbox), Utils.Size(targetHitbox),
                            nodes[i].Position, nodes[i + 2].Position, nodes[i].Width * 0.7f, ref point))
                    {
                        return true;
                    }
                }
            }

            // 源：return base.Colliding(...) —— Pylon 里 null 就是游戏原生矩形判定。
            return null;
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        private static List<Node> NodesOf(Projectile proj)
        {
            NodeTable.TryGetValue(proj.whoAmI, out var nodes);
            return nodes;
        }

        /// <summary>源 Draw：节点折线的 Trail.fx pass 0（tex4 + img_color + Projectile_0），逐点显式左右顶点。</summary>
        public static void PushVisual(Projectile p)
        {
            var nodes = NodesOf(p);
            if (nodes == null || nodes.Count < 3) return;

            var points = new List<TrailStripPoint>(nodes.Count);
            for (var i = 1; i < nodes.Count && nodes[i].Position != Vector2.Zero; i++)
            {
                var normalDir = nodes[i - 1].Position - nodes[i].Position;
                normalDir = XiheHelper.Normalize2(new Vector2(-normalDir.Y, normalDir.X));
                var factor = 1f - (float)i / nodes.Count;
                var w = MathHelper.Lerp(2f, 0.1f, factor);
                var width = nodes[i].Width * 3f;
                points.Add(new TrailStripPoint
                {
                    Left = nodes[i].Position + normalDir * width,
                    Right = nodes[i].Position - normalDir * width,
                    UvX = (float)Math.Sqrt(factor),
                    W = w,
                });
            }

            if (points.Count < 2) return;

            // 源 Draw：Trail.fx pass 0（tex4 + img_color + Projectile_0，Additive），
            // t = timeLeft * 0.04。走精确的 Trail.fx 逐像素通道（硬件几何）。
            XiheFx.TrailStrip(points, Tex.Tex4, Tex.ImgColor, Tex.Transparent,
                p.timeLeft * 0.04f, additive: true);
        }
    }
}





