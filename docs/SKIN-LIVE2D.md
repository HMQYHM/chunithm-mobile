# Live2D 游玩皮肤接口

当前皮肤包可以在 `ui/gameplay-ui.json` 中声明 Live2D 模型及触控参数映射：

```json
{
  "live2d": {
    "enabled": true,
    "model": "model/cat.model3.json",
    "idleMotion": "CAT_motion",
    "leftPressMotion": "CAT_motion_lock",
    "rightPressMotion": "CAT_motion_lock",
    "leftXParameter": "ParamMouseX",
    "leftYParameter": "ParamMouseY",
    "rightXParameter": "ParamMouseX",
    "rightYParameter": "ParamMouseY",
    "leftDownParameter": "ParamMouseLeftDown",
    "rightDownParameter": "ParamMouseRightDown"
  }
}
```

模型文件允许使用 `.model3.json`、`.moc3`、`.cdi3`、`.motion3` 和 `.exp3`，仍然受到皮肤包大小、文件数量和路径校验限制。皮肤包不能包含 C#、DLL、Prefab、Shader 或原生运行时代码。

游戏每帧将左右触控区域的中心点、数量、速度和按下状态转换为只读 Live2D 参数帧。参数名经过字符白名单校验，动作名只允许字母、数字、下划线和短横线。

当前工作区尚未安装 Cubism SDK for Unity，因此 `GameplaySkinLive2DBridge` 已完成参数桥接和缺少 SDK 时的安全降级，但暂时不会渲染 `.moc3`。接入官方 Cubism SDK 后，渲染器只需要订阅桥接层的 `ParametersUpdated` 事件即可复用这套输入接口。
