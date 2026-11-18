using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>基线 Main.TML.cs 的等价扩展成员；游戏类型来自 interop 代理，用 extension block 补静态成员。</summary>
public static class MainTml
{
    extension(Terraria.Main)
    {
        public static Color DiscoColor =>
            new(Terraria.Main.DiscoR, Terraria.Main.DiscoG, Terraria.Main.DiscoB);

        public static IEnumerable<Projectile> ActiveProjectiles
        {
            get
            {
                for (var i = 0; i < Terraria.Main.maxProjectiles; i++)
                {
                    var projectile = Terraria.Main.projectile[i];
                    if (projectile.active) yield return projectile;
                }
            }
        }

        public static void NewText(object o, Color? color = null) =>
            Terraria.Main.NewText(o is LocalizedText text ? text.Value : o?.ToString(),
                color ?? new Color(255, 255, 255, 255), false);
    }
}
