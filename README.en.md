# CHUNITHM Mobile

An independent Unity mobile project containing the game client, a focused multiplayer core server, and the complete open-source Skin Studio editor.

## Components

- `CHUNITHM-Mobile-Unity/`: game client, multiplayer client, Live2D runtime, and sample skins.
- `CHUNITHM-Mobile-Multiplayer-Server/`: WebSocket/HTTP rooms, synchronization, song transfer, and smoke tests only.
- `CHUNITHM-Mobile-Unity/tools/SkinStudio/`: full browser and Electron editor source, including rule blocks, scene tracks, PNG digits, and Live2D configuration.

QQ Bot code, bot tests, bot credentials, extended admin pages, and runtime data are excluded. The multiplayer core does not depend on a bot.

## Quick start

Open `CHUNITHM-Mobile-Unity/tools/SkinStudio/index.html` for the browser editor. For the desktop editor, run `npm install` and `npm start` in that directory; run `npm test` for unit tests.

For the server, use Node.js 18+, copy `config.example.json` to `config.json`, then run `node start-server.js` and `node smoke-test.js`.

Download APK and IPA builds from [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases). The IPA is unsigned and must be re-signed with your own Apple developer account.

Original code is released under the MIT License. Third-party content remains under its own licenses; see `THIRD-PARTY-LICENSES.md`.

- [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [English](README.en.md)
