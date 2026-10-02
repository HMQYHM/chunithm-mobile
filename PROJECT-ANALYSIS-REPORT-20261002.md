# CHUNITHM Mobile 项目熟悉报告

生成日期：2026-10-02  
分析范围：Unity 游戏、多人服务端、Skin Studio 皮肤编辑器、Live2D 示例、Git 历史和当前构建产物。

## 1. 项目结论

这是一个由三个相互配合的部分组成的项目：

1. `CHUNITHM-Mobile-Unity/`：Unity 2022.3.62f3c1 游戏工程，当前版本 0.3.2。
2. `CHUNITHM-Mobile-Multiplayer-Server/`：Node.js 内置模块实现的多人房间服务端，当前版本 0.2.6。
3. `CHUNITHM-Mobile-Unity/tools/SkinStudio/`：浏览器版和 Electron 桌面版共用的皮肤编辑器，当前 package 版本 0.1.0。

游戏、服务端和皮肤编辑器之间的主要契约是 JSON、PNG、Live2D 模型资源、歌曲 ZIP 和 WebSocket/HTTP 协议。皮肤包不执行脚本，也不能修改判定、分数或网络权限。

当前最新 Unity 提交是 `7708c5f Align gameplay skins with Skin Studio`。工作区保留了原电脑上的大量未提交修改；这是迁移状态的一部分，不能直接当作本次迁移产生的修改。

## 2. 目录结构

```text
cnmb/
├─ CHUNITHM-Mobile-Unity/
│  ├─ Assets/
│  │  ├─ Scripts/Chart       C2S 谱面解析和数据模型
│  │  ├─ Scripts/Timing      DSP 时间轴、变速和倒播
│  │  ├─ Scripts/Input       地面触控和 AIR 输入
│  │  ├─ Scripts/Judgement   判定点调度和判定窗口
│  │  ├─ Scripts/Rendering   谱面、轨道、Mesh、皮肤 UV
│  │  ├─ Scripts/Runtime     UI、歌曲、联机、皮肤、音频、平台层
│  │  ├─ Editor/              构建入口和 Editor 自测
│  │  ├─ Plugins/Android      Android 原生导入/日志/音频组件
│  │  ├─ Plugins/iOS          iOS 文件选择器、WebSocket、SceneDelegate
│  │  └─ Live2D/              Cubism 运行时资源和插件
│  ├─ Examples/Skins/         StudioComplete、BongoCat Live2D 示例
│  ├─ tools/SkinStudio/       皮肤制作器源码和测试
│  ├─ docs/                   开发、构建、皮肤、迁移和联机说明
│  ├─ Packages/               Unity 包清单
│  ├─ ProjectSettings/        Unity 平台和版本设置
│  └─ Build/                  构建日志和 APK（生成物）
├─ CHUNITHM-Mobile-Multiplayer-Server/
│  ├─ server.js               WebSocket/HTTP 服务端主体
│  ├─ admin-v2.html            管理后台
│  ├─ qq-bot.js                QQ Bot 接入
│  ├─ smoke-test.js            协议烟雾测试
│  └─ config.example.json      脱敏配置模板
└─ CHUNITHM-Mobile-Git-Backup-20261002/
   ├─ CHUNITHM-Mobile-All-History.bundle
   ├─ CHUNITHM-Mobile-WorkingTree.patch
   └─ untracked/                原工作区未跟踪文件
```

`Library/`、`Logs/`、本地 `Build/` 等是 Unity 生成物，不属于源码核心；首次打开工程时会重新生成或更新。

## 3. Unity 游戏架构

### 启动和页面

`AppBootstrap` 在首场景前创建跨场景运行时根节点，装配低延迟音频、加载遮罩、试听播放器、输入系统、摄像机、共享谱面场景和 `AppUiShell`。`AppUiShell` 是页面编排层，负责首页、歌曲列表、设置、数据管理、联机、游玩和结算页面。

### 歌曲和谱面

歌曲目录位于平台存储目录下。`SongCatalog` 扫描歌曲目录并通过文件签名缓存索引；`SongImportManager` 负责歌曲 ZIP 的路径检查、大小/内存/磁盘预算、解压和索引更新；`SongExportManager` 负责导出。

谱面主链路为：

```text
C2S 文件
  → C2sParser
  → ParsedChart / ChartModels
  → OriginalPlaybackMap + ScrollPositionMapper
  → DspChartClock
  → ChartPlaybackRenderer
  → GameplayJudgeController + JudgePointScheduler + JudgementEngine
```

