# 贡献指南

欢迎 issue 与 pull request。这个仓库的每一层都有硬约束，改动前请先读对应文档；本文只讲流程与规范。

## 1. 开始之前

| 想做什么 | 先读 |
|---|---|
| 改内核（C++） | [src/core/README.md](src/core/README.md) 的"硬约束" |
| 改托管层（C#） | [src/csharp/managed/README.md](src/csharp/managed/README.md) |
| 改启动器 / 宿主（Kotlin） | [架构](docs/architecture.md) 的分层与线程模型 |
| 补文档 | 本文第 5 节 |

本地环境准备与构建命令见 [构建](docs/build.md)。提交前至少跑通：

```powershell
pwsh -File src/csharp/tools/pylon.ps1 check
pwsh -File src/csharp/tools/pylon.ps1 build
```

真机相关的改动（内核、宿主、图形、导入流程）请在 PR 描述里附上 `pylon.ps1 run` 的日志片段。

## 2. 分支与提交

- 从 `main` 开分支：`feat/<范围>-<摘要>`、`fix/<范围>-<摘要>`、`docs/<摘要>`。
- 提交信息：`<范围>: <祈使句摘要>`，范围取 `app` / `game` / `core` / `managed` / `modloader` / `docs` / `tools`。
- 一个提交做一件事；格式化与逻辑改动分开，便于 review。
- 不要提交 `refs/`、`runtime/`、`interop/generated/`、`.work/`、构建产物或任何游戏文件。

## 3. 代码规范

通用：

- 不写叙述性注释。注释只解释"为什么"（时序约束、平台坑、非直觉取舍），不解释"是什么"。
- 不留死代码与被注释掉的旧实现。
- 新增行为要有可观察的证据：日志阶段行、引用报告或真机验证结论。

### 内核（C++20）

- 编译契约（flags、符号可见性、页对齐）在 [src/core/README.md](src/core/README.md)，改动等于改动对外承诺。
- 不用异常与 RTTI；错误用 `Status` / `Outcome<T>` 表达。
- 不写死偏移与类名以外的假设；解析失败只降级，不带崩进程。
- 新增导出符号必须能在导出自检里看到。

### 托管层（C#）

- `net10.0`，可空引用开启，`TreatWarningsAsErrors`，零警告基线。
- 一个文件一个公开类型；文件级命名空间；成员访问不写 `this.`。
- 已发布 API 不改成员形态（属性与字段互换会让老 Mod 抛 `MissingFieldException`）。
- 热路径不分配、不闭包、不拼字符串；分派前先判"有没有人覆写"。

### 启动器 / 宿主（Kotlin）

- 状态以 `StateFlow` 暴露，UI 只读；不要在 Composable 里做 IO。
- 跨进程契约的常量放 `src/common`（如 `RuntimeLayout`），不要各写一份字符串。
- 日志统一走项目的 `Log` 封装，阶段行保持 `[stage] +Nms` 格式。

## 4. 测试

没有单元测试基线，验证以"可复现的真机回路"为准：

| 改动 | 验证方式 |
|---|---|
| 托管层 / Mod 框架 | `pylon.ps1 managed` → 看 `[mod]` 与 `[hook]` 日志 |
| 内核 | `pylon.ps1 build` → 看 `[boot]` 阶段与引用报告 |
| 导入 / 版本门禁 | 用不同版本的已安装游戏走一遍导入，确认拒绝与放行的边界 |
| 文档 | 命令复制到终端能跑；路径与链接指向真实文件 |

PR 描述里写清：改了什么、为什么、怎么验证的、有什么已知副作用。

## 5. 文档规范

- 中文书写，GitHub 风格 Markdown；标题层级从 `#` 起，正文最多用到 `###`。
- 一份文档只回答一个问题；跨文档用相对链接互指，不复制整段内容。
- 命令一律用 `powershell` 代码块，路径用行内代码，术语保持统一：启动器 / 宿主 / 内核 / 托管层 / Mod / 导入。
- 面向读者的语气：陈述现状与约束，不写"我们尝试过…"的过程叙事；过程属于 issue 与 commit message。
- 新增能力时同步更新 [路线](docs/roadmap.md) 的"已完成 / 未实现"两张表。

## 6. 合规

- 不提交、不发布任何游戏文件或由游戏派生的代理程序集。
- 不实现、不合并绕过购买、许可或完整性校验的改动（含 PairIP）。
- 不复制 [Bunny Loader](https://github.com/JonataOliveiraa/Bunny-Loader) 等采用不兼容许可的上游代码；借鉴技术路线可以，代码请独立实现并在 PR 中说明来源。
- 新引入的第三方依赖必须与 [LICENSE](LICENSE) 兼容，并在 PR 中列出许可证。
