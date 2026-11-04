using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// NPC 实体 ↔ ModNpc 实例，外加“每类型覆写位集”的缓存。
/// 位集按 typeof(ModNpc) 算（不是 ModType）：否则 ModNpc 自己的空 virtual 会被算成覆写，门控等于没有。
/// </summary>
internal sealed class NpcBinding
{
    private readonly EntityBinding<NPC, ModNpc> _binding = new();
    private readonly Dictionary<Type, HookMask> _masks = [];

    /// <summary>把位集挂到每个模板上（绑定时从模板复制到实例）。每次 Mod 注册后跑一次。</summary>
    public void Prime()
    {
        foreach (var npc in ModContent.GetContent<ModNpc>()) npc.Hooks = MaskOf(npc);
    }

    /// <summary>原版类型一次比较就返回：每帧每只 NPC 的每个钩子都会走这里。</summary>
    public ModNpc? InstanceOf(NPC? entity)
    {
        if (entity is null) return null;
        if (TemplateOf(entity.type) is not { } template) return null;

        var current = _binding.Get(entity);
        return current is not null && current.Type == entity.type ? current : Bind(template, entity);
    }

    /// <summary>SetDefaults 时强制换新实例：槽位复用与同类型重生都不继承上一只的状态。</summary>
    public ModNpc? Rebind(NPC? entity) =>
        entity is null || TemplateOf(entity.type) is not { } template ? null : Bind(template, entity);

    /// <summary>CloneDefaults 借用别的类型的 SetDefaults 之后，把绑定还原成原实例。</summary>
    public void RebindTo(NPC? entity, ModNpc? instance)
    {
        if (entity is null || instance is null) return;
        _binding.Bind(entity, instance);
    }

    public static long Address(NPC? entity) => EntityBinding<NPC, ModNpc>.Address(entity);

    private static ModNpc? TemplateOf(int type) =>
        type < VanillaCounts.Npc ? null : ModContent.Find(ContentKind.Npc, type) as ModNpc;

    private HookMask MaskOf(ModNpc template)
    {
        if (_masks.TryGetValue(template.GetType(), out var cached)) return cached;

        var mask = HookScan.Of(template.GetType(), typeof(ModNpc));
        _masks[template.GetType()] = mask;
        return mask;
    }

    private ModNpc Bind(ModNpc template, NPC entity)
    {
        var instance = (ModNpc)Activator.CreateInstance(template.GetType())!;
        instance.Type = template.Type;
        instance.Registered = template.Registered;
        instance.Mod = template.Mod;
        instance.Plan = template.Plan;
        instance.Hooks = template.Hooks.IsEmpty ? MaskOf(template) : template.Hooks;
        instance.Texture = template.Texture;
        instance.BoundEntity = entity;
        return _binding.Bind(entity, instance);
    }
}
