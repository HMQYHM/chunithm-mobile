# iOS 导入、联机与界面测试手册

本文档对应 CHUNITHM Mobile `0.3.2` 的 iOS/iPadOS 测试工程，重点验证长条显示、原生 WSS 联机、延迟设置、剩余容量提示和 Safe Area 适配。

## 1. 本包新增内容

- 重做 iOS 系统文件选择：支持本地文件、iCloud 和第三方文件提供器。
- 选择文件后在后台流式复制，显示复制进度和预计剩余时间。
- 等待 iCloud 文件下载完成，拒绝超时、半截复制和字节数不一致的文件。
- iOS build number 更新为 `221`；Xcode 导出增加 Scene 生命周期适配和 `UniformTypeIdentifiers.framework` 链接。
- DDS 封面支持 BC1/DXT1、BC2/DXT3、BC3/DXT5 与 DX10 BC 格式；后台解码后再于主线程创建 Unity 纹理。
- 导入 ZIP 前检查可用存储和内存；空间不足时显示“至少还需释放 X.XX GB”并取消导入。
- iOS 公网联机改用系统 `NSURLSessionWebSocketTask`，通过系统 TLS 连接 WSS。
- 联机失败记录原生 `NSError` 域、错误码和说明，三次失败弹出最后一次具体错误。
- 普通 UI、导入弹窗、MOD 和联机界面适配 iPhone/iPad Safe Area。
- 游玩轨道、触控板与可视化编辑器保持全屏坐标，不因 Safe Area 改变判定位置。
- 日志诊断新增可用内存和可用存储信息。

关键新增源码：

```text
Assets/Plugins/iOS/ChunithmDocumentPicker.mm
Assets/Plugins/iOS/ChunithmWebSocket.mm
Assets/Plugins/iOS/ChunithmSceneDelegate.mm
Assets/Scripts/Runtime/SafeAreaCanvasFitter.cs
Assets/Scripts/Runtime/DdsTextureDecoder.cs
```

## 2. 构建环境

- Unity：`2022.3.62f3c1`，并安装同版本 iOS Build Support。
- Xcode：使用当前 macOS 可安装的稳定版本。
- Bundle ID：`com.hmqyhm.chunithmmobile`。
- 最低系统：iOS/iPadOS 15.0。
- 测试设备：至少一台 iPhone（有刘海或灵动岛）和一台横屏 iPad。
- 联机地址：`hmqyhm.net`，客户端会规范化为 `wss://hmqyhm.net/ws`。

不要把 Apple 证书、Provisioning Profile、QQ Bot 密钥或服务端管理员密码放进 Unity 工程。

## 3. 在 Mac 上导出与安装

1. 解压源码包，不要从 Windows 复制 `Library`、`Temp` 或 `Build`。
2. 用 Unity Hub 的 `2022.3.62f3c1` 打开工程，等待首次资源导入完成。
3. 执行菜单 `CHUNITHM Mobile > Apply iOS Settings`。
4. 执行菜单 `CHUNITHM Mobile > Build iOS Xcode Project`。
5. 在 Xcode 打开 `Build/iOS/CHUNITHM-Mobile-iOS/Unity-iPhone.xcodeproj`。
6. 选择 `Unity-iPhone` Target，在 Signing & Capabilities 中确认 Team 与 Bundle ID。
7. 连接真机并运行；需要分发时使用 Xcode 的 Product > Archive。

构建后确认 Xcode 工程包含：

- `ChunithmDocumentPicker.mm`
- `ChunithmWebSocket.mm`
- `UnityFramework` Target
- `NSLocalNetworkUsageDescription`
- iPhone/iPad 的横屏方向声明

## 4. 文件导入测试

每项都要观察选择文件后的读取界面、进度、预计剩余时间，并确认界面不会假死。

| 编号 | 场景 | 操作 | 预期结果 |
| --- | --- | --- | --- |
| I-01 | 本地歌曲 ZIP | 从“文件”中选择已经下载到本机的有效 ZIP | 后台复制显示进度与预计时间，随后解压并导入成功 |
| I-02 | iCloud ZIP | 选择带云朵图标、尚未下载到本机的 ZIP | 系统先下载文件，应用等待本地化后复制；不会导入半截文件 |
| I-03 | 文件仍在上传/下载 | 在其他设备上传大 ZIP 后立刻在 iPhone/iPad 选择 | 最多等待约 45 秒；可用后继续，否则给出明确失败信息 |
| I-04 | 重复导入 | 连续两次选择同一个 ZIP | 缓存文件被覆盖，不会把第二次内容追加到第一次后面 |
| I-05 | 取消选择 | 打开系统文件选择器后取消 | 回到原界面，不残留“正在导入”或皮肤待处理状态 |
| I-06 | 损坏 ZIP | 选择截断或内容损坏的 ZIP | 停止导入、显示错误，不留下可被识别为完整歌曲的残缺目录 |
| I-07 | 空间不足 | 在剩余容量较低的设备导入大 ZIP | 解压前取消，并显示预计需要、当前可用、至少还需释放多少 GB |
| I-08 | 内存不足 | 关闭其他应用后仍使用超大压缩包测试 | 预检失败时取消，不继续解压；提示分批导入较小压缩包 |
| I-09 | 皮肤 ZIP | 从皮肤设置选择有效和缺资源的皮肤包 | 有效包导入成功；缺失项明确列出并提示使用原版资源回退 |
| I-10 | 背景图片 | 一次选择 1、10、50 张 PNG/JPG | 显示逐张复制进度，保持比例处理；失败图片不影响其他图片 |
| I-11 | 中途锁屏/切后台 | 正在复制大文件时锁屏或短暂切换应用 | 回到应用后继续或明确失败，不应永久停留在读取界面 |
| I-12 | 超时与半截复制 | 使用网络文件提供器制造断网或暂停下载 | 不把暂时读不到数据误判为 EOF，目标文件字节数不符时拒绝导入 |
| I-13 | DDS 封面 | 分别导入 DXT1、DXT3、DXT5/DX10 DDS 封面的歌曲 | 列表封面方向、颜色和透明度正确，不出现黑块或空白 |
| I-14 | DDS 游玩背景 | 将带 DDS 封面的歌曲设为游玩背景并进入谱面 | 中央封面与模糊背景都能显示，切歌后新封面正确 |

