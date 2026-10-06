# CHUNITHM Mobile

[專案說明網站](https://hmqyhm.github.io/chunithm-mobile/) · [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases)

CHUNITHM Mobile 是獨立的 Unity 行動端專案。本儲存庫公開專案說明頁、多人連線核心原始碼封存，以及 Skin Studio 皮膚製作器原始碼封存。

## 目前公開內容

- **多人連線核心原始碼**：包含房間、同步、歌曲傳輸與協定煙測的 Node.js 服務端封存。
- **Skin Studio**：可編輯遊玩介面布局、資源、規則、場景與 Live2D 設定的瀏覽器版與 Electron 桌面版皮膚製作器。
- **專案說明網站**：支援簡體中文、繁體中文、日本語與 English，並提供 Android、iOS 與 HarmonyOS 平台說明。
- **發布入口**：實際的 APK 與 IPA 檔案請以 [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) 中的檔案為準。

## 原始碼封存

- [多人連線核心原始碼 ZIP](CHUNITHM-Mobile-Multiplayer-Core-0.2.6-source.zip)
- [Skin Studio 原始碼 ZIP](CHUNITHM-Mobile-SkinStudio-0.1.0-source.zip)

### 多人連線核心

解壓封存後，準備 Node.js 18 或更新版本並執行：

```text
node start-server.js
node smoke-test.js
```

預設連接埠與設定方式請查看封存內的 `README.md` 與 `config.example.json`。

### Skin Studio

解壓封存後：

- 瀏覽器版：直接開啟 `index.html`。
- 桌面版：在封存目錄執行 `npm install`，再執行 `npm start`。
- 測試：執行 `npm test`。

製作器可以匯入既有皮膚，調整遊玩介面的元素位置、尺寸、顏色與資源，並匯出遊戲可讀取的皮膚包。支援欄位和資源限制請以封存內文件為準。

## 社群與支持

- [Discord](https://discord.gg/uuVWNBBhXR)
- [QQ群](https://qm.qq.com/q/LOzdadEsy6)
- [Bilibili](https://space.bilibili.com/351963496)
- [支持專案營運](https://afdian.com/a/HMQYHM)

## 授權

原創程式碼採用 [MIT License](LICENSE)。第三方內容遵循各自授權，詳見 [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md)。歌曲、譜面、角色、商標與音樂不在本專案授權範圍內。

## 語言

- [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [English](README.en.md)
