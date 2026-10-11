# CHUNITHM Mobile Unity 代码导读

本文档是工程源码的维护入口。源码中的注释解释“为什么这样做”，本文档补充“整个系统怎样连起来”。所有说明都以当前 Unity 版本的实际代码为准；如果实现发生变化，应同时更新对应源码注释和本文件。

## 1. 运行时总流程

应用启动由 `AppBootstrap` 开始。它负责创建或查找全局服务、准备平台存储目录、读取持久化设置，并把控制权交给 `AppUiShell`。`AppUiShell` 是页面壳层：它创建首页、歌曲列表、设置、数据管理、联机、游玩和结算等页面，保存当前选择，并把用户操作分发给目录、导入、音频和联机服务。

单人游玩的大致顺序是：

1. `SongCatalog` 从索引文件读取歌曲和谱面元数据；没有有效索引时扫描歌曲目录并重建索引。
2. `SongSelectionScreen` 保存当前歌曲、难度、排序和试听状态；`SongSelectionPreviewPlayer` 只负责列表试听。
3. `LocalChartPlaybackSession.PlaySong` 解析 C2S、加载 `gameplay.mp3`、按需要合成正解音，创建 `DspChartClock` 时间轴，并准备渲染器和判定器。
4. `ChartPlaybackRenderer` 根据时间轴创建、更新和回收可见音符；它只负责画面，不决定成绩。
5. `ArcadeInputRig` 聚合地面触控和 AIR 传感器输入；`GameplayJudgeController` 将输入和 `JudgePointScheduler` 生成的判定点匹配。
6. `JudgementEngine` 只做纯判定计算，返回判定等级和时间差；分数、Combo、统计和结算由 `GameplayScoreState` 管理。
7. 播放结束后 `AppUiShell` 生成 `ResultViewModel`，写入 `SongRecordStore`，刷新歌曲列表中的成绩显示。

联机流程在上述播放流程之外增加了一层状态同步：`MultiplayerClient` 负责网络消息和重连，`MultiplayerSongTransfer` 负责歌曲/谱面包的校验、上传和下载，`AppUiShell.MultiplayerV2` 负责大厅、房间、准备、同步、游玩和多人结算页面。联机进入游玩时，文件同步、谱面解析、渲染器绑定、判定器绑定和输入绑定必须全部完成后才能启动统一时间轴。

## 2. 时间与判定原则

游戏逻辑时间以 `DspChartClock` 为准，而不是以 `Time.deltaTime` 或渲染帧数为准。音频 DSP 时钟是单调时间源；谱面时间通过锚点、播放速率和暂停状态换算。触控样本必须带有同一单调时钟的时间戳，判定器再把它转换到谱面时间。

因此：

- 60/90/120 Hz 屏幕不应改变同一次触摸的判定结果。
- `DspChartClock.PlayScheduled` 用于把歌曲起点、谱面起点和预备阶段对齐。
- 暂停时要冻结逻辑时间和输入捕获；恢复时重新建立 DSP 锚点。
- 播放速率改变时先保留当前谱面时间，再更换速率，不能直接修改原始时间戳。
- 长条的头、保持点和尾部显示可以独立存在；判定点由调度器决定，不能根据渲染对象是否存在推断成绩。

`JudgementEngine` 中的窗口以 60 Hz 参考帧换算成毫秒。`JudgePointScheduler` 把每种音符展开成稳定、可排序的判定点，并处理地面密集音符的 lane 保护区。保护区是“输入候选归属”的限制，不是额外的判定窗口；最终窗口仍由音符类型自己的判定函数决定。

## 3. 谱面数据层

`C2sParser` 将原始 C2S 文本转换为 `ParsedChart`。解析包含 BPM、滚速、音符类型、位置、宽度、Air 方向、长条区间和显式判定时间。解析阶段应保持原始时间和结构信息，渲染阶段再依据 `OriginalPlaybackMap` 和 `ScrollPositionMapper` 计算空间位置。

`ChartModels` 是数据契约：`ChartNote` 描述单个谱面元素，`ScrollEvent` 和 `ScrollAssignment` 描述滚速变化，`ParsedChart` 是一首谱面的完整内存表示。不要在 UI 层直接拼接或推测这些对象的语义。

`ChartMirror` 只改变谱面空间位置和方向相关数据，不能修改歌曲元数据、判定时刻或原始成绩记录。

## 4. 输入层

`ArcadeTouchInput` 把 Unity 的触控事件映射到 16 个地面分区，并保留 PointerId、按下/移动/抬起阶段、覆盖范围、移动距离和 flick 脉冲。触控范围用于生成覆盖 lane mask；判定器应使用“同一平面”的候选，不应因为触点半径把上下层无关音符同时判定。

