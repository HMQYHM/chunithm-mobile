# CHUNITHM Mobile

独立 Unity 实现的 CHUNITHM 风格移动端项目，包含游戏客户端、多人联机核心服务端和完整开源 Skin Studio 皮肤编辑器。

- 当前版本：**0.3.3**
- Unity：2022.3.62f3c1
- Android：Android 7.0+，ARM64，Vulkan / OpenGL ES 3
- iOS：iOS 15+，需要自己的 Apple 签名

## 组件

- **Unity 客户端**：`CHUNITHM-Mobile-Unity/`，包含游戏、联机客户端、Live2D 运行时和示例皮肤。
- **多人联机核心**：`CHUNITHM-Mobile-Multiplayer-Server/`，只包含 WebSocket/HTTP 房间、同步、歌曲传输和烟测。
- **Skin Studio**：`CHUNITHM-Mobile-Unity/tools/SkinStudio/`，浏览器版与 Electron 桌面版共用的完整开源皮肤制作器，含规则积木、场景轨道、数字 PNG 和 Live2D 配置。

QQ Bot、机器人测试、机器人凭据、管理后台扩展和运行时 `data/` 不在公开仓库中。联机核心不依赖 Bot。

## 快速开始

### Skin Studio

直接打开 `CHUNITHM-Mobile-Unity/tools/SkinStudio/index.html` 使用浏览器版；桌面版在该目录运行 `npm install` 后使用 `npm start`。测试：`npm test`。

皮肤编辑器文档：[`tools/SkinStudio/README.md`](CHUNITHM-Mobile-Unity/tools/SkinStudio/README.md)；游戏皮肤格式和 Live2D 说明：[`docs/SKIN-STUDIO.md`](CHUNITHM-Mobile-Unity/docs/SKIN-STUDIO.md)。

### 联机核心服务端

```text
cd CHUNITHM-Mobile-Multiplayer-Server
copy config.example.json config.json
node start-server.js
node smoke-test.js
```

服务端默认监听 `27960`，需要 Node.js 18+。公网部署前必须配置长随机 `authToken` 和 TLS/WSS。

## 下载

请从 [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) 下载 APK 和 IPA。IPA 为未签名构建，需要使用自己的 Apple 开发者账号重新签名。

## 开源与第三方内容

原创代码按 MIT License 发布。Live2D Cubism、Twemoji、vgmstream、BongoCat 和其他第三方内容遵守各自许可证，详见 `THIRD-PARTY-LICENSES.md`。歌曲、谱面、角色、商标和音乐不随本项目授权。本项目是独立的同人/致敬实现，不是官方产品。

## 源码归档

- [完整 Skin Studio 源码 ZIP](CHUNITHM-Mobile-SkinStudio-0.1.0-source.zip)：浏览器版、Electron 桌面版、规则积木、场景轨道、PNG 数字和 Live2D 配置。
- [多人联机核心源码 ZIP](CHUNITHM-Mobile-Multiplayer-Core-0.2.6-source.zip)：WebSocket/HTTP 房间、同步、歌曲传输和烟测。

这两个压缩包均不包含 QQ Bot、Bot 测试、Bot 凭据、管理后台扩展或运行时数据。

## 多语言

- [简体中文](README.md)
- [繁體中文](README.zh-TW.md)
- [日本語](README.ja.md)
- [English](README.en.md)
