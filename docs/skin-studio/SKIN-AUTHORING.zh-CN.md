# 游玩皮肤制作文档

本文对应当前游戏运行时和 Skin Studio 导出的 `formatVersion: 1` 包格式。皮肤包是 ZIP，不能包含 C#、DLL、Shader、可执行文件或脚本；游戏只读取白名单内的 JSON、PNG 和受支持的 Live2D 资源，因此导入包不会获得执行代码的能力。

## 1. 最小目录

```text
MySkin.zip
├─ skin.json
├─ ui/
│  └─ gameplay-ui.json
├─ ui/numbers/combo/0.png ... 9.png
├─ ui/numbers/score/0.png ... 9.png
├─ ui/effects/combo100.png
└─ model/                 # 可选，启用 Live2D 时放这里
   ├─ cat.model3.json
   ├─ cat.moc3
   ├─ cat.cdi3.json
   ├─ texture_00.png
   └─ motion.motion3.json
```

导入时 ZIP 内的相对路径必须保持正斜杠，不能使用绝对路径、`..`、驱动器名或符号链接。推荐先在制作器打开现有皮肤包，修改后使用“保存/导出 ZIP”。完整可导入参考包位于 `Examples/Skins/StudioComplete`。

## 2. `skin.json`

```json
{
  "formatVersion": 1,
  "id": "my_skin_001",
  "name": "我的游玩皮肤",
  "author": "作者名",
  "version": "1.0.0",
  "gameplaySkin": true
}
```

`id` 只能使用字母、数字、下划线和连字符，并且应保持唯一。`gameplaySkin` 必须为 `true`。

## 3. 图片规格

所有外部图片使用 PNG。推荐使用带透明通道的 RGBA PNG；UI 图片按元素的宽高比绘制，数字图片会保持原始宽高比。单张图片不超过 4096x4096，规则图片会受到解码内存预算限制。推荐尺寸如下：

| 用途 | 推荐尺寸 | 说明 |
|---|---:|---|
| 音符下方数字 | 高 64–256 px | 每个数字单独一张，透明背景；Combo 与分数各一套 |
| 判定图 | 512x128 或 1024x256 | 透明 PNG，实际大小由 `judgementImage` 决定 |
| HUD 图标/暂停按钮 | 128–512 px | 留透明边距，避免按钮内容贴边 |
| 规则特效 | 256–1024 px | 透明 PNG；用持续时间控制性能 |
| 轨道/背景 | 1920x1080 或可平铺比例 | 场景图会受设备分辨率缩放 |
| 平板装饰 | 1024x1024 以内 | Live2D background 使用模型空间坐标 |

制作器会检查 PNG 类型、路径、尺寸和画布范围；运行时还会再次检查，手工修改 JSON 不会绕过安全限制。

## 4. 界面布局

`gameplay-ui.json` 的 `elements` 是数组。坐标是画布归一化坐标，左下角为 `(0,0)`，右上角为 `(1,1)`。`x/y/width/height` 应满足 `x+width<=1`、`y+height<=1`。`visible` 控制显示，`opacity` 为 0 到 1，`color` 和 `textColor` 使用 `#RRGGBB` 或 `#RRGGBBAA`。

当前 18 个 ID：`songInfo` 歌曲信息、`songCover` 封面、`scoreHud` 分数、`comboHud` Combo、`judgement` 判定区域、`judgementImage` 判定图片、`pauseButton` 暂停按钮、`pausePanel` 暂停面板、`progressBar` 进度条、`fps` 帧数、`countdown` 倒计时、`touchpad` 触控板、`airZone` AIR 区域、`airBoundaryBottom`/`airBoundaryTop` AIR 边界、`airGuide` 引导线、`trackMask` 轨道遮罩、`touchFeedback` 触控反馈。

暂停按钮和暂停面板的行为仍由游戏提供；皮肤只能改位置和外观。`touchpad`、`airZone` 的布局同时映射到输入采样，并会自动考虑 iOS 安全区。隐藏输入元素不会关闭采样；全屏触控板 MOD 仍优先。游戏界面中的封面和判定图虽然是嵌套对象，运行时会把制作器的全局画布坐标转换到正确的父节点。

## 5. 数字与指标

```json
{
  "combo": { "color":"#FFFFFF", "fontSize":58, "digits":[{"digit":"0","image":"ui/numbers/combo/0.png"}] },
  "score": { "color":"#FFFFFF", "fontSize":42, "digits":[] },
  "judgements": { "color":"#FFFFFF", "fontSize":16 },
  "belowNoteMetric": { "mode":"combo" }
}
```

