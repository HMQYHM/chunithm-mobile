Skin Studio 完整参考包

这是一个可直接导入的完整示例，覆盖界面 18 个元素、Combo/分数 0-9 数字图、音符下方显示模式、场景轨道、Live2D 触控映射和规则特效。

导入：数据管理 -> 游玩皮肤 -> 导入，选择本 ZIP；然后在游玩皮肤列表选中“Skin Studio 完整参考包”。
示例使用工程已有的 BongoCat Live2D 模型和用户提供的平板图。规则图片故意使用同一张小图来保持包体简单；制作时请替换成透明 PNG。

规则 metricLoss 默认关闭，避免示例在 SSS+ 分数跌破 0 时铺满屏幕。它展示了“显示模式 + 阈值 + 图片 + 淡入淡出”的完整字段，打开后即可测试。

Live2D 参数与 BongoCat 示例相同：左右触控中心驱动两只爪子，按下驱动 down 参数，抬手恢复 idle。模型和 MIT 许可文本来自 Examples/Skins/BongoCatLive2DTouch。
