# Live2D 游玩皮肤

普通 UI 布局也可以使用 `Tools/SkinStudio/index.html` 可视化编辑；制作器导出的
`ui/gameplay-ui.json` 可以和本页的 Live2D 配置放在同一个皮肤包中。Live2D 只负责
皮肤表现，UI 元素、触控读取和成绩逻辑仍由游戏控制。

游戏已接入 Cubism Core。导入的 ZIP 可以携带模型和贴图，游戏通过内置渲染器创建模型；不需要将每只猫编译进 APK。模型布局和触控参数由皮肤决定，模型不会参与判定、修改分数或读取文件系统中的其他目录。

## 包结构

ZIP 根目录包含 `skin.json`，其中 `formatVersion` 为 1，`gameplaySkin` 为 true，`id` 为唯一皮肤编号。`ui/gameplay-ui.json` 声明布局。模型的 `.model3.json` 使用相对于自身目录的资源引用，例如：

```text
skin.json
ui/gameplay-ui.json
ui/touch/tablet.png
model/cat.model3.json
model/cat.moc3
model/textures/atlas.png
LICENSE-MIT.txt
```

示例配置如下；这些参数名仅用于 BongoCat，其他模型应填自己的参数 ID：

```json
{
  "live2d": {
    "enabled": true,
    "model": "model/cat.model3.json",
    "x": 0.015,
    "y": 0.34,
    "width": 0.20,
    "height": 0.36,
    "background": { "image": "ui/touch/tablet.png", "x": -0.53, "y": -0.375, "width": 1.2, "height": 0.72 },
    "hiddenParts": ["leftstick", "rightstick", "Part8", "Part10", "Part4", "Part6"],
    "leftXParameter": "CatParamStickRX",
    "leftYParameter": "CatParamStickRY",
    "leftVisibleParameter": "CatParamStickShowRightHand",
    "leftDownParameter": "CatParamRightHandDown",
    "rightXParameter": "CatParamStickLX",
    "rightYParameter": "CatParamStickLY",
    "rightVisibleParameter": "CatParamStickShowLeftHand",
    "rightDownParameter": "CatParamLeftHandDown"
  }
}
```

`x/y/width/height` 是整个游玩 UI 的归一化坐标，原点在左下角。相机使用模型的固定画布，保持纵横比，猫爪移动不会重新计算外框。`touchOverlay.tablet` 可独立指定平板 PNG 和位置；它不替代 Live2D 模型。

需要平板始终贴合猫时，改用 `live2d.background`，并关闭普通 `touchOverlay`。background 的 x/y/width/height 是模型坐标单位，原点和模型一致，不是全屏归一化坐标；PNG 在模型之前绘制，与猫共用相机，保证不同手机和平板比例下仍对齐。只含图片，不执行动作或代码。

左右手按屏幕左右半区分配。每侧手指的平均 X 坐标先转换为该半区的 0～1，再映射到模型参数的最小值～最大值；Y 使用判定区域的 0～1。没有触点时坐标回到参数范围中点，visible/down 回到 0；按下时 visible/down 为 1。BongoCat 面向玩家，其解剖学左右手与屏幕左右相反，所以示例映射交换 L/R。

`hiddenParts` 最多 32 个 ID，隐藏直接属于相应模型 Part 的绘制对象，用于去掉摇杆、服饰等附件；不会删除模型或触控功能。

## 声明式自定义显示

自定义图片显示不要求 Live2D。把 `customDisplays` 放在
`ui/gameplay-ui.json` 的根对象中即可，图片必须是皮肤目录内的 PNG；每条规则的
`id` 必须唯一，最多 48 条。皮肤包只提供数据和图片，不会执行脚本、DLL、Shader
或回调，因此显示层不能修改判定、Combo、分数和输入。

```json
{
  "customDisplays": [
    {
      "id": "combo100",
      "trigger": "combo",
      "condition": "equal",
      "threshold": 100,
      "image": "ui/custom/combo100.png",
      "x": 0.40,
      "y": 0.70,
      "width": 0.20,
      "height": 0.16,
      "duration": 1.2,
      "fadeIn": 0.12,
      "fadeOut": 0.35,
      "scaleFrom": 0.7,
      "scaleTo": 1.0,
      "rotation": 0
    },
    {
      "id": "airRise",
      "trigger": "air",
      "value": "rising",
      "image": "ui/custom/air-rise.png"
    }
  ]
}
```

