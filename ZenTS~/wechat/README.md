# 微信小游戏：平台内嵌 JS VM

支持范围是 Unity 2022.3 LTS 及后续 LTS，以及基于 2022.3 LTS 的团结引擎。原生 Editor 使用 V8，小游戏脚本直接运行在微信宿主 JS VM；wasm 中不集成 V8 或其他解释器。

本轮实际运行环境：Unity 2022.3.62f3、Windows 微信开发者工具 Stable 2.01.2510260、基础库 3.17.6。团结 `WeixinMiniGame` 的路由与构建分支已接入，本轮未运行团结 Editor，也没有手机真机验收。

## 安装与配置

1. 安装 Unity WebGL 平台模块，以及 ZenTS 对应 Editor 的原生 SDK，完成 `ZenTS/Install`。
2. 通过 Package Manager 安装[微信官方转换 SDK](https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk)。本测试工程固定提交 `915d4bf4f60ef0cd1c29316a8d67257185d609a7`，SDK 标识 `202610200817`。
3. 精简工程需要补齐 SDK 所需 Unity UI、AssetBundle 与 UnityWebRequest 等模块；测试工程 manifest 已包含相关依赖。
4. 登录微信开发者工具，准备具有小游戏及官方 UnityPlugin 使用权限的真实 AppID。
5. 切换到 Unity WebGL；团结使用已安装的 `WeixinMiniGame` 平台。配置 SDK 的 AppID、输出位置和资源加载方式。

SDK 是可选依赖。ZenTS.Editor 不直接引用 WxEditor 程序集，未安装微信 SDK 不影响原生平台使用；点击微信构建菜单时才检查依赖。

## 构建

菜单 `ZenTS/Build WeChat Mini Game` 调用官方 `WXConvertCore.DoExport(true)`，不维护另一套转换器。

- 微信 SDK 的可写配置在 `Assets/WX-WASM-SDK-V2/Editor/MiniGameConfig.asset`，模板复制到项目 Assets；ZenTS 的 PackageCache 保持只读。
- 正常构建保留 SDK 的项目名、封面、CDN/分包、Development 等已有选择。Linear 色彩空间使用 WebGL2。
- 当前 HostJs 支持单线程构建，关闭 SDK 的 `enableRenderThread`；保留完整异常支持。
- 转换期间的模板、emscripten 参数、异常、缓存、图形 API、线程与 IL2CPP 代码生成设置在结束或异常时恢复。
- 输出默认由 SDK 配置决定，未配置时使用 `Build-WeChat`。拒绝向包目录、Assets、项目根目录或文件系统根目录导出。

命令行环境变量：

| 变量 | 用途 |
| --- | --- |
| `ZENTS_WECHAT_APPID` | 覆盖本次 AppID；源码不硬编码业务 AppID |
| `ZENTS_WECHAT_OUTPUT` | 覆盖 SDK 输出目录 |
| `ZENTS_NODE` | 可选，自定义 SDK 的 Node 路径 |
| `ZENTS_WECHAT_ACCEPTANCE=1` | 验收模式：Development、关闭性能分析、资源放入分包，无需额外 CDN |

Unity 批处理使用 `-buildTarget WebGL -executeMethod ZenTS.Editor.WeChatBuild.RunBatch`。导出后在微信工具打开 `<输出目录>/minigame`。团结请使用对应的微信平台参数。

## 宿主适配与限制

- 标准库与 ES 模块都在构建期生成静态工厂；ZenTS HostJs 初始化不再使用 `eval`、`new Function` 或 `with`。
- Loader 仍选择模块源码与 Reset 版本；能够执行的版本必须预先进入构建。运行时任意新增源码、dynamic import 与 import.meta 不属于当前支持范围。
- HostJs 使用 WeakRef / FinalizationRegistry 管理 JS 与 CLR 包装对象。缺失这些能力时明确报错，不使用持有强引用的假弱引用实现。
- 无 TextDecoder 时提供等价 UTF-8 解码路径，包含内嵌 NUL、BOM、非 BMP 字符与非法字节替换。
- 支持微信 SDK 的 IL2CPP OptimizeSize：wasm 委托适配完全共享泛型的按地址参数/返回值。GTAP 通过实际 invoker 判断 AOT 可调用性，不因缺少直接调用桩误报。
- 团结目标在项目 Assets 生成微信专用桥接 .jslib，不修改只读包内的导入设置。

## 自动验收

Windows 上运行：

```text
node <ZenTS包>/ZenTS~/wechat/run-wechat.mjs <Unity工程> <微信工具>/cli.bat
```

脚本复制正式 `Build-WeChat/minigame` 到 `Library/ZenTS/WeChatAcceptance`，仅在副本中加入观测入口。正式产物及测试期望不修改。原始 wasm、框架和资源文件保持不变。

每轮开始会关闭上一轮脚本管理的窗口，只保留本轮项目。日志通过带独立标识的路由发送到本机 `127.0.0.1:8786`，保存为项目根 `wechat-player.log`；不发送到外部服务。收到结束信号或连接失败即停止采集，不持续重试。正式小游戏不包含此采集脚本。

断言包括：实际 HostJs 桥接与 wasm 回调、JS 矩阵 56/56、Smoke 10/10、TS 3/3，且无未捕获异常或测试失败。托管工作线程细分用例沿用 WebGL 的平台排除，其他互操作测试没有新增跳过。

独立受限环境验证：

```text
node <ZenTS包>/ZenTS~/host-js/verify-restricted-host.mjs <Unity工程>
```

该验证禁止动态代码生成并移除 TextDecoder，检查实际标准库工厂、UTF-8/NUL/BOM/非法字节和 Reset。

本轮记录见 [微信适配验收](../migration/P6-WECHAT.md)。这次没有执行上传、发布或真机扫码调试。