解析阶段保留原始 Tick、BPM、SFL/SLP/SLA/STP、音符类型、宽度、AIR 方向和长条结构。判定时间不由渲染帧决定，而由 DSP 锚定的 `DspChartClock` 决定，因此暂停、变速和视觉倒播不会直接破坏判定时间的单调性。

### 输入、判定和渲染

- `ArcadeTouchInput` 把触控映射到 16 个地面 lane，并保留按下、移动、抬起、覆盖范围、移动距离和 flick 信息。
- `AirSensorInput` 管理 6 个 AIR 区域及进入/离开、上升/下降和运动序号。
- `ArcadeInputRig` 合并 Ground/AIR 输入，转发暂停、重置和生命周期。
- `JudgePointScheduler` 把音符展开为稳定的判定点；长条保持点、AIR sustain 和 Slide checkpoint 与头部判定分开。
- `JudgementEngine` 是纯判定计算层；成绩、Combo、统计和结算由运行时状态管理。
- `ChartPlaybackRenderer` 使用对象池、预计算几何和独立 Mesh 生命周期绘制地面、AIR、长条、引导线和装饰层，不参与判定。

### 音频和播放会话

`LocalChartPlaybackSession` 管理歌曲加载、MP3 播放、谱面绑定、预备、播放、暂停、恢复和资源释放；界面 BGM 使用 `InterfaceAudioController`，歌曲试听使用 `SongSelectionPreviewPlayer`。正式游玩使用 `audio/gameplay.mp3`，预览可使用导入生成的 `audio/preview.mp3`。

当前项目仍有迁移阶段边界：歌曲库、完整结果页、引导音生成和材料/特效匹配仍属于后续完善方向，不能把现有 0.3.2 视为功能全部完成的最终版。

## 4. 多人服务端

服务端只使用 Node.js 内置模块，不需要 npm 依赖。入口是 `server.js`，默认监听 `27960`，WebSocket 为 `/ws`，管理和健康接口为管理员专用 HTTP 接口。

### 连接流程

客户端先发送 `hello`，服务端校验：协议版本、`authToken`、稳定 `deviceId`、玩家名和 `clientVersion`。服务端通过 HMAC 生成并持久化 `gameId`，返回 `sessionToken`、能力列表、配额和房间列表。一个 game ID 只能有一个活动连接，也不能同时进入多个房间。

### 房间和回合

主要消息包括：

- 大厅：`listRooms`、`reserveRoomId`、`createRoom`、`joinRoom`、`leaveRoom`、`leaveLobby`。
- 房间：`setReady`、`updateRoomSettings`、`selectSong`、`kickPlayer`、`transferHost`。
- 同步：`setSongSyncStatus`、`setMemberBest`、`startRound`、`setRoundLoaded`、`roundLoadFailed`。
- 结果：`submitResult`、`confirmPersonalResult`、`confirmGroupResult`、`exitRound`。
- 状态：`appState`、`ping/pong`、`transferProgress`。

房间状态覆盖 lobby、loading、countdown、playing、personalResults、groupResults 等阶段。房主控制歌曲和主要设置，回合结束后可以转移 Host。服务端要求同一房间的客户端版本一致，普通房间只允许服务端认可的计分 MOD；完整 AUTO、AIR AUTO、免地雷和改速等配置会被拒绝。

### 歌曲传输

房主通过带 Bearer `sessionToken` 的 HTTP POST 上传歌曲 ZIP。服务端检查房主权限、大小、SHA-256 和当前歌曲选择，写入临时文件后原子改名；成员通过 GET/HEAD 下载，并支持 HTTP Range 断点续传。Unity 客户端下载后再次校验哈希，再交给 `SongImportManager` 导入。

### 配额、身份和管理

设备被分配到管理员维护的组；默认每天允许创建 5 个房间、加入 20 个房间，失败的验证不会消耗配额。后台可查看设备、昵称历史、房间、封禁状态、组和一次性群组加入码。加入码只保存 SHA-256，不保存明文。

服务端具备连接数、单 IP 连接数、消息速率、空闲、后台、房间保留和传输清理限制。生产部署必须保留 `config.json`、`data/`、证书和日志，但这些内容不应放入公开仓库或迁移包。默认 HTTP/WS 只适合本机或可信局域网；公网必须配置 TLS/WSS 和长随机 `authToken`。

QQ Bot 是可选管理/查询入口，不是游戏核心依赖。其 AppSecret、管理员 OpenID 和群组数据属于敏感配置。

