using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>基线 EntitySources.TML.cs 的等价扩展成员。</summary>
public static class EntitySourcesTml
{
    extension(EntitySource_TileBreak)
    {
        public static EntitySource_TileBreak Create(int tileCoordsX, int tileCoordsY, string? context = null) =>
            new(tileCoordsX, tileCoordsY);
    }

    extension(Player player)
    {
        public IEntitySource GetSource_FromThis(string? context = null) =>
            Wrap(new EntitySource_Parent(new IEntitySourceTarget((nint)player.Pointer)));
    }

    extension(NPC npc)
    {
        public IEntitySource GetSource_FromThis(string? context = null) =>
            Wrap(new EntitySource_Parent(new IEntitySourceTarget((nint)npc.Pointer)));
    }

    extension(Projectile projectile)
    {
        public IEntitySource GetSource_FromThis(string? context = null) =>
            Wrap(new EntitySource_Parent(new IEntitySourceTarget((nint)projectile.Pointer)));
    }

    private static IEntitySource Wrap(EntitySource_Parent source) => new((nint)source.Pointer);
}
