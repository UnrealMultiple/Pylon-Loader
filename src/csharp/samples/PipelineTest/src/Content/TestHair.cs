using System.Collections.Generic;
using Pylon.ModLoader;

namespace PipelineTest.Content;

/// <summary>测试发型：主贴图 assets/hairs/TestHair.png；建号菜单可用。</summary>
public sealed class TestHair : ModHair
{
    public static bool StaticDefaultsRan;

    public override void SetStaticDefaults() => StaticDefaultsRan = true;
}

/// <summary>锁定发型：GetUnlockConditions 返回永假条件 → 理发师不可选。</summary>
public sealed class TestLockedHair : ModHair
{
    public override string TexturePath => "assets/hairs/TestHair.png";

    public override bool AvailableDuringCharacterCreation => false;

    public override List<Condition> GetUnlockConditions() =>
    [
        new Condition("TestLockedHair.Locked", static () => false),
    ];
}