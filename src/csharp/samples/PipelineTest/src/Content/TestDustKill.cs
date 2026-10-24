using Terraria;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>击杀型测试尘土（B1 验证脚手架）：存活 <see cref="KillFrames"/> 帧后由作者 Update 置 dust.active=false——「当场消失 + 槽位还栈」的观察载体。</summary>
public sealed class TestDustKill : ModDust
{
    /// <summary>击杀前的存活帧数。</summary>
    public const int KillFrames = 180;

    public static int SpawnCount;
    public static int UpdateCount;
    public static bool Killed;

    /// <summary>最后一次 Spawn 返回的实例（击杀断言用）。</summary>
    public static Dust? Last;

    public static void Reset()
    {
        SpawnCount = UpdateCount = 0;
        Killed = false;
        Last = null;
    }

    public override string TexturePath => "assets/dusts/TestDust.png";

    public override void OnSpawn(Dust dust)
    {
        SpawnCount++;
        dust.noGravity = true;
        Last = dust;
    }

    public override bool Update(Dust dust)
    {
        UpdateCount++;
        if (UpdateCount >= KillFrames)
        {
            Killed = true;
            dust.active = false; // TML 惯用杀法；管线 kill tracking 应把槽位补还给 freeDust 栈
            return false;
        }
        return true;
    }
}
