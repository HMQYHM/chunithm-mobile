# CHUNITHM Mobile 项目移动交接书

生成日期：2026-10-02  
原工作区：`F:\zhongermate`  
当前游戏工程：`chunithm-mobile-unity`  
当前游戏版本：`0.3.3`，Android versionCode `302`  
Unity：`2022.3.62f3c1`  
服务端源码版本：`0.2.6`  
最近游戏 Git 提交：`7708c5f Align gameplay skins with Skin Studio`

## 一、迁移包内容

### 1. Unity 游戏

位置：`CHUNITHM-Mobile-Unity/`

这里是完整的 Unity 项目源码和 Git 历史，包含：

- `Assets/`：游戏逻辑、谱面解析、输入、判定、渲染、UI、皮肤运行时和 Editor 自测。
- `Packages/`、`ProjectSettings/`：Unity 包和平台设置。
- `Examples/Skins/`：BongoCat Live2D 示例和 `StudioComplete` 完整皮肤示例。
- `docs/`：开发流程、iOS 构建、可移植性和皮肤制作文档。
- `tools/SkinStudio/`：皮肤制作器源码和测试。
- `.git/`：原项目 Git 历史。

Unity 生成的 `Library/` 没有带走。第一次打开项目会重新导入资源，第一次导入可能需要较长时间；不要在导入未完成时启动批处理构建。

### 2. 联机服务端

位置：`CHUNITHM-Mobile-Multiplayer-Server/`

服务端是只使用 Node.js 内置模块的 Windows 房间服务器，主要文件如下：

- `server.js`：服务端主体、WebSocket 房间、设备 Game ID、文件传输和管理员 API。
- `start-server.js`、`start-server.bat`：启动入口。
- `setup-wss.ps1`、`setup-wss.bat`、`setup-wss-zh.bat`、`setup-wss-en.bat`：Caddy WSS 配置入口。
- `admin-v2.html`：新版管理后台。
- `qq-bot.js`：官方 QQ Bot 接入。
- `smoke-test.js`、`qq-bot-test.js`：协议和 Bot 测试。
- `README.md`：服务端功能、房间规则、空闲策略、加入码和 Bot 指令说明。
- `config.example.json`：迁移包新增的脱敏配置模板。

`data/` 没有放进迁移包。生产服务器上的 `data/security.json`、`data/server-state.json`、QQ Bot 配置和管理员状态需要单独安全备份，再复制到新设备；不要把它们上传 Git 或发送给其他人。

### 3. Skin Studio

源码在游戏工程内：`CHUNITHM-Mobile-Unity/tools/SkinStudio/`。

它是一个 Electron 项目，当前支持：

- 18 个游玩界面元素的显示、位置、尺寸、透明度、颜色和文字样式。
- 长按拖动、重叠元素优先选择、隐藏元素、20 次撤回/重做、保存和放弃修改保护。
- Combo/分数/判定文字与 0–9 数字图片。
- 音符下方 Combo、剩余 Combo、分数、评级目标、损失分数和距离最高分等模式。
- Combo、分数、评级、判定、AIR、触控四区、游玩开始/结束和显示指标规则。
- 场景背景、轨道、分道、引导线、边缘线和判定线配置。
- Live2D 模型位置、平板背景、触控中心点、按下状态、可见状态、参数范围和动作组。
- ZIP/文件夹/JSON 导入、路径白名单、PNG/MOC3 校验和原子保存。

制作器不会执行皮肤包内的代码。皮肤只能提供 JSON、PNG 和受支持的 Live2D 资源。

## 二、新设备准备

### Unity 环境

1. 安装 Unity Hub。
2. 安装精确版本 `2022.3.62f3c1`。
3. 安装 Android Build Support、Android SDK & NDK Tools 和 OpenJDK。
4. 如果要构建 iOS，在 macOS 安装相同 Unity 版本和 iOS Build Support；Windows 只负责修改 Unity 工程，不能生成最终 iOS 签名包。
5. 把 `CHUNITHM-Mobile-Unity/` 放到不含中文和空格的路径，例如 `D:\Projects\CHUNITHM-Mobile-Unity`。
6. 用 Unity Hub 打开项目，等待第一次导入和脚本编译完成。

