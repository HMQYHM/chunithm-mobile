# CHUNITHM Mobile

[项目说明网站](https://hmqyhm.github.io/chunithm-mobile/) · [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases)

CHUNITHM Mobile 是一个独立的 Unity 移动端项目，仓库提供项目说明页、联机核心源码归档和 Skin Studio 皮肤制作器源码归档。

## 当前公开内容

- **联机核心源码**：Node.js 服务端归档，包含房间、同步、歌曲传输和协议烟测。
- **Skin Studio**：浏览器版与 Electron 桌面版皮肤制作器，支持游玩界面布局、资源、规则、场景和 Live2D 配置编辑。
- **项目说明网站**：提供简体中文、繁体中文、日本語和 English 四种语言，并包含 Android、iOS 与 HarmonyOS 的平台说明。
- **发布页入口**：APK 和 IPA 构建以 [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) 中的实际文件为准。

## 源码归档

- [联机核心源码 ZIP](CHUNITHM-Mobile-Multiplayer-Core-0.2.6-source.zip)
- [Skin Studio 源码 ZIP](CHUNITHM-Mobile-SkinStudio-0.1.0-source.zip)

### 联机核心

解压源码归档后，需要 Node.js 18 或更高版本：

```text
node start-server.js
node smoke-test.js
```

默认端口和配置方式请查看归档内的 `README.md` 与 `config.example.json`。

### Skin Studio

解压源码归档后：

- 浏览器版：直接打开 `index.html`。
- 桌面版：在归档目录运行 `npm install`，再运行 `npm start`。
- 测试：运行 `npm test`。

制作器可以导入已有皮肤，调整游玩界面的元素位置、尺寸、颜色和资源，并导出游戏可读取的皮肤包。具体字段和资源限制以归档内的文档为准。

## 社区与支持

- [Discord](https://discord.gg/uuVWNBBhXR)
- [QQ群](https://qm.qq.com/q/LOzdadEsy6)
- [Bilibili](https://space.bilibili.com/351963496)
- [支持项目运营](https://afdian.com/a/HMQYHM)

## 许可证

原创代码使用 [MIT License](LICENSE)。第三方内容遵循各自许可证，详见 [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md)。歌曲、谱面、角色、商标和音乐不随本项目授权。

## 多语言

- [简体中文](README.md)
- [繁體中文](README.zh-TW.md)
- [日本語](README.ja.md)
- [English](README.en.md)
