using Il2CppInterop.Runtime.InteropTypes;

namespace Pylon.ModLoader;

/// <summary>
/// 实体地址 → 内容实例的绑定表。用地址而不是引用做键：游戏销毁实体后条目自然失联，
/// 槽位复用时由重绑覆盖（见各 Loaders 的 Rebind 调用）。
/// </summary>
internal sealed class EntityBinding<TEntity, TInstance>
    where TEntity : class
    where TInstance : class
{
    private readonly Dictionary<long, TInstance> _byEntity = [];

    public TInstance? Get(TEntity entity)
    {
        var address = Address(entity);
        return address != 0 && _byEntity.TryGetValue(address, out var instance) ? instance : null;
    }

    public TInstance Bind(TEntity entity, TInstance instance)
    {
        var address = Address(entity);
        if (address != 0) _byEntity[address] = instance;
        return instance;
    }

    public static long Address(TEntity? entity) => entity is Il2CppObjectBase obj ? (long)obj.Pointer : 0;
}
