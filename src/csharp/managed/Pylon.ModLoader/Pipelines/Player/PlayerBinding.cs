using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>玩家实例保管：模板表 + 每玩家一份实例数组；键是 Player 指针而不是 whoAmI（同一槽位会换人）。</summary>
internal sealed class PlayerBinding
{
    private readonly List<ModPlayer> _templates = [];
    private readonly Dictionary<nint, ModPlayer[]> _instances = [];
    private readonly HashSet<HookId> _active = [];

    internal int Count => _templates.Count;

    internal IReadOnlyList<ModPlayer> Templates => _templates;

    internal void Register(ModPlayer template)
    {
        template.Index = (ushort)_templates.Count;
        _templates.Add(template);
    }

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

    internal bool Active(HookId hook) => _active.Contains(hook);

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
            instance.Index = _templates[i].Index;
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
