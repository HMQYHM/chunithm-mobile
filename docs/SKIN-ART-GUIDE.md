# 游玩皮肤绘图制作指南

本文档说明当前游戏游玩皮肤中音符、长条、AIR 和判定效果的绘制规格。
目标是让你可以使用自己的原创素材重新绘制，不需要修改 Unity 代码。

## 1. 皮肤包目录

最小可导入的皮肤包结构如下：

```text
MySkin.zip
  skin.json
  notes/
    tap_01.png ... tap_16.png
    extap_01.png ... extap_16.png
    hold_01.png ... hold_16.png
    slide_01.png ... slide_16.png
    flick_01.png ... flick_16.png
    dmg_01.png ... dmg_16.png
    air_01.png ... air_16.png
    air_up_01.png ... air_up_16.png
    air_down_01.png ... air_down_16.png
    ahtap_01.png ... ahtap_16.png
    ahtapend_01.png ... ahtapend_16.png
    ahact_01.png ... ahact_16.png
    ahsdw_01.png ... ahsdw_16.png
  track/                 # 可选：长条、轨道和 AIR 主体
  effects/               # 可选：AIR 和地雷特效
  ui/                    # 可选：判定文字、Combo/分数和触控板皮肤
```

`skin.json` 必须放在 ZIP 根目录。图片使用 PNG，建议使用 RGBA 透明格式。

游戏缺少某一张图片时会回退到内置资源，所以皮肤仍然可以导入；但是如果要
完全使用自己的原创音符外观，建议把下面列出的 13 类音符全部制作齐全。

## 2. 编号和宽度

文件名末尾的 `01` 到 `16` 是谱面的实际宽度，不是动画帧编号。

当前工程使用的画布宽度对应如下：

| 编号 | 推荐画布宽度 |
| --- | ---: |
| 01 | 64 px |
| 02 | 128 px |
| 03 | 256 px |
| 04 | 256 px |
| 05 | 512 px |
| 06 | 512 px |
| 07 | 512 px |
| 08 | 512 px |
| 09 | 1024 px |
| 10 | 1024 px |
| 11 | 1024 px |
| 12 | 1024 px |
| 13 | 1024 px |
| 14 | 1024 px |
| 15 | 1024 px |
| 16 | 1024 px |

绘图时请保持主体水平居中。不要把图案贴到画布边缘，左右至少保留约 2 px
透明边距。当前工程会根据 PNG 的非透明范围适配部分地面音符；留出透明边缘
可以避免缩放后被裁切。

## 3. 音符图片规格

### 3.1 地面音符

以下 6 类的当前内置画布高度都是 `128 px`，宽度按照上一节的编号表：

| 文件前缀 | 用途 | 是否相似 |
| --- | --- | --- |
| `tap` | 普通 TAP | 基准音符 |
| `extap` | EX TAP，以及 EX HOLD 的头部 | 和 TAP 共用几何形状，但外观应独立 |
| `hold` | 地面长条头部、尾部 | 和 SLIDE 类似，但颜色和图形可不同 |
| `slide` | 滑条头部、尾部 | 和 HOLD 类似，但建议保留方向感 |
| `flick` | Flick 音符 | 可沿用 TAP 的主体比例，再加入方向或闪光标记 |
| `dmg` | 地雷音符 | 可与 TAP 共用画布尺寸，但必须明显区分 |

例子：

```text
notes/tap_01.png       64 x 128
notes/tap_05.png      512 x 128
notes/tap_16.png     1024 x 128
notes/hold_16.png    1024 x 128
notes/dmg_16.png     1024 x 128
```

HOLD 和 SLIDE 的中间长条不是由这些头部图片重复拼接出来的，主体通常来自
`track/field/hold_joint.png` 和 `track/field/slide_joint.png`。因此绘制头部时，
头部底边要能自然接到长条主体，尾部也要能平滑收口。

### 3.2 AIR 方向音符

以下文件当前使用 `256 px` 高度，宽度仍按编号表：

| 文件前缀 | 用途 |
| --- | --- |
| `air` | 普通 AIR 的兼容资源 |
| `air_up` | 向上 AIR |
| `air_down` | 向下 AIR |

`air_up` 和 `air_down` 不建议直接共用同一张图，因为方向和箭头尖端需要清楚。
如果设计完全对称，也要分别导出两组同名文件。

例子：

```text
notes/air_up_01.png      64 x 256
notes/air_up_09.png    1024 x 256
notes/air_down_16.png  1024 x 256
```

### 3.3 AIR 长条和特殊节点

| 文件前缀 | 推荐高度 | 用途 | 绘制建议 |
| --- | ---: | --- | --- |
| `ahtap` | 128 px | AIR 长条起点 | 底部要能接到 AIR 长条 |
| `ahtapend` | 128 px | AIR 长条尾部 | 与 `ahtap` 保持同一视觉语言 |
| `ahact` | 64 px | AIR 长条中间动作节点 | 使用清晰的小型标记 |
| `ahsdw` | 64 px | AIR 特殊下落/节点标记 | 不要画成普通 TAP |

`ahtap` 和 `ahtapend` 都是 `64/128/256/256/512/512/512/512/1024...` 的宽度
规格。`ahact` 也是同样的宽度规格，但高度为 `64 px`。

内置资源中 `ahsdw_05.png` 和 `ahsdw_06.png` 是很窄的特殊资源，历史画布为
`8 x 128`。自制皮肤建议统一使用对应编号的标准宽度、`64 px` 高度，并把图形
画在中间；只有在需要复刻极窄垂直标记时才使用 `8 x 128`。

代码中还存在 `ahroof`、`airup`、`airdw` 等网格名称，它们属于模型/网格资源，
不是必须绘制的 PNG。只制作图片时不需要生成这些文件，也不需要制作 OBJ。

