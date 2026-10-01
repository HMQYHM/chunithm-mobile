# Skin Studio

在 Windows、macOS 或 Linux 上可以直接双击 `index.html` 打开浏览器版，也可以运行同目录的 `open-studio.bat`。

## 桌面版

安装 Node.js 后，在本目录运行：

```text
npm install
npm start
```

生成 Windows 桌面目录版：

```text
npm run dist:win
```

输出在 `desktop-dist/win-unpacked/`，运行其中的 `CHUNITHM Mobile Skin Studio.exe`。
也可以生成可发送的 ZIP：

```text
npm run dist:win:zip
```

输出为 `desktop-dist/CHUNITHM-Mobile-Skin-Studio-win-x64.zip`。桌面壳使用 Electron 安全隔离，只加载本地制作器页面，
不开放 Node API，不允许皮肤包执行代码，也不允许页面跳转到外部网站。没有安装
Electron 依赖时，`open-studio.bat` 会自动退回浏览器版。

浏览器版和桌面版共用完全相同的皮肤格式与导出逻辑，后续 Android 版也会复用这些文件。

## 使用

1. 在左侧预览中选择图层，在右侧编辑显示、坐标、尺寸、颜色、图片和文字。
2. 在“音符下方显示模式”中选择 Combo、分数、评级剩余分数、已损失分数或距离最好成绩。
3. 使用“选择 PNG 图片”把图片加入当前元素。资源会放进导出 ZIP 的 `ui/studio/` 目录。
4. 导出 `gameplay-ui.json` 可放入已有皮肤包的 `ui/` 目录；导出皮肤 ZIP 可直接在游戏数据管理中导入。

坐标使用 0 到 1 的游玩画布归一化坐标，原点在左下角。制作器只生成受游戏白名单支持的元素 ID，皮肤不能执行脚本、替换按钮回调、改变判定或读取外部文件。

图片请使用 PNG。若手动输入了图片路径但没有在制作器中选择对应文件，JSON 会保留路径，但 ZIP 不会自动生成缺失图片。