空间不足提示示例应包含：

```text
预计需要 3.25 GB
当前可用 1.10 GB
至少还需要释放 2.15 GB
```

缺口按 0.01 GB 向上取整，因此提示值不能小于真实缺口。

## 5. 联机测试

本次 iOS 使用系统 `NSURLSessionWebSocketTask` 建立 WSS，Android 和 Unity Editor 继续使用原有托管实现。

| 编号 | 场景 | 操作 | 预期结果 |
| --- | --- | --- | --- |
| M-01 | 首次登录 | 输入 `hmqyhm.net` 和玩家名称后登录 | 自动使用 `wss://hmqyhm.net/ws`，进入房间大厅 |
| M-02 | 地址持久化 | 退出应用并重新打开联机登录页 | 上次服务器地址和玩家名称仍显示 |
| M-03 | 错误域名 | 输入不存在的域名 | 三次重试后退出联机模式；弹窗包含最后一次原生错误详情 |
| M-04 | 服务端离线 | 连接地址正确但停止服务端或代理 | 显示“网络错误，正在重试”，三次失败后回主页 |
| M-05 | 心跳 | 登录大厅后保持至少 3 分钟 | 连接保持，日志持续出现 ping/pong，不误判为空闲断开 |
| M-06 | 前后台切换 | 在大厅切到后台后 1 分钟内返回 | 自动恢复或重连；状态与房间列表刷新 |
| M-07 | 后台超时 | 在后台停留超过 10 分钟再返回 | 回到联机登录界面并提示等待超时 |
| M-08 | 创建/加入房间 | 创建普通、休闲、公开、隐藏及带密码房间 | 服务端额度、模式、人数、密码和可加入状态校验一致 |
| M-09 | 歌曲已存在 | 成员本地已有完全相同歌曲与谱面 | 显示同步成功，不重复下载 |
| M-10 | 歌曲缺失 | 成员没有房主选择的歌曲 | 房主上传、成员下载均显示进度；完成后才能准备 |
| M-11 | 传输中断 | 上传或下载中断网后恢复 | 不进入不可操作的游玩状态；重连后重新同步或明确失败 |
| M-12 | 全员开始 | 所有人同步完成并准备 | 正常进入 2 秒预备、音乐和谱面同步开始，不卡在加载首帧 |
| M-13 | 结算流程 | 全员完成或有人提前退出 | 单人结算、全员排行、10 秒确认和返回房间状态正确 |
| M-14 | 版本不一致 | 使用不同客户端版本加入 | 客户端和服务端都拒绝进入，并显示版本不一致 |

成功连接时日志应依次包含类似内容：

```text
connect-target ... transport=NSURLSessionWebSocketTask
transport-connected
network-send type=hello
login-accepted
heartbeat-ping
heartbeat-pong
```

连接失败时应包含 `NSError` 的 domain、code 和具体描述，不应只剩下：

```text
Unable to connect to the remote server
```

## 6. Safe Area 与横屏测试

1. 在有刘海或灵动岛的 iPhone 上分别使用左右两个横屏方向。
2. 检查主页、歌曲列表、设置、数据管理、MOD、联机大厅、房间和所有弹窗。
3. 普通按钮、标题、搜索栏和关闭按钮不得进入刘海、圆角或 Home Indicator 区域。
4. 在 iPad 上检查 4:3 横屏，文字不能裁切，列表和右侧详情不能重叠。
5. 旋转设备后等待界面稳定，再检查点击区域是否与按钮视觉位置一致。
6. 游玩轨道和可视化编辑器继续使用全屏坐标；Safe Area 不能缩窄轨道、触控板或改变判定位置。

## 7. 日志与问题反馈

在“更多设置/日志”导出日志。每个问题单独记录以下信息：

- 测试编号，例如 `I-03` 或 `M-10`。
- 设备型号、系统版本、剩余存储、网络类型。
- 文件来源（本地、iCloud Drive、第三方文件提供器）和文件大小。
- 服务器输入值、发生时间、重试次数。
- 从点击开始到出错的完整步骤。
- 截图或录屏。
- 导出的完整日志，不要只截最后一行。

诊断日志头部应显示：

```text
Available memory:
Available storage:
```

## 8. 发布前通过标准

- I-01、I-02、I-05、I-07、I-09、I-10 全部通过。
- M-01、M-02、M-03、M-05、M-09、M-10、M-12 全部通过。
- iPhone 和 iPad 的两个横屏方向均无重要控件遮挡。
- Xcode 控制台没有 Objective-C 异常、原生崩溃或持续重复错误。
- 至少完整游玩一首单人歌曲和一首联机歌曲，并完成结算返回。

Windows 侧的预检与 Unity Editor 自测只能验证 C# 编译和通用逻辑；`ChunithmDocumentPicker.mm`、`ChunithmWebSocket.mm`、iCloud 文件协调、Apple TLS 和真机 Safe Area 必须按本手册在 Mac/Xcode 与真实 iOS 设备上最终确认。
