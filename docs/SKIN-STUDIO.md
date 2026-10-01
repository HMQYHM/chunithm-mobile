# 游玩皮肤可视化制作器

制作器位于 `Tools/SkinStudio/index.html`，不依赖 Unity 或服务器。Windows 可以双击
`open-studio.bat`，macOS/Linux 可以直接用浏览器打开 `index.html`。

## 工作流程

1. 在“元素图层”添加或选择元素。
2. 在右侧修改显示开关、归一化坐标、尺寸、透明度、颜色、PNG 路径和按钮标签。
3. 在预览状态中查看游玩、暂停、MISS、Combo 100、AIR 和目标值归零状态。
4. 选择“音符下方显示模式”。该设置会写入 `belowNoteMetric.mode`。
5. 导出 JSON，或直接导出 ZIP 皮肤包。

预览画布是 16:9 横屏比例。坐标原点在左下角，`x/y/width/height` 范围为 0 到 1。
导出的 ZIP 包含 `skin.json`、`ui/gameplay-ui.json`、`README.txt` 和制作器中选择的
PNG 文件。导出 ZIP 使用无压缩存储，便于游戏导入和排查资源。

## 支持的元素

`songInfo`、`songCover`、`scoreHud`、`comboHud`、`judgement`、`judgementImage`、
`pauseButton`、`pausePanel`、`progressBar`、`fps`、`countdown`、`touchpad`、
`airZone`、`airBoundaryBottom`、`airBoundaryTop`、`airGuide`、`trackMask`、
`touchFeedback`。

元素是已有游戏 UI 的声明式覆盖层。`pauseButton` 和 `pausePanel` 只允许修改外观、
位置、透明度和文字，皮肤不能替换暂停、返回或确认按钮的行为。隐藏判定、AIR 引导
或触控反馈只影响显示，不影响输入和成绩逻辑。

## 显示模式

- `combo`：当前 Combo 从 0 增加。
- `remaining_combo`：距离满 Combo 的剩余值。
- `score`：当前分数从 0 增加。
- `sss+`、`sss`、`ss+`、`ss`、`s+`、`s`：距离对应评级门槛的剩余分数。
- `lost_score`：只显示已经损失的分数。
- `best_score`：按当前规则显示距离最好成绩的剩余差值。

## 自定义触发显示

制作器会保留导入 JSON 中的 `customDisplays`。需要手动编辑触发规则时，放在
`ui/gameplay-ui.json` 根对象中。支持 Combo、分数、评级、判定、AIR、触控、游玩状态
和 `displayed_metric`。图片必须是皮肤目录内的 PNG。触发显示只能渲染图片，不能改动
判定、输入、Combo 或分数。

## 安全边界

游戏和制作器都只使用 JSON 与 PNG。皮肤包不能执行 JavaScript、DLL、Shader、Prefab
或回调；路径必须位于皮肤目录内。游戏端会限制元素数量、图片尺寸、文件扩展名、颜色、
标签长度和归一化数值。导入后若图片缺失，游戏使用内置资源或保留空白，不会从网络下载。
