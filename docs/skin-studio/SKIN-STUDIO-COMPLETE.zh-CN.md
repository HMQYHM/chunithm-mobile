# Skin Studio 完整说明与 AI 设计规范

本文档对应当前版本的 `tools/SkinStudio` 和游戏运行时皮肤读取器。皮肤包是数据包，
只包含 JSON、PNG、音频和 Live2D 数据；游戏不会从皮肤包加载 C#、JavaScript、DLL、Shader、
Prefab 或回调。未填写的字段保留游戏原有显示，缺少单个资源时使用内置资源回退。

## 1. 最小目录

```text
MySkin.zip
  skin.json
  ui/gameplay-ui.json
  README.txt
  notes/
  track/
  effects/
  ui/
  audio/
  model/
```

`skin.json` 和 `ui/gameplay-ui.json` 必须位于 ZIP 根目录。目录名和引用路径使用 `/`，
Android 上大小写必须完全一致。皮肤包最大文件、图片像素、Live2D 依赖和路径穿越会在导入时检查。

## 2. `skin.json`

| 字段 | 类型 | 效果 |
| --- | --- | --- |
| `formatVersion` | 整数 | 当前填写 `1`。用于兼容性检查。 |
| `id` | 字符串 | 皮肤唯一 ID。建议只用小写字母、数字、`_` 和 `-`。 |
| `name` | 字符串 | 游戏内显示名称。 |
| `author` | 字符串 | 作者名称。 |
| `version` | 字符串 | 皮肤版本，不参与加载逻辑。 |
| `gameplaySkin` | 布尔值 | 必须为 `true` 才会作为游玩皮肤导入。 |

## 3. 归一化坐标

`ui/gameplay-ui.json` 中的 `x`、`y`、`width`、`height` 都相对于游玩画布，范围通常为 `0` 到 `1`：

- `x=0`：左边，`x=1`：右边。
- `y=0`：底边，`y=1`：顶边。
- `width=0.5`：占画布宽度的一半。
- `height=0.2`：占画布高度的五分之一。
- `opacity=0`：完全透明，`opacity=1`：完全不透明。
- `color` 和 `textColor` 使用 `#RRGGBB` 或 `#RRGGBBAA`。

坐标不能代替输入范围。隐藏 `touchpad`、`airZone` 或 `touchFeedback` 只隐藏外观，
不会关闭判定；全屏触控板 MOD 仍然优先使用游戏定义的输入区域。

## 4. UI 字段

```json
{
  "combo": { "color": "#F8FBFF", "fontSize": 58, "digits": [] },
  "score": { "color": "#DDF7FF", "fontSize": 42, "digits": [] },
  "judgements": { "color": "#FFFFFF", "fontSize": 16 }
}
```

| 字段 | 默认/范围 | 效果 |
| --- | --- | --- |
| `fontSize` | 8 到 256 | 程序字体字号。 |
| `defaultSize` | 正整数 | 旧版字号字段，只有大于 0 才覆盖默认尺寸。 |
| `digits` | 0 到 9 的图片 | Combo 或分数的数字图片。只有当前显示值所需的数字全部存在时才启用，否则回退文字。 |
| `color` | 颜色 | Combo、分数或判定文字颜色。 |

可覆盖的 `elements.id` 包括：

`songInfo`、`songCover`、`scoreHud`、`comboHud`、`judgement`、`judgementImage`、
`pauseButton`、`pausePanel`、`progressBar`、`fps`、`countdown`、`touchpad`、`airZone`、
`airBoundaryBottom`、`airBoundaryTop`、`airGuide`、`trackMask`、`touchFeedback`。

每个元素可使用：

| 字段 | 范围 | 效果 |
| --- | --- | --- |
| `visible` | `true/false` | 是否绘制该元素。 |
| `x/y/width/height` | 归一化坐标 | 元素位置和大小。 |
| `opacity` | 0 到 1 | 元素整体透明度。 |
| `color` | 颜色 | 背景、轨道、边界或进度颜色。 |
| `image` | 包内 PNG | 图片覆盖层，路径必须在皮肤目录内。 |
| `label` | 字符串 | 只改变按钮显示文字，不改变按钮行为。 |
| `textSize` | 8 到 256 | 单个元素字号。 |
| `textColor` | 颜色 | 单个元素文字颜色。 |

## 5. 场景与轨道

```json
"scene": {
  "background": { "image": "ui/background.png", "color": "#06111B", "opacity": 0.85 },
  "track": {
    "image": "track/runtime/field_blue_16.png",
    "laneImage": "track/runtime/barline_blue.png",
    "color": "#FFFFFF",
    "laneColor": "#36CBE0",
    "guideColor": "#7FEFFF",
    "edgeColor": "#A9F5FF",
    "judgementLineColor": "#FFFFFF"
  }
}
```

- `background.image`：替换游玩背景图。
- `background.color`：背景底色或图片叠加色。
- `background.opacity`：背景层透明度，0 到 1。
- `track.image`：轨道底图。
- `laneImage`：分道线图片。
- 其余颜色分别控制轨道、分道线、引导线、边缘和判定线。

歌曲封面仍来自歌曲数据。皮肤没有封面覆盖图时，游戏自动显示歌曲原封面。

## 6. 音符、长条和特效

音符图片放在 `notes/`，文件名末尾 `01` 到 `16` 是谱面宽度，不是动画帧。支持：

`tap`、`extap`、`hold`、`slide`、`flick`、`dmg`、`air`、`air_up`、`air_down`、
`ahtap`、`ahtapend`、`ahact`、`ahsdw`。

长条主体放在 `track/field/`：

