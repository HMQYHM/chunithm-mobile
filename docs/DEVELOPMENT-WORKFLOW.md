# Unity 开发流程

## 范围与基线

主工程是 `chunithm-mobile-unity`，联机服务端位于相邻的 `chunithm-multiplayer-server`。不要修改旧 Android/Kotlin 工程或构建副本来代替主工程。Unity 版本由 `ProjectSettings/ProjectVersion.txt` 指定；Android 构建设置和输出由 `Assets/Editor/AndroidBuildConfigurator.cs` 决定。`Build/`、`Library/` 和日志均为生成物，不作为源码提交。

每次开始先运行 `git status --short`，识别已有修改。只修改本次需求涉及的文件；对已有修改作增量编辑，不使用 reset/checkout 清理现场。当前工作区可能包含其他未提交工作，不能把整个工作区的 diff 视作本次改动。

## 一次改动的闭环

1. **确定行为**：写清复现步骤、期望行为、涉及单人/联机、谱面、设备和设置。读调用链和现有回归测试；原版行为未证实时不要在注释中称为“原版规则”。
2. **控制范围**：在数据、输入、判定、时间、渲染、UI 或服务端的责任层修改。跨层契约变更要检查调用方，联机协议变更同时检查服务端校验与兼容性。
3. **注释与测试**：为新接口和不直观的时序/资源所有权写相邻注释；保持单位明确（毫秒、谱面时间、DSP 时间、真实时间、lane）。修 bug 时新增能先复现、后通过的 Editor 自测，并保留旧边界测试。
4. **预检**：在工程根目录运行 `./tools/develop.ps1 -Mode Preflight`。它检查结构、工作区状态和空白错误；并不证明 Unity 编译或设备行为正确。
5. **相关测试**：选择 `Assets/Editor/*SelfTest.cs` 中与改动相关的 `Run` 方法，运行 `./tools/develop.ps1 -Mode Test -TestName GroundInputSelfTest`（替换为所需测试名）。脚本检查 Unity 正常退出及该测试专属通过标记；测试可能依赖真实谱面或图形设备，不能把跳过解释为通过。新增自测时要在脚本的 `testMarkers` 中登记其独有的完成标记。
6. **构建**：需要 APK 时运行 `./tools/develop.ps1 -Mode Build`。脚本验证 Unity 版本、进程结果、构建日志、新 APK 的修改时间和大小，输出 SHA-256。构建过程不得同时打开同一 Unity 工程。
7. **设备验证**：仅在明确要求安装时执行 ADB 安装。按改动范围验证启动、正常游玩、暂停恢复、设置持久化、联机同步和结算。小概率问题需重复进出并保留时间戳日志。
8. **交付**：说明改动、测试结果、APK 绝对路径及 SHA-256、尚未验证的设备/服务器行为。不要把未运行的自测、Unity 崩溃或旧 APK 报告为成功。

## 按风险选择测试

| 修改区域 | 最少检查 |
| --- | --- |
| 谱面解析、滚速、长条 | `OriginalPlaybackSelfTest.Run`、相关 `SlideHeadSelfTest.Run` / `WorldEndAirNodeSelfTest.Run` |
| 触控、flick、判定 | `GroundInputSelfTest.Run`、`PreparationAndLongHeadSelfTest.Run`；设备上复测不同刷新率 |
| AIR 与视觉层 | `AirLayerSelfTest.Run`、相关真实谱面的视觉检查 |
| UI / 设置 / MOD | `LayoutFeedbackSelfTest.Run`、`ModDialogSelfTest.Run`、`SkinSettingsSelfTest.Run` |
| 联机客户端或协议 | 客户端联机启动/取消/断线检查；服务端 `smoke-test.js`，双方版本一致 |
| 音频加载与预备 | 首帧/预备回归测试，真机反复进出歌曲并检查音频、谱面、输入同起点 |

表中测试是选择入口，不保证所有机器都具备所需资源。修改 shared 层时扩大覆盖范围；设备端验证仍不能由 Editor 自测替代。

## 发布与版本

仅在明确要求版本更新时同步修改 `AndroidBuildConfigurator` 的 `bundleVersion`、`bundleVersionCode` 和默认 APK 名，核对 `ProjectSettings.asset` 以及 UI 更新弹窗/历史日志。不要为了普通修复自行升级版本号。构建成功之后再考虑提交：一个可解释的变更对应一个提交；发布标签只在 APK 和必要设备验证完成后创建，不覆盖旧标签。服务端部署包应排除 `data/`、令牌和环境配置；部署前备份服务端状态，客户端和服务端协议一起测试。

## 故障记录

Unity 批处理有时在 `ReloadAssembly` 或 FMOD 原生线程崩溃。2026-09-13 的 `GroundInputSelfTest` 批处理两次在 `FMOD::OutputWASAPI::feederUpdate` 崩溃，第二次添加 `-noaudio` 仍然崩溃，均未运行到 C# 测试；日志保存在 `Build/test-GroundInputSelfTest-20260913-215959.log` 与 `Build/test-GroundInputSelfTest-20260913-220134.log`。该问题未解决前不能声称 Editor 自测或 APK 构建通过。即使外层 PowerShell 返回成功，也必须检查日志末尾是否有正常退出，以及 APK 是否为本次新生成。崩溃时保留日志路径并停止交付，不复用旧 APK。联机“首帧加载但音乐在后台播放”应记录 `roundId`、文件校验、解析、绑定、首帧、`roundStarting`、遮罩关闭和音频调度的顺序；不要仅凭一个 ready 回调推断画面已显示。
