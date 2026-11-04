using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 玩家实例保管：模板表 + 每玩家一份实例数组。
/// 键是 Player 的对象指针而不是 whoAmI——同一个槽位会换人。
/// </summary>
internal sealed class PlayerBinding
{
    private readonly List<ModPlayer> _templates = [];
    private readonly Dictionary<nint, ModPlayer[]> _instances = [];
    private readonly HashSet<HookId> _active = [];

    internal int Count => _templates.Count;

    /// <summary>注册期：只记模板，位集等内容都注册完再算。</summary>
    internal void Register(ModPlayer template) => _templates.Add(template);

    /// <summary>算位集与"有没有人覆写这个钩子"：分派前一次，之后是位运算与字典查找。</summary>
    internal void Refresh()
    {
        _active.Clear();

        Span<HookId> buffer = stackalloc HookId[HookMask.Capacity];

        foreach (var template in _templates)
        {
            var mask = HookScan.Of(template.GetType(), typeof(ModPlayer));
            template.Hooks = mask;

            var count = mask.Collect(buffer);
            for (var i = 0; i < count; i++) _active.Add(buffer[i]);
        }

        _instances.Clear();
    }

    /// <summary>有人覆写这个钩子吗：没人就不建实例、不进循环。</summary>
    internal bool Active(HookId hook) => _active.Contains(hook);

    /// <summary>这个玩家的实例数组；第一次问到时按模板逐个建，后来注册的模板补建在后面。</summary>
    internal ModPlayer[] Of(Player player)
    {
        var key = player.Pointer;

        if (_instances.TryGetValue(key, out var all) && all.Length == _templates.Count) return all;

        var old = all?.Length ?? 0;
        var grown = new ModPlayer[_templates.Count];
        if (all is not null) Array.Copy(all, grown, old);

        for (var i = old; i < grown.Length; i++)
        {
            var instance = (ModPlayer)Activator.CreateInstance(_templates[i].GetType())!;
            instance.Mod = _templates[i].Mod;
            instance.Hooks = _templates[i].Hooks;
            instance.Player = player;
            grown[i] = instance;
        }

        _instances[key] = grown;

        for (var i = old; i < grown.Length; i++)
        {
            try
            {
                grown[i].Initialize();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"{grown[i].GetType().Name}.Initialize 异常：{Safe.Describe(e)}");
            }
        }

        return grown;
    }
}
