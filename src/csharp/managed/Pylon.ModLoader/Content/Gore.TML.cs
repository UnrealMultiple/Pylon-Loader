using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>Gore 的作者扩展门面：interop 代理不能写 partial，用 extension block 补 ModGore 关联与带源生成入口。</summary>
public static class GoreTml
{
    extension(Gore gore)
    {
        public ModGore? ModGore => GoreTable.Of(gore.type);
    }

    extension(Gore)
    {
        public static int NewGore(IEntitySource source, Vector2 position, Vector2 velocity, int type, float scale = 1f)
        {
            GorePipeline.PendingSource = source;
            try { return Gore.NewGore(position, velocity, type, scale); }
            finally { GorePipeline.PendingSource = null; }
        }

        public static Gore NewGoreDirect(IEntitySource source, Vector2 position, Vector2 velocity, int type, float scale = 1f)
        {
            var index = NewGore(source, position, velocity, type, scale);
            return Main.gore[index];
        }

        public static Gore NewGorePerfect(IEntitySource source, Vector2 position, Vector2 velocity, int type, float scale = 1f)
        {
            GorePipeline.PendingSource = source;
            try { return Gore.NewGorePerfect(position, velocity, type, scale); }
            finally { GorePipeline.PendingSource = null; }
        }
    }
}
