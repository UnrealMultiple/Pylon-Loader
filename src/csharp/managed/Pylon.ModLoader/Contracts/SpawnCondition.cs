namespace Pylon.ModLoader;

/// <summary>游戏的天然生成条件（照 tModLoader 的 SpawnCondition）：每个条件说当前抽出里落到它的份额（Chance，0..1）；SpawnChance 里这样用：<c>SpawnCondition.OverworldNightMonster.Chance * 0.1</c>。</summary>
public class SpawnCondition
{
    /// <summary>这次抽出里落到这个条件的份额（0..1）；只在有人读时算，一次抽出一次，没 Begin 过是 0。</summary>
    public float Chance { get; internal set; }

    /// <summary>这个条件在这次抽出里算过没有（false = 没轮到它）。</summary>
    public bool Active { get; internal set; }

    /// <summary>这个条件在抽里的权重（构造默认 1）；抽到时 WeightFunc 非空就按它重算。</summary>
    public float BlockWeight { get; internal set; } = 1f;

    /// <summary>抽时重算 BlockWeight 的函数（参考里 Wraith/MartianProbe/GoblinScout/OverworldFirefly 在用）；null = 用初值。</summary>
    public Func<float> WeightFunc { get; set; } = null!;
}

// 未移植 constructor、Begin/CheckAll 与静态条件树：判定与 info 用的 NPCSpawnInfo 这版游戏没有
// （dump.cs 只有原生 NPC.Spawner），树与评估又归内部分发管线；等 NPCSpawnInfo 管线定形后一起补。