`hold_joint.png`、`slide_joint.png`、`air_hold_joint.png`、`heaven_roof.png`、
`heaven_roof_pink.png`、`heaven_wall.png`、`heaven_shadow.png`。

特效放在 `effects/air/`，支持 AIR 动作、AIR 长条、地雷爆炸和判定辅助图。图片使用透明 PNG，
长条主体应设计为可平铺纹理，不要绘制固定长度的完整长条。

## 7. 音频

当前皮肤包能替换的音频为：

| 路径 | 用途 | 格式 |
| --- | --- | --- |
| `audio/ui_entry.mp3` | 进入界面音效 | MP3/OGG/WAV |
| `audio/ui_results.mp3` | 进入结算界面音效 | MP3/OGG/WAV |
| `audio/delay_ready.wav` | 延迟测试准备音 | MP3/OGG/WAV |
| `audio/se_touch_answer.wav` | 正解音 | MP3/OGG/WAV |

正解音会按谱面判定点合并到游玩音频。歌曲的背景音乐仍由歌曲包提供，当前皮肤包不会替换歌曲 BGM；
这样不会改变谱面原曲、时长或联机同步。

## 8. 触控板和触控爪

```json
"touchOverlay": {
  "enabled": true,
  "tablet": { "image": "ui/touch/tablet.png", "x": 0.24, "y": 0.08, "width": 0.52, "height": 0.30 },
  "leftPaw": { "image": "ui/touch/left-paw.png", "width": 0.14, "height": 0.18, "offsetX": 0, "offsetY": 0, "smoothing": 0.18 },
  "rightPaw": { "image": "ui/touch/right-paw.png", "width": 0.14, "height": 0.18, "offsetX": 0, "offsetY": 0, "smoothing": 0.18 }
}
```

`smoothing` 越大跟手越快，越小越平滑；建议 0.08 到 0.35。`offsetX` 和 `offsetY` 是相对于触控板的归一化偏移。

## 9. Live2D

```json
"live2d": {
  "enabled": true,
  "model": "model/cat.model3.json",
  "x": 0.015, "y": 0.34, "width": 0.20, "height": 0.36,
  "leftXParameter": "CatParamStickRX",
  "leftXRange": { "min": 0.75, "max": 1.0 },
  "idleMotion": "CAT_motion",
  "leftPressMotion": "CAT_motion_lock",
  "rightPressMotion": "CAT_motion_lock"
}
```

- `x/y/width/height`：模型在游玩画布中的位置和大小。
- `*Parameter`：模型中实际存在的参数 ID。
- `*Range.min/max`：触控值映射到模型参数的归一化范围，0 到 1。
- `hiddenParts`：隐藏模型部件 ID，最多 32 个。
- `idleMotion`：无触控时动作组。
- `leftPressMotion/rightPressMotion`：左右触控时动作组。
- `background.image`：模型背后的图片，坐标使用模型空间，范围允许大于 1。

模型依赖文件、贴图、MOC3、CDI3、动作和表情必须一起放入 `model/`。制作器会检查模型依赖、
动作组、参数名称和大小写。没有安装 Cubism SDK 时，游戏会记录警告、禁用模型渲染，并保留其他皮肤内容。

## 10. 规则积木

规则可以监听 Combo、分数、评级、JC/J/Attack/Miss、AIR、触控区域、游玩开始/结束和音符下方显示值。

图片动作参数：

| 字段 | 建议范围 | 效果 |
| --- | --- | --- |
| `duration` | 0.05 到 30 秒 | 显示总时长。 |
| `fadeIn` | 0 到 `duration` | 淡入时长。 |
| `fadeOut` | 0 到剩余时长 | 淡出时长。 |
| `scaleFrom/scaleTo` | 0.1 到 4 | 起始和结束缩放。 |
| `rotation/rotationTo` | 任意角度 | 起始和结束 Z 轴旋转。 |
| `opacity` | 0 到 1 | 特效透明度。 |
| `x/y/width/height` | 0 到 1 | 图片位置和大小。 |

规则是越过阈值时触发一次，不会每帧重复触发。最多 48 条规则同时保存，最多 24 条特效同时显示。

## 11. 给 AI 的皮肤设计提示词

可以把下面模板和本文档一起交给 AI：

```text
请为 CHUNITHM Mobile 设计一套可导入的游玩皮肤。
主题：<主题>
主色：<颜色>
辅助色：<颜色>
视觉密度：低 / 中 / 高
是否启用 Live2D：是 / 否
是否启用触控板和触控爪：是 / 否
目标设备：Android / iOS，横屏，安全区不可遮挡

只使用当前 Skin Studio 支持的 skin.json、ui/gameplay-ui.json、PNG、音频和 Live2D 数据。
请输出：
1. 完整目录树；
2. skin.json；
3. ui/gameplay-ui.json；
4. 每张图片的建议尺寸和透明区域；
5. 每个坐标、字号、透明度、持续时间和 Live2D 范围的理由；
6. 缺少资源时的回退说明。

不要生成脚本、Shader、DLL、Prefab、网络请求或未实现的歌曲 BGM 替换字段。
不要隐藏判定线、触控区域或关键成绩信息。所有引用路径必须位于皮肤 ZIP 内。
```

## 12. 导出前检查

1. ZIP 根目录存在 `skin.json` 和 `ui/gameplay-ui.json`。
2. JSON 可以被制作器重新打开。
3. 所有 `image`、`digits`、音频和 Live2D 路径大小写一致。
4. 图片为 PNG，透明边缘没有裁切关键图案。
5. 触控板、AIR 区域和判定线没有被不透明背景覆盖。
6. 先在制作器预览，再导入游戏验证游玩、暂停、MISS、Combo、AIR 和结算页面。
