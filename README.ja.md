# CHUNITHM Mobile

[プロジェクト説明](https://hmqyhm.github.io/chunithm-mobile/) · [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases)

CHUNITHM Mobile は独立した Unity モバイルプロジェクトです。このリポジトリでは、プロジェクト説明サイト、マルチプレイ中核のソースアーカイブ、Skin Studio のソースアーカイブを公開しています。

## 公開内容

- **マルチプレイ中核ソース**：ルーム、同期、楽曲転送、プロトコルのスモークテストを含む Node.js サーバーアーカイブ。
- **Skin Studio**：プレイ画面のレイアウト、素材、ルール、シーン、Live2D 設定を編集できるブラウザ版・Electron 版の制作ツール。
- **プロジェクト説明サイト**：簡体字中国語、繁体字中国語、日本語、英語に対応し、Android・iOS・HarmonyOS の案内を掲載。
- **リリース入口**：実際の APK と IPA は [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) の掲載ファイルを確認してください。

## ソースアーカイブ

- [マルチプレイ中核ソース ZIP](CHUNITHM-Mobile-Multiplayer-Core-0.2.6-source.zip)
- [Skin Studio ソース ZIP](CHUNITHM-Mobile-SkinStudio-0.1.0-source.zip)

### マルチプレイ中核

アーカイブを展開し、Node.js 18 以降を用意して次を実行します。

```text
node start-server.js
node smoke-test.js
```

デフォルトポートと設定は、アーカイブ内の `README.md` と `config.example.json` を確認してください。

### Skin Studio

アーカイブを展開した後：

- ブラウザ版：`index.html` を開きます。
- デスクトップ版：アーカイブのディレクトリで `npm install`、続けて `npm start` を実行します。
- テスト：`npm test` を実行します。

既存スキンを読み込み、プレイ画面の要素の位置・サイズ・色・素材を調整し、ゲームで読み込めるスキンパッケージを書き出せます。対応項目と素材の制限はアーカイブ内のドキュメントを確認してください。

## コミュニティと支援

- [Discord](https://discord.gg/uuVWNBBhXR)
- [QQ グループ](https://qm.qq.com/q/LOzdadEsy6)
- [Bilibili](https://space.bilibili.com/351963496)
- [プロジェクトを支援](https://afdian.com/a/HMQYHM)

## ライセンス

オリジナルコードは [MIT License](LICENSE) で公開しています。第三者コンテンツは各ライセンスに従います。詳細は [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md) を確認してください。楽曲、譜面、キャラクター、商標、音楽は本プロジェクトのライセンス対象ではありません。

## 言語

- [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [English](README.en.md)
