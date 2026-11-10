using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Content;

internal static class AmmoProbe
{
    internal static bool Active, Block, Alter;
    internal static int Consumed, LastAmmoType, LastAmmoStack, Picked;
    internal static void Reset()
    {
        Block = Alter = false;
        Consumed = LastAmmoType = LastAmmoStack = Picked = 0;
    }
}

public sealed class TestAmmoGlobal : GlobalItem
{
    public override bool CanConsumeAmmo(Item weapon, Item ammo, Player player) =>
        !AmmoProbe.Active || !AmmoProbe.Block;

    public override void OnConsumeAmmo(Item weapon, Item ammo, Player player)
    {
        if (!AmmoProbe.Active) return;
        AmmoProbe.Consumed++;
        AmmoProbe.LastAmmoType = ammo.type;
        AmmoProbe.LastAmmoStack = ammo.stack;
    }

    public override void PickAmmo(Item weapon, Item ammo, Player player, ref int type, ref float speed,
                                  ref StatModifier damage, ref float knockback)
    {
        if (!AmmoProbe.Active) return;
        AmmoProbe.Picked++;
        if (!AmmoProbe.Alter) return;
        type = ProjectileID.WoodenArrowFriendly;
        speed = 7f;
        damage.Flat += 5f;
        knockback = 4f;
    }
}
