# CHUNITHM Mobile

[Project guide](https://hmqyhm.github.io/chunithm-mobile/) · [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases)

CHUNITHM Mobile is an independent Unity mobile project. This repository publishes the project guide, a multiplayer core source archive, and a Skin Studio source archive.

## Public contents

- **Multiplayer core source**: a Node.js server archive with rooms, synchronization, song transfer, and protocol smoke tests.
- **Skin Studio**: browser and Electron desktop skin editor source for gameplay layout, assets, rules, scenes, and Live2D configuration.
- **Project guide**: available in Simplified Chinese, Traditional Chinese, Japanese, and English, with Android, iOS, and HarmonyOS platform notes.
- **Release entry**: use [GitHub Releases](https://github.com/HMQYHM/chunithm-mobile/releases) for the actual APK and IPA files.

## Source archives

- [Multiplayer core source ZIP](CHUNITHM-Mobile-Multiplayer-Core-0.2.6-source.zip)
- [Skin Studio source ZIP](CHUNITHM-Mobile-SkinStudio-0.1.0-source.zip)

### Multiplayer core

After extracting the archive, install Node.js 18 or later and run:

```text
node start-server.js
node smoke-test.js
```

See the archive `README.md` and `config.example.json` for the default port and configuration.

### Skin Studio

After extracting the archive:

- Browser version: open `index.html`.
- Desktop version: run `npm install`, then `npm start` in the archive directory.
- Tests: run `npm test`.

The editor can import an existing skin, adjust gameplay element positions, sizes, colors, and assets, and export a skin package that the game can read. See the archive documentation for supported fields and resource limits.

## Community and support

- [Discord](https://discord.gg/uuVWNBBhXR)
- [QQ group](https://qm.qq.com/q/LOzdadEsy6)
- [Bilibili](https://space.bilibili.com/351963496)
- [Support the project](https://afdian.com/a/HMQYHM)

## License

Original code is released under the [MIT License](LICENSE). Third-party content remains under its own licenses; see [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md). Songs, charts, characters, trademarks, and music are not licensed by this project.

## Languages

- [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [English](README.en.md)
