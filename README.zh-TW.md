# CHUNITHM Mobile

獨立 Unity 風格行動專案，包含遊戲客戶端、多人連線核心伺服器，以及完整開源的 Skin Studio 皮膚編輯器。

## 組件

- `CHUNITHM-Mobile-Unity/`：遊戲、聯機客戶端、Live2D 執行時與示例皮膚。
- `CHUNITHM-Mobile-Multiplayer-Server/`：只保留 WebSocket/HTTP 房間、同步、歌曲傳輸與煙測核心。
- `CHUNITHM-Mobile-Unity/tools/SkinStudio/`：瀏覽器版與 Electron 桌面版完整原始碼，支援規則積木、場景、PNG 數字與 Live2D。

QQ Bot、機器人測試、機器人憑據、管理後台擴展與執行資料不公開；聯機核心不依賴 Bot。

## 使用

Skin Studio 可直接開啟 `CHUNITHM-Mobile-Unity/tools/SkinStudio/index.html`，桌面版在該目錄執行 `npm install`、`npm start`，測試使用 `npm test`。

服務端需要 Node.js 18+：複製 `config.example.json` 為 `config.json`，執行 `node start-server.js`，再執行 `node smoke-test.js`。

請從 [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) 下載 APK 與 IPA。IPA 未簽名，需要使用自己的 Apple 開發者帳號重新簽名。

原創程式碼採 MIT License；第三方內容遵循各自授權，詳見 `THIRD-PARTY-LICENSES.md`。

## 原始碼封存

- [完整 Skin Studio 原始碼 ZIP](CHUNITHM-Mobile-SkinStudio-0.1.0-source.zip)：瀏覽器版、Electron 桌面版、規則積木、場景、PNG 數字與 Live2D。
- [多人聯機核心原始碼 ZIP](CHUNITHM-Mobile-Multiplayer-Core-0.2.6-source.zip)：WebSocket/HTTP 房間、同步、歌曲傳輸與煙測。

兩個壓縮包都不包含 QQ Bot、Bot 測試、Bot 憑據、管理後台擴展或執行資料。

- [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [English](README.en.md)