支持的 `trigger` 是 `combo`、`score`、`rank`、`judgement`、`air`、`touch` 和
`gameplay`。`combo`/`score` 使用 `threshold`，条件可用 `equal`、
`less_or_equal` 或默认的跨过阈值；`rank` 可用 `SSS+`、`SSS`、`SS+`、`SS`、
`S+`、`S` 等评级；`judgement` 的值是 `justice_critical`、`justice`、`attack`、
`miss`；`air` 可用 `active`、`rising`、`falling`、`enter`、`exit` 或
`sensor1` 到 `sensor6`；`touch` 可用 `any`、`lefttop`、`leftbottom`、
`righttop`、`rightbottom`；`gameplay` 可用 `start` 和 `stop`。

若要跟随设置中的“音符下方显示”模式触发，使用 `trigger: "displayed_metric"`，
`mode` 可填 `combo`、`remaining_combo`、`score`、`sss+`、`sss`、`ss+`、`ss`、
`s+`、`s`、`lost_score` 或 `best_score`。例如 `condition: "less"`、`threshold: 0`
会在对应的剩余值第一次跌到 0 以下时显示图片；该值以只读事件传给皮肤，仍不
参与成绩计算。动画还支持 `opacity`、`rotationTo` 和 `animateRotation: true`。

坐标和尺寸是游玩页面的 0..1 归一化值，原点在左下角。图片会按规则独立创建，
支持淡入、淡出、缩放和旋转，单次最多同时显示 24 个对象。解析会拒绝重复
ID、越界路径、非 PNG 图片、超大图片和非法数值。暂停菜单在自定义显示层之上，
不会被皮肤图片挡住。

## 动作与边界

可选 `idleMotion`、`leftPressMotion`、`rightPressMotion` 填 `.model3.json` 中的动作组名称。每组播放第一条动作；空闲动作循环，按下动作在新按下时播放一次，然后回到空闲。触控参数在动作采样之后写入，防止动作覆盖猫爪位置。动作只接受模型现有 Parameter、PartOpacity 和 Model/Opacity 曲线，不执行音效、用户事件或外部代码。

当前支持普通 Cubism Drawable、PNG 贴图、普通/兼容加算/兼容乘算混合、Over 透明混合、普通和反向裁切遮罩。Cubism 5.3 的 Offscreen 混合组和其他扩展混合模式暂不支持；遇到这些模型会记录完整失败日志，保留普通皮肤。自动物理、表情控制和口型驱动尚未接入；`.exp3.json` 可以随包存放，但不会自动播放。

## 安全与资源

- 本地导入，不下载模型，不执行包内脚本、DLL、Prefab 或 Shader。
- 保留 ZIP 的文件数、解压体积和目录穿越校验；模型资源路径也必须处于皮肤根目录内，拒绝绝对路径和盘符。
- 最大 16 张贴图、单张 4096×4096、总计不超过 16,777,216 像素。解码前检查 PNG 尺寸；最大 2,048 个 Drawable。
- 动作最多 256 条曲线、每条最多 8,192 个段数据、时长最多 600 秒；拒绝非有限数值和无效绑定。
- 模型独占贴图和离屏资源，避免预览缓存淘汰时把正在游玩的猫删掉。切换皮肤释放模型、原生数据、相机、贴图和材质；离开游玩页面停止模型和相机。
- 暂停时手势不驱动猫爪；继续倒计时恢复触控表现，判定仍由游戏的倒计时逻辑独立控制。

## 测试与示例

示例源码位于 `Examples/Skins/BongoCatLive2DTouch`，资源来自 https://github.com/vladelaina/BongoCat ，包含模型素材 MIT 许可证和用户提供的平板图片，不包含 BongoCat 程序源码。

在数据管理中导入 ZIP，然后在游玩皮肤列表选中 `BongoCat Live2D 平板触控 v4`，进入歌曲测试。v4 使用新 ID，并将左侧爪子的横向模型范围收进平板；普通音符继续使用内置基础皮肤。没有模型的普通图片皮肤也可以单独使用上面的 `customDisplays`。

`Live2DSkinSelfTest.Run` 覆盖真实 ZIP 导入、原生模型、透明像素、左手/右手/双手/暂停姿态变化、另一套带裁切遮罩的模型、目录限制和资源释放。截图写到 `Build/live2d-*.png`。Editor 测试不能代替 Android/iPhone 真机测试。

Windows Editor 原生库已采用 Unity 2022 的插件元数据。Android 使用 ARM64 Core 库；iOS 导出前通过 `GameplaySkinNativePlugins` 选择匹配设备或模拟器、Debug 或 Release 的静态库，避免重复链接。iOS 的实际 Xcode 编译和显示仍需在 Mac/iPhone 验证。