Android SDK、NDK、JDK 的实际路径不要照搬旧电脑。打开 Unity 的 `Edit -> Preferences -> External Tools` 重新选择新设备上的路径。

### Node.js 环境

服务端需要 Node.js 18 或更高版本。Skin Studio 需要 Node.js 和 npm；进入 `CHUNITHM-Mobile-Unity/tools/SkinStudio` 后执行：

```powershell
npm install
npm test
```

`node_modules` 没有放入迁移包，这是有意的。不要用旧电脑的 `node_modules` 覆盖新设备的依赖。

## 三、恢复 Git 工作区

迁移包中的 `.git` 已保留历史。进入 Unity 工程后检查：

```powershell
git status --short
git log -5 --oneline
git branch --show-current
```

当前原工作区本来就有大量未提交的历史修改，迁移时没有擅自清理。它们属于原工作区状态，不要为了得到干净状态执行 `git reset --hard` 或 `git clean -fd`。如果需要从一个干净基线开始，应先复制一份工程，再由开发者明确决定如何处理这些修改。

最近一次已提交的皮肤运行时/示例包改动是 `7708c5f`。它包含制作器与游戏运行时对齐、完整皮肤示例、皮肤文档和 Live2D 导入测试。

原项目没有检测到可用的 Git remote 配置。迁移到新设备后，如需推送 GitHub，请先检查并设置：

```powershell
git remote -v
git remote add origin <你的仓库地址>
git push -u origin main
```

不要把服务端 `data`、密钥、证书、`.env`、生产日志或个人谱面数据推送到公开仓库。

## 四、游戏构建

在 Unity 工程根目录执行：

```powershell
.\tools\develop.ps1 -Mode Preflight
.\tools\develop.ps1 -Mode Test -TestName SkinStudioRulesSelfTest
.\tools\develop.ps1 -Mode Test -TestName GameplaySkinDigitsSelfTest
.\tools\develop.ps1 -Mode Test -TestName GameplaySkinCustomDisplaySelfTest
.\tools\develop.ps1 -Mode Test -TestName Live2DSkinSelfTest
.\tools\develop.ps1 -Mode Test -TestName SkinSettingsSelfTest
```

确认 Unity 已关闭后再构建 Android：

```powershell
.\tools\develop.ps1 -Mode Build
```

输出默认位于 `Build/Android/CHUNITHM-Mobile-0.3.3-unity-arm64.apk`。构建日志也在 `Build/`，这些文件不会提交 Git。构建脚本会检查 Unity 退出状态、日志和 APK 大小；不能只看 PowerShell 返回值判断成功。

本迁移包的 `Artifacts/CHUNITHM-Mobile-0.3.3.APK` 是旧设备上已有的参考构建，不代表新设备已经重新构建。

## 五、iOS 构建

`Artifacts/CHUNITHM-Mobile-0.3.3-222-iOS-Xcode-Live2D-20260930.zip` 是当前已有的 Xcode 工程包，可直接解压到 Mac 继续签名和 Archive。

如果要从 Unity 重新导出：

1. 在 Mac 安装 Unity 2022.3.62f3c1 和 iOS Build Support。
2. 打开 Unity 工程，切换到 iOS 平台。
3. 执行项目内的 iOS 构建配置入口，导出 Xcode 工程。
4. 在 Xcode 中打开 `Unity-iPhone.xcodeproj`，检查 Team、Bundle Identifier、签名方式、Provisioning Profile 和 Build Number。
5. 真机运行前确认 Apple Developer 的 App ID 与工程 Bundle ID 一致。
6. Archive 后在 Organizer 中分发或上传 App Store Connect。

Xcode 的签名证书、描述文件和 Apple Team 不在迁移包中，需要在新 Mac 单独登录 Apple Developer。不要把 `.p12`、Provisioning Profile 或 Apple 私钥放入项目压缩包。

## 六、服务端恢复与运行

### 首次配置

1. 把 `CHUNITHM-Mobile-Multiplayer-Server/` 放到新服务器路径，例如 `D:\CHUNITHM\server`。
2. 复制 `config.example.json` 为 `config.json`。
3. 设置一个长随机 `authToken`；如果开放公网，必须同时配置 TLS 或使用 Caddy WSS。
4. 设置独立的 `adminToken`，不要使用游戏登录令牌。
5. 需要官方 QQ Bot 时填写 `qqBot.appId`、`qqBot.appSecret`、管理员 OpenID 和来源白名单。
6. 需要 Caddy 时填写域名，确保域名 DNS 已指向服务器，并让 80/443 端口可访问。
7. 双击 `start-server.bat`，或执行 `node start-server.js`。

