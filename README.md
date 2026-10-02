# CHUNITHM Mobile

独立 Unity 实现的 CHUNITHM 风格移动端项目，包含游戏客户端、多人服务端和 Skin Studio。

- 当前版本：**0.3.3**
- Unity：2022.3.62f3c1
- Android：Android 7.0+，ARM64，Vulkan / OpenGL ES 3
- iOS：iOS 15+，需使用自己的 Apple 签名
- 服务端：Node.js 内置模块 + WebSocket，无 npm 运行时依赖
- 皮肤编辑器：浏览器版和 Electron 桌面版

## 下载

请从 [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) 下载 APK 和 IPA。

`CHUNITHMMobile-0.3.3-223-unsigned.ipa` 是未签名 IPA，不能直接安装；需要在 Xcode 中使用自己的开发者账号和 provisioning profile 重新签名。

## 项目目录

- `CHUNITHM-Mobile-Unity/`：Unity 客户端、Live2D 集成、运行时和 Skin Studio
- `CHUNITHM-Mobile-Multiplayer-Server/`：HTTP/WebSocket 多人服务端、管理后台和 QQ Bot
- `PROJECT-ANALYSIS-REPORT-20261002.md`：项目结构、服务端、皮肤编辑器和构建说明

## 开源与第三方内容

本仓库中的原创源代码按 MIT License 发布。Live2D Cubism、Twemoji、vgmstream、BongoCat 和其他第三方内容继续遵守各自许可证；详见 `THIRD-PARTY-LICENSES.md` 和对应目录中的声明。歌曲、谱面、角色、商标和音乐不随本项目授权。

本项目是独立的同人/致敬实现，不是官方产品。

## 多语言

- [简体中文](README.md)
- [繁體中文](README.zh-TW.md)
- [日本語](README.ja.md)
- [English](README.en.md)
