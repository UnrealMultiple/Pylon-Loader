using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>
///     进世界后把测试物品塞到热键栏第 1 格：没有这个物品就没法用肉眼验证物品介绍/介绍行。
///     只在 Player.PostUpdate 里补一次（拿到就是拿到了，之后不再动背包）。
/// </summary>
public sealed class TestPlayer : ModPlayer
{
    private bool _given;

    public override void PostUpdate(Player player)
    {
        if (_given) return;
        if (player is null || !player.active) return;

        var type = ModContent.TypeOf<TestItem>();
        if (type <= 0) return;

        var inv = player.inventory;
        if (inv is null || inv.Length == 0) return;

        for (var i = 0; i < inv.Length; i++)
        {
            var slot = inv[i];
            if (slot is not null && slot.type == type)
            {
                _given = true;
                return;
            }
        }

        var item = new Item();
        item.SetDefaults(type, null);
        item.stack = 1;

        inv[0] = item;
        _given = true;

        PT.Log2?.Info($"[PT] 已把测试物品放进热键栏第 1 格（type={type} 玩家槽位数={inv.Length}）");
    }
}
