using System.Collections.Generic;
using Terraria.WorldBuilding;

namespace Pylon.ModLoader;

/// <summary>系统内容基类的世界生成面：PreWorldGen → ModifyWorldGenTasks → 生成 → PostWorldGen，困难模式另走 ModifyHardmodeTasks。</summary>
public abstract partial class ModSystem
{
    public virtual void PreWorldGen() { }

    public virtual void ModifyWorldGenTasks(List<GenPass> tasks) { }

    public virtual void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
    {
        double previousWeight = SystemLoader.WeightOf(tasks);
        ModifyWorldGenTasks(tasks);
        totalWeight += SystemLoader.WeightOf(tasks) - previousWeight;
    }

    public virtual void PostWorldGen() { }

    public virtual void ModifyHardmodeTasks(List<GenPass> list) { }
}