## 4. 长条、轨道和特效图片

如果只替换音符头部，可以不提供这一部分，游戏会使用内置资源。想要整套外观
完全原创时，可以继续制作：

### 4.1 轨道和长条主体

```text
track/runtime/field_blue_16.png   轨道底纹，建议做成可平铺纹理
track/runtime/barline_blue.png    小节线，建议 64 x 8
track/runtime/goal_line.png       判定线装饰，建议 32 x 32

track/field/hold_joint.png        HOLD 中间主体
track/field/slide_joint.png       SLIDE 中间主体
track/field/air_hold_joint.png    AIR 长条主体
track/field/heaven_roof.png       AIR 顶部外观
track/field/heaven_roof_pink.png  AIR 粉色顶部外观
track/field/heaven_wall.png       AIR 侧墙
track/field/heaven_shadow.png     AIR 阴影
```

长条主体建议使用横向可平铺、纵向连续的透明 PNG。不要把完整长条画成一张
固定长度图片，否则不同持续时间和曲线会出现拉伸、断层或接缝。

### 4.2 AIR 和地雷特效

```text
effects/air/air_action_up.png       512 x 384
effects/air/air_action_down.png     512 x 384
effects/air/airhold_float.png       512 x 64
effects/air/airhold_glow.png        128 x 128
effects/air/airhold_ring.png        512 x 512
effects/air/airhold_success.png     256 x 120
effects/air/bomb_belt.png             64 x 64
effects/air/bomb_diamond.png        128 x 128
effects/air/bomb_diamonds.png       512 x 512
effects/air/bomb_fin.png             256 x 256
effects/air/bomb_flare_orange.png   128 x 128
effects/air/bomb_flare_yellow.png   128 x 128
effects/air/bomb_glow.png            144 x 144
effects/air/key_beam.png              64 x 128
```

这些图片一般是特效层，不影响音符判定。建议使用柔和的半透明边缘，避免完全
不透明的大色块遮挡谱面。

## 5. 判定、Combo 和分数图片

判定文字是可选的，目录如下：

```text
ui/judgements/justice_critical.png
ui/judgements/justice.png
ui/judgements/attack.png
ui/judgements/miss.png
```

当前内置参考尺寸：

| 文件 | 参考尺寸 |
| --- | ---: |
| `justice_critical.png` | 254 x 75 |
| `justice.png` | 254 x 59 |
| `attack.png` | 230 x 64 |
| `miss.png` | 160 x 58 |

Combo、分数、评级和触控板目前主要由 `ui/gameplay-ui.json` 控制颜色、大小和
位置，数字默认使用程序字体。如果需要在 Combo、分数、评级、判定或 AIR 状态
变化时额外显示图片，可以使用同一文件中的 `customDisplays`；它是声明式接口，
只读取皮肤包内 PNG，不替换游戏成绩逻辑。完整字段和触发值见
`docs/SKIN-LIVE2D.md` 的“声明式自定义显示”。

## 6. 哪些音符可以共用设计

可以共用基础设计，但仍要输出各自文件名：

- `tap`、`flick`、`dmg`：可以使用相同圆形主体，再分别添加 Flick 标记和地雷标记。
- `hold`、`slide`：可以使用相同底图，只改变颜色、方向线或边缘标记。
- `ahtap`、`ahtapend`：可以使用同一套颜色和边框，但起点与尾部必须分别绘制。
- `air_up`、`air_down`：可以共用主体风格，但箭头方向必须分别处理。
- `ahact`、`ahsdw`：都是 AIR 长条内部节点，可以共用颜色体系，但形状应区分。
- 01 到 16：可以从一张最大尺寸母图缩放生成，但不要把低宽度图片强行拉伸到高宽度。

## 7. 推荐绘图流程

1. 先制作 `1024 x 128` 的地面音符母图，再按宽度表生成 16 个版本。
2. 再制作 `1024 x 256` 的 AIR 母图，生成 `air_up`、`air_down` 和兼容用 `air`。
3. 制作 `1024 x 128` 的 AIR 长条起点/尾部，以及 `1024 x 64` 的中间节点。
4. 所有图形放在透明画布中央，保留透明边缘，不要改变画布比例。
5. 导出为 PNG-32/RGBA，关闭 JPG 压缩，不要使用索引色或纯色背景。
6. 按文件名放入 `notes/`，再写入 `skin.json` 并压缩 ZIP。
7. 导入游戏后先测试 TAP、HOLD、SLIDE、FLICK、地雷、AIR、AIR 长条各一首，
   特别检查宽度 01、04、08、16 是否出现裁切或接缝。

## 8. 绘制时最容易出错的地方

- 把 `01` 到 `16` 当成动画帧，而不是谱面宽度。
- 图片没有透明背景，导致矩形黑底或白底。
- 主体贴到画布边缘，经过 UV 适配后被裁切。
- HOLD/SLIDE 只画了头部，没有考虑中间主体的连接位置。
- 把 AIR 箭头画成普通 TAP，导致方向无法识别。
- 把长条画成固定长度，而不是可平铺的主体纹理。
- 只替换 PNG，却误以为会同时替换内置 OBJ 网格和轨道模型。

## 9. 最小原创音符清单

如果暂时只重绘音符，不重绘轨道和特效，至少准备：

```text
13 类 x 16 张 = 208 张 PNG

tap       extap       hold        slide       flick
dmg       air         air_up      air_down    ahtap
ahtapend  ahact       ahsdw
```

这是当前游戏完整替换音符外观所需的清单。缺少的文件会自动使用内置资源，
但这样会出现自制图和内置图混用。