## 5. Skin Studio 皮肤编辑器

Skin Studio 同一套前端同时支持浏览器版和 Electron 桌面版。桌面壳使用 `contextIsolation`、`sandbox` 和受限 preload 接口，只允许主页面通过安全保存接口写出文件。

### 编辑能力

- 编辑 18 个游玩界面元素的位置、尺寸、透明度、颜色、文字和图片。
- 编辑 Combo/分数数字 PNG、音符下方指标模式、背景、轨道、分道线和判定线。
- 通过“规则积木”配置 Combo、分数、评级、判定、AIR、触控和游玩开始/结束事件。
- 规则支持图片淡入淡出、缩放、旋转，或请求 Live2D 动作组。
- 支持完整皮肤文件夹、JSON 和 ZIP 导入；支持撤回/重做最近 20 步；保存使用临时文件再替换目标。
- Live2D 面板支持模型文件夹、参数映射、参数范围、动作组、隐藏部件和模型坐标背景。

### 皮肤包格式

```text
skin.json
ui/gameplay-ui.json
ui/*.png
ui/numbers/.../0.png 到 9.png
model/*.model3.json / *.moc3 / *.png / *.motion3.json / *.cdi3.json
许可证文件
```

`skin.json` 使用 `formatVersion: 1`、唯一 `id` 和 `gameplaySkin: true`。布局使用 0～1 的左下角归一化坐标；Live2D 模型外框使用屏幕归一化坐标，模型背景使用模型坐标。

规则最多 48 条，单次最多显示 24 个规则对象；只接受 `image` 和 `live2dMotion` 两种动作。所有路径必须位于皮肤根目录，外部图片必须是 PNG。Live2D 资源会校验 `.model3.json` 引用的 MOC3、贴图、动作、表达式和可选 CDI 文件；模型最多 16 张贴图、最多 2048 个 Drawable，并限制动作曲线和资源尺寸。

### 游戏侧加载

`GameplaySkinManager` 是游戏端的安全边界，解析布局、数字、场景、customDisplays 和 Live2D 配置；`GameplaySkinDigitRenderer` 绘制数字；`GameplaySkinCustomDisplay` 响应只读游玩事件；`GameplaySkinCubismRenderer`、`GameplaySkinModelSurface` 和 `GameplaySkinLive2DBridge` 负责模型、参数、动作和资源生命周期。皮肤表现不能参与判定，也不能读取外部目录、执行代码或改写按钮行为。

浏览器编辑器不会运行 Cubism 模型本身，只预览布局和规则；真实模型动作、裁切、暂停和触控必须在 Unity Editor 自测或设备上验证。

## 6. Git 和迁移状态

- 当前工作区：`CHUNITHM-Mobile-Unity`，分支 `main`。
- 当前 HEAD：`7708c5f Align gameplay skins with Skin Studio`。
- 当前仓库没有 remote 配置。
- 工作区约有 343 条状态记录，包含大量历史未提交的脚本、PNG、Mesh 删除/修改以及新的 Live2D、编辑器、文档和插件文件。
- Unity 交接包自带 `.git`，应作为当前工作区历史使用。
- 另存的 `CHUNITHM-Mobile-All-History.bundle` 记录的是备份时的另一历史端点 `2ed8055`，并附带 working-tree patch 与 untracked 文件；不要用它覆盖当前 `.git`。它适合作为额外历史恢复来源和离线备份。

安全操作顺序是先复制工程或建立分支，再逐步处理已有修改；不要使用 `git reset --hard` 或 `git clean -fd`。

## 7. 已确认的构建产物

### Android

- Unity：2022.3.62f3c1。
- 包名：`com.zhongermate.chunithmmobile`。
- Android versionCode：302。
- 最低 API：24；目标 API：34；IL2CPP；ARM64；Vulkan 优先、GLES3 回退。
- APK：[Build/Android/CHUNITHM-Mobile-0.3.2-unity-arm64.apk](CHUNITHM-Mobile-Unity/Build/Android/CHUNITHM-Mobile-0.3.2-unity-arm64.apk)。
- SHA-256：`cf4e1453205e33e710d7bfe61893e87870857c4329ce20ccded68e0401b0e90a`。

构建日志确认 Unity 正常退出并生成了本次 APK。没有进行实体 Android 设备安装和音频/AIR/触控回归。

### iOS