`AirSensorInput` 读取传感器或模拟 AIR 样本，维护活动区域、进入/离开区域、平均高度、左右位置、上升/下降方向和运动序号。`ArcadeInputRig` 将 Ground 和 Air 合并成一帧输入，并负责暂停、重置和生命周期转发。

`TouchContactOverlay` 是调试/可视化反馈，不参与判定。`TrackMaskOverlay` 负责触控板显示、触控范围线和分界线等画面层；关闭显示不应关闭输入逻辑。

## 5. 渲染层

`ChartPlaybackRenderer` 是游玩谱面的核心渲染器。它根据谱面时间筛选可见音符，使用对象池降低 GC，使用预计算几何体降低每帧 Mesh 分配，并把地面、AIR、装饰线、拖尾和音符头分到不同渲染队列。AIR 拖尾与谱面装饰线必须使用独立材质和生命周期。

`OriginalNotePrecalculation` 是渲染器进入 Unity 对象层之前的稳定边界。它对应原版 Windows 客户端的 `NotesPreCalcManager`/`NotesManager` 分工：一次性确定音符的视觉类型、Head/Body/Tail 归属、长条链 owner、资源族和显示窗口。`ChartPlaybackRenderer` 只负责对象池、网格、材质和逐帧裁切，不应在渲染帧中重新推断谱面语义。该层不创建 GameObject、不推进时钟，也不参与判定。

渲染器的重要边界：

- `ChartPlaybackRenderer` 不执行判定。
- 判定完成后由判定控制器通知渲染器隐藏或回收对应视觉对象。
- 尚未到显示时间的音符不能出现在判定线附近。
- 已过自己的可见生命周期、且没有视觉尾部职责的对象必须回收。
- 长条中间或头尾插入的其他音符不能覆盖或删除长条本身的必要段。

`PlayfieldRenderer` 负责轨道和判定线，`ChartSpace` 负责谱面坐标到世界坐标的转换，`MeshFactory` 负责材质和 Mesh 的基础创建，`NoteSkinUvCache` 缓存外部皮肤在不同宽度 Mesh 上的 UV 适配。

## 6. 音频层

`LowLatencyAudioController` 管理 Unity 音频配置和 DSP buffer；它不应该调用 Android 硬件倍速 API。播放速率应保持在 Unity 软件 DSP 链路中完成，以便和谱面时钟一致。

`LocalChartPlaybackSession` 负责游玩歌曲的完整资源生命周期：加载、混音、预备、播放、暂停、恢复、释放和结束。每次加载都有 generation 标识；旧的异步回调不能修改新的播放会话。正解音是在谱面判定点上限流合成到歌曲 PCM 的，需同时考虑音乐延迟、正解音额外延迟和音量，且同一时刻最多合成规定数量的声音，避免高密度片段爆音。

`InterfaceAudioController` 只负责首页/列表/结算等界面背景音乐和渐弱；`SongSelectionPreviewPlayer` 只负责歌曲列表试听。游玩音乐不能被界面 BGM 控制器抢占。

## 7. 皮肤与背景

`GameplaySkinManager` 读取皮肤包清单、图片、材质、Mesh、判定效果和 UI 资源，负责缺失资源回退到原版资源。资源命名必须和皮肤包规范一致；宽度 1 到 16 的资源不能简单按图片外框缩放，UV/主体尺寸要通过 `NoteSkinUvCache` 适配。

`BackgroundImageStore` 管理导入背景图、720p 压缩、长宽比保持和持久化索引。`MeteorKeyBackground` 负责主页等非游玩界面的拖尾/emoji 背景；游玩页面应按设置关闭它，避免遮挡音符和消耗不必要的 GPU。

## 8. UI 与持久化

`AppUiModels` 定义页面、语言、MOD、设置、歌曲选择和结算数据模型。`AppUiShell` 及其 partial 文件是 UI 编排层：

- `AppUiShell.cs`：公共字段、页面构建、页面切换、歌曲选择和主流程。
- `AppUiShell.Mods.cs`：单人 MOD 列表、计分/不计分分组和按钮状态。
- `AppUiShell.MultiplayerV2.cs`：联机大厅、房间、同步、准备和多人结算。
- `AppUiShell.SkinSettings.cs`：背景皮肤和游玩皮肤设置。
- `AppUiShell.ManagementList.cs`：歌曲与皮肤数据管理列表。
- `AppUiShell.Credits.cs`：制作与感谢名单。
- `AppUiShell.PaletteTransition.cs`：高/低对比 UI 过渡动画。

