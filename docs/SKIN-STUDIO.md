# 游玩皮肤可视化制作器

制作器位于 `Tools/SkinStudio/index.html`，不依赖 Unity 或服务器。Windows 可以双击
`open-studio.bat`，macOS/Linux 可以直接用浏览器打开 `index.html`。

当前同时提供 Electron 桌面封装配置。进入 `tools/SkinStudio` 后执行 `npm install`、
`npm start` 可运行窗口版，执行 `npm run dist:win` 可生成 Windows 桌面目录版，执行
`npm run dist:win:zip` 可生成可发送的 ZIP。桌面版不向皮肤页面暴露 Node API，只加载
本地制作器资源；未安装 Electron 时可继续使用浏览器版。

桌面导出通过受限的保存接口调用原生“另存为”窗口。仅信任本地主页面，只接受固定的
JSON/ZIP 导出名与限定大小的字节数据，不接受页面指定的文件路径；取消后不写磁盘。

## 工作流程

已有皮肤优先点击顶部“打开皮肤文件夹”，选择解压后包含 `skin.json` 与
`ui/gameplay-ui.json` 的根目录。操作保留原名称、ID、模型路径、背景与手爪校准，完整校验
成功后才替换当前项目。关闭制作器前请导出 ZIP，下次解压后重新打开；不会自动保存大模型。

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

## Live2D 面板

Live2D 设置位于右侧属性栏顶部的 Live2D 标签，默认显示，包含启用开关、模型文件夹导入与选择、位置尺寸、
左右手参数和轴范围、动作组、隐藏部件、模型背景 PNG。参数名可从模型的 `.cdi3.json`
选取，动作组可从 `.model3.json` 选取；BongoCat 预设沿用游戏的屏幕左右手映射。

模型资源导入保留子目录，放入 ZIP 的 `model/` 下。导出前检查资源引用、目录穿越、
文件数、大小、PNG 尺寸、贴图总像素和缺失背景。只接纳模型数据、PNG 与许可证，
不加载 SDK 或执行代码。物理和表情文件可携带，但游戏当前不自动驱动它们。

完整皮肤目录按原有皮肤相对路径导入，不再次添加 `model/`。导出额外校验所有 UI 图片、
实际 PNG 解码、MOC3 文件头、动作组、CDI 参数名称和大小写冲突。新项目使用独立 ID；
已有项目保留 ID，游戏内已有同 ID 时先删旧版或修改 ID，避免不同皮肤相互覆盖。

画布当前只预览模型的布局范围和背景图，不是 Cubism 实时预览；真实动作在游戏验证。
导入 JSON 会保留已有 Live2D 和其他顶层配置，避免制作器误删已有猫爪校准参数。

## 回归验证

`npm test` 验证共享路径、校准与引用规则。`tests/live2d-ui.cjs` 使用实际 BongoCat 目录，
验证完整项目导入、连续编辑、JSON 字段保留、ZIP 字节、缺图/错误参数/错误目录拦截和
桌面/手机布局。设置 `CHUNITHM_STUDIO_SKIN_ZIP` 为测试导出的 `test-output/live2d-skin.zip`，
运行 `tools/develop.ps1 -Mode Test -TestName Live2DSkinSelfTest`，Unity 会读取这份确切 ZIP，
检查原生模型透明像素、左右手、暂停、反向校准、两种分辨率的实际 UI 合成和资源释放。
测试使用临时随机 ID，不覆盖用户已安装皮肤；这不是 Android/iOS 实机测试。

## 安全边界

游戏和制作器只读取 JSON、PNG 和 Live2D 模型数据。皮肤包不能执行 JavaScript、DLL、Shader、Prefab
或回调；路径必须位于皮肤目录内。游戏端会限制元素数量、图片尺寸、文件扩展名、颜色、
标签长度和归一化数值。导入后若图片缺失，游戏使用内置资源或保留空白，不会从网络下载。
