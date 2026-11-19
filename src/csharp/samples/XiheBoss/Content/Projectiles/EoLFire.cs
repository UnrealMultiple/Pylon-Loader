using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{
    public sealed class EoLFire : ModProjectile
    {

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

        private static readonly Dictionary<int, List<Node>> NodeTable = new();

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
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

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            var proj = Projectile;
            NodeTable[proj.whoAmI] = new List<Node>();
        }

        public override void OnKill(int timeLeft)
        {
            var proj = Projectile;
            NodeTable.Remove(proj.whoAmI);
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            var proj = Projectile;
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

            for (var j = 0; j < nodes.Count; j++)
            {
                if (nodes[j].TimeLeft <= 0) nodes.RemoveAt(j);
            }

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

            nodes.Add(new Node(proj.Center, proj.velocity * 10f, 10, 50f, 2));
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            var proj = Projectile;
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

            return null;
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        private static List<Node> NodesOf(Projectile proj)
        {
            NodeTable.TryGetValue(proj.whoAmI, out var nodes);
            return nodes;
        }

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

            XiheFx.TrailStrip(points, Tex.Tex4, Tex.ImgColor, Tex.Transparent,
                p.timeLeft * 0.04f, additive: true);

            var extent = 60f;
            for (var i = 0; i < nodes.Count; i++)
                extent = Math.Max(extent, Vector2.Distance(p.Center, nodes[i].Position) + nodes[i].Width * 3f);
            XiheFx.WarpTrail(p.Center, extent, 0.02f);
        }
    }
}
