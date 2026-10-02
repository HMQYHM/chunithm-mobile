# CHUNITHM Mobile

独立した Unity 製のモバイルプロジェクトです。ゲームクライアント、マルチプレイ中核サーバー、完全オープンソースの Skin Studio を含みます。

## コンポーネント

- `CHUNITHM-Mobile-Unity/`：ゲーム、通信クライアント、Live2D ランタイム、サンプルスキン。
- `CHUNITHM-Mobile-Multiplayer-Server/`：WebSocket/HTTP のルーム、同期、楽曲転送、スモークテストだけを公開。
- `CHUNITHM-Mobile-Unity/tools/SkinStudio/`：ブラウザ版と Electron 版の完全なソースコード。ルール、シーン、PNG 数字、Live2D を編集できます。

QQ Bot、Bot テスト、Bot の認証情報、管理画面の拡張、実行時データは公開していません。マルチプレイ中核は Bot に依存しません。

## 使い方

Skin Studio は `CHUNITHM-Mobile-Unity/tools/SkinStudio/index.html` をブラウザで開けます。デスクトップ版は同じディレクトリで `npm install`、`npm start`、テストは `npm test` です。

サーバーは Node.js 18 以降で `config.example.json` を `config.json` にコピーし、`node start-server.js`、`node smoke-test.js` を実行します。

APK と IPA は [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) から取得できます。IPA は未署名のため、自分の Apple 開発者アカウントで再署名してください。

オリジナルコードは MIT License、第三者コンテンツは各ライセンスに従います。詳細は `THIRD-PARTY-LICENSES.md` を確認してください。

## ソースアーカイブ

- [完全な Skin Studio ソース ZIP](CHUNITHM-Mobile-SkinStudio-0.1.0-source.zip)：ブラウザ版、Electron 版、ルール、シーン、PNG 数字、Live2D。
- [マルチプレイ中核ソース ZIP](CHUNITHM-Mobile-Multiplayer-Core-0.2.6-source.zip)：WebSocket/HTTP ルーム、同期、楽曲転送、スモークテスト。

どちらのアーカイブにも QQ Bot、Bot テスト、Bot 認証情報、管理画面拡張、実行時データは含まれません。

- [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [English](README.en.md)