Combo 和分数各自支持 0 到 9 十张图片；缺少当前数值所需的数字时，游戏回退到普通文字。可用模式为：`combo`、`remaining_combo`、`score`、`sss+`、`sss`、`ss+`、`ss`、`s+`、`s`、`lost_score`、`best_score`。该字段只改变音符下方显示，不改变判定或实际成绩。制作器中的预览、撤回、保存状态属于制作器功能，不会写入游戏运行时。

## 6. 场景

```json
"scene": {
  "background": {"image":"ui/background.png","color":"#06111B","opacity":0.8},
  "track": {"image":"ui/track.png","laneImage":"ui/lane.png","color":"#FFFFFF",
    "laneColor":"#36CBE0","guideColor":"#7FEFFF","edgeColor":"#A9F5FF",
    "judgementLineColor":"#FFFFFF"}
}
```

背景、轨道和分道图片必须是包内 PNG。颜色会作为材质颜色使用；没有提供的字段保留内置皮肤。

## 7. 规则特效

`customDisplays` 最多 48 项，每项需要唯一 `id`、`trigger`、`action`。`action` 只能是 `image` 或 `live2dMotion`。图片规则使用 `image` 和画布位置；`duration` 0.05–30 秒，`fadeIn/fadeOut` 为秒，`scaleFrom/scaleTo` 为 0.01–4，`rotation/rotationTo` 为角度，`opacity` 为 0–1。

触发来源：`combo`、`score`、`displayed_metric` 是数值阈值；`rank` 是 `sss+` 到 `d`；`judgement` 是 `justice_critical`、`justice`、`attack`、`miss`；`air` 支持 `active`、`rising`、`falling`、`enter`、`exit`、`sensor1` 到 `sensor6`；`touch` 支持 `any`、`lefttop`、`leftbottom`、`righttop`、`rightbottom`；`gameplay` 支持 `start`、`stop`。数值条件支持 `greater_or_equal`、`equal`、`less`、`less_or_equal`；AIR/touch 的 `rising` 表示边沿触发，`exit` 表示离开。

例如，SSS+ 剩余分数低于 0 后显示滤镜：

```json
{"id":"sss_plus_failed","trigger":"displayed_metric","mode":"sss+","condition":"less","threshold":0,"action":"image","image":"ui/effects/fail.png","x":0,"y":0,"width":1,"height":1,"duration":0.5,"opacity":0.25}
```

规则层只能显示图片或请求 Live2D 动作，不能执行代码、改变判定、改变分数或读取账号数据。暂停和倒计时时，视觉层可以继续读取触控轨迹；游戏判定层仍按独立规则屏蔽不应计入的输入。

## 8. Live2D

`live2d.enabled` 为 `true` 后提供 `model` 路径和屏幕归一化矩形 `x/y/width/height`。参数名必须与模型中的参数 ID 完全一致。`leftXParameter/rightXParameter` 和 Y 参数读取左右触控中心点，范围对象的 `min/max` 为 0 到 1，反向范围是允许的。`leftVisibleParameter/rightVisibleParameter` 控制有无手指时的爪子显示，`leftDownParameter/rightDownParameter` 控制按下状态；`idleMotion`、`leftPressMotion`、`rightPressMotion` 是模型中存在的动作组名称。

`background` 的 x/y/width/height 属于模型空间，适合放平板或桌面图；它和模型一起缩放。`hiddenParts` 只隐藏已存在的模型 Part。制作器可以保存这些字段，但浏览器预览只绘制边界，不运行 Cubism 原生模型；实际动作需在游戏或 Editor Live2D 自测中验证。

## 9. 安全与性能

不要在包内放令牌、账号、服务器地址或脚本。导入器限制 ZIP 条目数量、解压总大小、单文件大小、路径和 PNG 解码像素；Live2D 模型只加载白名单资源。规则最多 48 项，运行时同时显示的规则效果有限。规则图片尽量小，避免在每个判定都触发全屏图片；数字图片预先准备完整 0–9 集合。

## 10. 测试清单

1. 在制作器导入 ZIP，检查元素可见性、重叠选择、拖动、规则位置、Live2D 映射，保存后重新打开。
2. 在游戏中导入 ZIP，重新启动游戏，确认皮肤仍在列表并能激活。
3. 进入一首歌：检查封面、分数、Combo、判定图、暂停按钮、进度条、AIR 边界、轨道和触控反馈。
4. 分别触发 Combo/分数/评级/判定/AIR/touch 规则，暂停、倒计时、切歌后确认效果不会卡在上一局。
5. 对 Live2D 检查左右触控、移动、按下、抬手、暂停和两个屏幕方向；没有模型的设备应至少正常显示其余 UI。

可直接参考并导入：`Examples/Skins/StudioComplete`。该目录可用 ZIP 工具压缩，本文发布时会同时提供已经打好的 ZIP。