最初建议只在本机验证：`adminLocalOnly: true`，通过 `http://127.0.0.1:27960/admin-v2` 打开后台。确认本地健康检查、创建房间、加入房间和歌曲传输正常后，再配置公网 WSS。

### 重要端口和地址

- 本地服务：`127.0.0.1:27960`
- WebSocket：`ws://服务器地址:27960/ws`
- WSS：`wss://你的域名/ws`
- 管理后台：`http://127.0.0.1:27960/admin-v2`
- 健康检查：`/health`，管理员权限保护

客户端和服务端版本必须匹配。房间会校验 `clientVersion`；服务端、Android APK 和 iOS 工程升级后应一起测试。

### 数据恢复

如果要迁移旧服务端状态，先停止服务端，再备份新设备自动生成的 `data`，然后由管理员确认后复制旧 `data/security.json` 和 `data/server-state.json`。复制前必须确认版本兼容，保留新旧备份；不要把数据文件放入公开压缩包。

## 七、服务端安全检查

- `authToken` 不能留空。
- `adminToken` 不能与 `authToken` 相同。
- 公网部署不要使用 HTTP/WS 明文。
- Caddy 证书申请需要正确 DNS 和 80/443 入站端口。
- 不要把 QQ AppSecret、管理员 OpenID 列表、加入码明文和 `data` 目录提交 Git。
- 管理后台默认只允许本机访问；确需远程管理时，优先使用 VPN/SSH 隧道。
- 服务器启动失败先看同目录 `server-startup.log` 和 PowerShell 窗口，不要反复启动多个 Node 进程。

## 八、皮肤制作与导入

详细格式见 Unity 工程中的 `docs/SKIN_AUTHORING.md`。

可直接测试的皮肤包：

- `Artifacts/CHUNITHM-Mobile-Skin-Studio-Complete-Reference-20261002.zip`
- `CHUNITHM-Mobile-Unity/Examples/Skins/StudioComplete/`
- `CHUNITHM-Mobile-Unity/Examples/Skins/BongoCatLive2DTouch/`

游戏导入路径：`数据管理 -> 游玩皮肤 -> 导入`。导入后在皮肤列表激活。导入 ZIP 不会执行脚本，必须包含根目录 `skin.json` 和 `ui/gameplay-ui.json`。

## 九、当前已验证和未验证事项

已验证：

- Unity 工程结构预检。
- Skin Studio 规则解析、数字皮肤、特效配置和皮肤设置 Editor 自测。
- 完整示例 ZIP 的安装、Live2D 原生模型加载、透明像素渲染、左右触控、按下/抬手和暂停姿态。
- 皮肤运行时的安全路径、PNG 限制和规则白名单。

新设备仍需验证：

- 新设备上的 Unity 首次导入和 Android SDK/NDK 路径。
- 真机的 Android 音频、触控、AIR 传感器和性能。
- Mac 上的 iOS 签名、Archive、真机导入文件权限和 Live2D 显示。
- 公网服务器的 DNS、Caddy 证书、WSS、QQ Bot 和跨设备建房。
- 迁移旧服务端 `data` 后的管理员、群组和加入码数据。

遇到问题时记录：操作时间、设备系统、游戏/服务端版本、完整日志、是否使用 WSS、是否新建房间，以及首次失败前最后一条状态。不要在日志或截图中公开令牌、AppSecret、加入码或完整设备标识。

## 十、恢复完成后的建议顺序

1. 先打开 Unity 工程并等待导入完成。
2. 运行 Preflight 和皮肤相关 Editor 测试。
3. 安装 Node.js 依赖并运行 Skin Studio 测试。
4. 用示例皮肤包做一次导入、激活和 Live2D 检查。
5. 新建服务端 `config.json`，本机启动并完成 smoke test。
6. 构建新的 Android Debug/Release 包，在实体设备测试。
7. 在 Mac 导出或打开 Xcode 工程，重新设置签名后构建 iOS。
8. 最后恢复服务端生产数据和公网配置，并做一次联机回归。
