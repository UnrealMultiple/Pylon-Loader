package dev.pylon.loader.game.core

/**
 * 层：宿主层 / 内核桥 —— M2 的引用自检清单（**数据**，不是逻辑）
 *
 * 内核不认识具体游戏名：只按 `class:/field:/method:` 语法去 IL2CPP 里查，所以"要查什么"
 * 必须由这一侧给。M4 之后清单改由 Mod 的 manifest 生成，M2 先用能证明"按名解析 + 引用校验"
 * 整条链路通的最小集合。
 * 语法（docs/08 §2.2）：class:<ns>.<Name>；field:<ns>.<Class>::<field>；method:<ns>.<Class>::<name>/<argc>。
 * 选这些名字：它们在 refs/classes.dex 与 global-metadata 里都可读（docs/01「代码未混淆」），
 * 不依赖硬编码偏移，符合项目纪律。
 */
internal object RefSpecs {

    /**
     * M2 自检清单。三类解析各来几条，**每个名字都在 D:\INT\Il2CppDumper\dump.cs
     * （本机构建的完整 dump）里核对过**，且内核实测 offset 与 dump 写的一致：
     *
     *   dump: `public static bool dayTime; // 0x480`      → 内核 static offset=1152  (0x480) ✓
     *   dump: `public static Main instance; // 0x28`      → 内核 static offset=40    (0x28) ✓
     *   dump: `public string name; // 0xE0`（Player）      → 内核 instance offset=224 (0xE0) ✓
     *   dump: `protected void DrawMenu(GameTime)`         → 内核 argc=1 ✓
     *   dump: `public void SetDefaults(int Type, ItemVariant variant)` → 内核 argc=2 ✓
     *
     * 两条方法**写死参数个数**（严格匹配，Mod 清单里推荐这种）；`UnityEngine.GameObject`
     * 故意留着——它在 UnityEngine.CoreModule.dll 里，能证明"扫全部程序集"那条退路没被删。
     * 注意：静态字段的 offset 是**静态块内**偏移（dayTime=1152），不是 0，也不是实例偏移；
     * 是不是静态看 `field_get_flags`（日志里的 static/instance 就是它）。
     */
    val M2_SELF_CHECK: List<String> = listOf(
        "# M2 自检：名字均与 dump.cs 核对过，三条 offset 与内核实测一致",
        "class:Terraria.Main",
        "class:Terraria.Item",
        "class:Terraria.Player",
        "class:UnityEngine.GameObject",
        "field:Terraria.Main::dayTime",
        "field:Terraria.Main::instance",
        "field:Terraria.Player::name",
        "method:Terraria.Main::DrawMenu/1",
        "method:Terraria.Item::SetDefaults/2",
    )

    /** 启动器经 debug action 传"故意写错"的引用，验证"校验失败 → 停用而不是崩"（docs/08 §6 反例）。 */
    fun withBogus(bogus: String?): List<String> {
        if (bogus.isNullOrBlank()) return M2_SELF_CHECK
        return M2_SELF_CHECK + bogus
    }

    /**
     * 调试用：整份替换清单（`--es refsProbe "field:A::x,method:B::y"`）。
     * 理由：写 M4 的 Mod 清单时，先用内核把候选名字跑一遍，看哪些真存在、方法的真实参数个数多少；
     * 内核只认清单、不认游戏名，这里只是把清单来源换成命令行。
     */
    fun resolve(customSpecs: String?, bogus: String?): List<String> {
        if (!customSpecs.isNullOrBlank()) {
            return customSpecs.split(',').map { it.trim() }.filter { it.isNotEmpty() }
        }
        return withBogus(bogus)
    }
}
