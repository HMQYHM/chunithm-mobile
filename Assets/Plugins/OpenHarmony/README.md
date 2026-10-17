# OpenHarmony Unity bridge

本目录记录 Unity 与 HarmonyOS 原生层的接入边界。当前 Unity 2022.3 安装没有确认到可用的 OpenHarmony 导出模块，因此这里提供的是接口契约和安全回退，不是可运行的 HAP 运行时。

需要接入原生 Runtime 后，按以下能力实现 `IOpenHarmonyBridge`：

- `File`：歌曲包、谱面、皮肤和背景图片选择；应用沙箱路径及导入目录。
- `Audio`：音频焦点、低延迟输出、DSP 时钟和后台切换。
- `Network`：HTTPS/WebSocket/TLS，服务端联机和 GitHub 更新检查。
- `Lifecycle`：UIAbility 前后台、返回键、暂停恢复和屏幕方向。
- `ExternalUrl`：拉起系统浏览器打开 GitHub Release、Discord 等链接。
- `Log`：写入 hilog 并导出游戏日志。

对应的 HarmonyOS Stage 契约位于：

`harmonyos/CHUNITHM-Mobile-HarmonyOS/entry/src/main/ets/common/HarmonyOSBridgeContract.ets`

在没有原生桥接实现时，游戏必须继续使用 Unity 的 Android/iOS/编辑器回退实现，不能把 Stage 启动壳宣称为完整游戏移植。
