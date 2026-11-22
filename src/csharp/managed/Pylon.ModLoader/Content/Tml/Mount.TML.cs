namespace Pylon.ModLoader.Content;
/// <summary>Mount.TML.cs 的等价扩展成员：按挂载实例挂任意托管数据的侧字典（tML <c>Mount._mountSpecificData</c>）。</summary>
public static class MountTml
{
    private static readonly object Gate = new();
    private static readonly Dictionary<nint, object?> Data = [];

    extension(Terraria.Mount mount)
    {
        /// <summary>
        /// 挂载点附加数据槽（tML <c>Mount._mountSpecificData</c> 的对应物）。interop 的原生 object 字段
        /// 放不进纯托管对象，这边按挂载实例指针存侧字典；换骑 / 下骑 / 坐骑表重建时由 Mount 管线清空，
        /// 生命周期与原版字段一致。原版挂载也可以挂（读原版字段语义请走 <c>Mount._mountSpecificData</c> 本体）。
        /// </summary>
        public object? MountSpecificData
        {
            get { lock (Gate) return Data.TryGetValue(mount.Pointer, out var value) ? value : null; }
            set
            {
                lock (Gate)
                {
                    if (value is null) Data.Remove(mount.Pointer);
                    else Data[mount.Pointer] = value;
                }
            }
        }
    }

    /// <summary>清掉这个挂载实例的附加数据（换骑 / 下骑时由 Mount 管线调）。</summary>
    internal static void Clear(nint pointer)
    {
        lock (Gate) Data.Remove(pointer);
    }

    /// <summary>清掉全部附加数据（坐骑表重建时由 Mount 管线调）。</summary>
    internal static void ClearAll()
    {
        lock (Gate) Data.Clear();
    }
}