UI 事件应只改变模型并调用统一刷新函数，不要在按钮回调中复制页面构建逻辑。设置和上次歌曲选择通过 `PlayerPrefs` 或工程已有持久化入口保存，启动时读取一次，页面创建时应用当前模型。

## 9. 导入与联机安全边界

Skin Studio 的 `rule-config.js` 定义积木式条件与导出验证，`app.js` 负责资源与编辑历史；
`GameplaySkinManager` 解析 `customDisplays`/`scene`，`GameplaySkinCustomDisplay` 解释只读游玩事件，
通过受限 `SkinMotionRequested` 请求当前模型动作。`GameplaySkinModelSurface` 校验并持有动作资源；
背景和轨道图片由管理器缓存，渲染器不得销毁共享 PNG。`AppUiShell.SkinLayout` 同步皮肤矩形和输入区域，
全屏触控板 MOD 先于皮肤配置，隐藏区域不关闭采样。相关测试为 `SkinStudioRulesSelfTest`。

Live2D 游玩皮肤的布局和参数映射由 `GameplaySkinCubismRenderer` 负责，`GameplaySkinModelSurface` 独占模型、贴图、相机及内置渲染管线的绘制命令。Cubism Core 计算变形与透明度，皮肤表现不参与判定。模型按照自身固定画布取景，避免猫爪移动导致整体位置跳动；`GameplaySkinNativePlugins` 在 iOS 导出前选择唯一匹配的原生库。资源和动作限制、包结构及测试入口见 `docs/SKIN-LIVE2D.md`。

`SongImportManager` 负责压缩包读取、路径安全检查、内存预算、导入进度、预计剩余时间、解压、校验和目录索引。导入时不能直接把整个大压缩包解压到内存；遇到空间或内存不足应停止并给出明确结果。

`MultiplayerClient` 的网络数据必须视为不可信输入。房间模式、MOD、人数、权限、版本和文件校验都必须由服务端最终判断，客户端只做体验层校验。`MultiplayerLog` 记录联机动作，但日志中不要泄露令牌或不必要的敏感信息。

## 10. 编辑器自测与构建

`Assets/Editor` 下的 `*SelfTest.cs` 是编辑器回归检查，不是运行时功能。它们覆盖输入、AIR、长条头尾、开头音符、地雷倒播、皮肤宽度、布局反馈、暂停和歌曲列表分组等历史问题。

`AndroidBuildConfigurator` 负责 Unity Android 构建参数和 APK 输出，`AndroidLauncherIconResources` 负责构建后的应用图标资源。修改运行时逻辑后应先执行编辑器自测和编译，再构建 APK；构建日志要保留，便于区分编译错误、资源错误和设备运行时错误。

## 11. 调试建议

遇到“音乐播放但界面卡住”时，按以下顺序读日志：文件准备完成、谱面解析完成、音频准备完成、渲染器绑定、判定器绑定、输入绑定、页面切换完成、首帧完成、统一时间轴启动。若音频启动早于页面和输入绑定，优先检查 `LocalChartPlaybackSession` 与联机启动协程；若只有联机复现，检查 `AppUiShell.MultiplayerV2` 和 `MultiplayerSongTransfer` 的重复回调。

遇到“音符显示不全”时，先区分解析缺失、几何构建缺失、材质/UV 缺失、渲染队列遮挡和生命周期提前回收。不要只改尺寸或透明度来掩盖数据层问题。

## 12. 后续代码注释规则

以后新增或修改代码时，必须同时留下能帮助下一位维护者判断上下文的注释：

1. 新文件顶部说明模块职责、生命周期、输入输出和明确不负责的边界。
2. 新的 public/internal 类、方法、属性和事件使用 XML 文档注释，说明参数、返回值、副作用、调用线程和失败条件。
3. 复杂条件、时间换算、缓存、对象池、异步/协程、网络协议和资源所有权必须在代码旁说明“为什么这样做”，不要只重复语句含义。
4. 修改判定、谱面显示或音频同步时，注释必须写明时间基准、单位、播放速率和暂停行为。
5. 修改联机流程时，注释必须写明消息来源、权限边界、房间/回合身份校验和旧回调如何失效。
6. 修改皮肤或导入流程时，注释必须写明资源命名、回退策略、内存所有权和失败后的清理动作。
7. 编辑器测试必须说明它覆盖的历史问题，以及哪些行为不在测试范围内。
8. 不给每一行写无意义的“赋值/调用”注释；只有当代码意图不能从语法直接看出时才添加说明。

提交前至少运行 `git diff --check`。如果改动了运行时逻辑，还应运行 Unity 编译或构建，并在无法完成时记录具体日志阶段和原因。
