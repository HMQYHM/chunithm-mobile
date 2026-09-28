# ACB/AWB 音频导入

歌曲导入现在可以识别 CRI 的 `.acb` 和 `.awb` 音频库。

## 行为

- 已有 `audio/gameplay.mp3` 或 `audio/gameplay.wav` 时直接使用，不覆盖原文件。
- 没有可播放音频但存在 ACB/AWB 时，桌面端导入会调用 `.tools/vgmstream/vgmstream-cli.exe`。
- 有 ACB 时以 ACB 作为入口，由 vgmstream 按同目录配对关系查找 AWB；只有 AWB 时直接解码 AWB。
- 转换结果写入 `audio/gameplay.wav`，原始 ACB/AWB 保留。
- 生成的 WAV 使用第一个子音轨；多子音轨音频库需要先在桌面端选择/整理目标音轨。
- MP3 优先于 WAV，选歌试听和正式游玩都根据扩展名选择 Unity 解码器。

## 工具与平台

当前工程内置的是 Windows 桌面 CLI 适配器。Android/iOS 不能直接启动 Windows CLI；移动端要在应用内直接导入 ACB/AWB，还需要将同一 vgmstream 解码核心分别编译为 Android `arm64-v8a` 原生库和 iOS 静态库，并接入对应的 Unity native bridge。

如果移动端导入包只含 ACB/AWB，界面会显示明确的“缺少移动端原生解码库”错误，不会产生一个导入成功但无法游玩的歌曲目录。先在桌面端完成转换后再导入 `gameplay.wav`，两端都可以正常播放。

## 许可证

vgmstream 使用宽松的许可条款。随工程分发的 CLI 版权与许可证文本位于 `.tools/vgmstream/COPYING`；重新编译或替换 vgmstream 时应同步更新该文件。