- Bundle ID：`com.hmqyhm.chunithmmobile`。
- Build：222；最低 iOS：15.0；ARM64；Metal；IL2CPP。
- Xcode Archive：[Build/XcodeArchive-20261002/CHUNITHM-Mobile.xcarchive](Build/XcodeArchive-20261002/CHUNITHM-Mobile.xcarchive)。
- IPA：[Build/CHUNITHMMobile-0.3.2-222-unsigned-20261002.ipa](Build/CHUNITHMMobile-0.3.2-222-unsigned-20261002.ipa)。
- IPA SHA-256：`f8b71dde0c099ed73ce829fba56895bb8c92fbc0ba89c82786bec94b641cf95d`。

Xcode Archive 已成功，但本机没有 Apple 签名身份和 provisioning profile；IPA 未签名，不能直接安装真机或上传 App Store Connect。现有 Archive 可在配置 Team、证书和描述文件后重新导出正式 IPA。

## 8. 当前风险和未完成事项

1. **工作区脏状态很大**：已有修改来自旧电脑，后续开发必须先建立清晰分支或快照。
2. **Git 备份端点不一致**：当前项目 HEAD 是 `7708c5f`，独立 bundle 的 HEAD 是 `2ed8055`，不能互相覆盖。
3. **没有远程仓库**：当前 Git 只在本地保存，若要协作需要用户确认新的 remote。
4. **iOS 签名缺失**：需要 Apple Developer Team 的证书和对应 profile。
5. **服务端生产状态不在迁移包**：`config.json`、`data/`、证书、QQ Bot 密钥和日志需要单独、安全迁移。
6. **本机开发工具不完整**：当前命令行没有 Node.js、npm、pwsh 和系统级 ADB；Unity 自带 Android 工具已经足够完成本次 APK 构建，但 Skin Studio 测试和服务端 smoke test 需要 Node.js 18+。
7. **设备验证尚未完成**：Android 真机音频、触控、AIR、性能，iOS 真机签名/导入/Live2D，多人跨设备联机和 WSS 均需实际设备或服务器验证。
8. **Unity 首次导入生成了约 5.9 GB Library**：这是本机缓存，不应提交 Git 或作为源码备份的一部分。

## 9. 推荐的后续开发顺序

1. 安装 Node.js 18+，运行 Skin Studio 的 `npm test`，再运行服务端 `smoke-test.js`。
2. 为当前 Unity 工作区建立开发分支并保存状态清单，不清理旧修改。
3. 用现有示例皮肤完成 Studio 导入、修改、导出、游戏导入和 Live2D 真机验证。
4. Android 真机验证歌曲导入、谱面播放、暂停/恢复、AIR、触控、皮肤和联机。
5. 配置 Apple Team、证书和 profile 后重新导出签名 IPA，并验证文件选择器、WSS 和 Live2D。
6. 服务端只在本机完成配置和 smoke test 后再恢复生产数据、TLS 和 QQ Bot。

## 10. 主要入口文件

- Unity 启动：`Assets/Scripts/Runtime/AppBootstrap.cs`
- Unity 页面：`Assets/Scripts/Runtime/AppUiShell*.cs`
- 谱面解析：`Assets/Scripts/Chart/C2sParser.cs`
- 时间轴：`Assets/Scripts/Timing/DspChartClock.cs`
- 判定：`Assets/Scripts/Judgement/JudgementEngine.cs`
- 渲染：`Assets/Scripts/Rendering/ChartPlaybackRenderer.cs`
- 歌曲导入：`Assets/Scripts/Runtime/SongImportManager.cs`
- 联机客户端：`Assets/Scripts/Runtime/MultiplayerClient.cs`
- 联机传输：`Assets/Scripts/Runtime/MultiplayerSongTransfer.cs`
- 游戏皮肤：`Assets/Scripts/Runtime/GameplaySkinManager.cs`
- Live2D：`Assets/Scripts/Runtime/GameplaySkinCubismRenderer.cs`
- Android 构建：`Assets/Editor/AndroidBuildConfigurator.cs`
- iOS 构建：`Assets/Editor/AppleBuildConfigurator.cs`
- 服务端：`CHUNITHM-Mobile-Multiplayer-Server/server.js`
- 皮肤编辑器：`CHUNITHM-Mobile-Unity/tools/SkinStudio/app.js`
- 规则校验：`CHUNITHM-Mobile-Unity/tools/SkinStudio/rule-config.js`
- Live2D 校验：`CHUNITHM-Mobile-Unity/tools/SkinStudio/live2d-config.js`
