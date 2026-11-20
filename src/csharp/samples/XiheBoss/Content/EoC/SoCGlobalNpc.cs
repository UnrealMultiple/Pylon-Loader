using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader.Globals;
using Terraria;
using Terraria.ID;

namespace XiheBoss.EoC
{
    public sealed class SoCGlobalNpc : GlobalNpc
    {
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) =>
            entity.type == NPCID.ServantofCthulhu;

        public override bool PreAI(NPC npc)
        {
            var ai = XiheHelper.ReadAi(npc);
            if (ai[0] == 0f)
            {
                var pos = npc.position;
                pos += npc.velocity * 0.8f;
                npc.position = pos;
            }
            if (ai[0] == 1f)
            {
                var parent = Main.npc[(int)ai[1]];
                if (parent.ai[3] == 100f && ai[2] == 0f)
                {
                    ai[2] = 1f;
                    for (int i = 0; i < 10; i++)
                    {
                        Dust obj2 = Dust.NewDustDirect(npc.Center, 0, 0, 182, 0f, 0f, 0, default(Color), 1.5f);
                        obj2.velocity = XiheHelper.Vxr(i * ((float)Math.PI * 2f) / 10f) * 5f;
                        obj2.noGravity = true;
                        obj2.noLight = true;
                    }
                    Player player = Main.player[Player.FindClosest(npc.Center, 1000, 1000)];
                    if (player != null)
                    {
                        npc.velocity = npc.DirectionTo(player.Center) * 5f;
                    }
                }
                if (ai[2] == 1f)
                {
                    if (npc.velocity.Length() < 30f)
                    {
                        var av = npc.velocity;
                        av *= 1.025f;
                        npc.velocity = av;
                    }
                    npc.rotation = Utils.ToRotation(npc.velocity);
                }
            }
            XiheHelper.WriteAi(npc, ai[0], ai[1], ai[2], ai[3]);
            return true;
        }
    }
}
