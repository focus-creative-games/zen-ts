# P6：微信小游戏 HostJs

本轮直接修改 ZenTSTest 的 package，没有同步到另一个 zen-ts checkout，也没有提交 Git。

## 完成的实现

- 可选集成微信官方 Unity/团结 SDK，提供菜单和 CI 构建入口；配置、模板和转换输出都在 package 外。
- WebGL 与团结 WeixinMiniGame 共用 HostJs 模块、Resources 发布和标准库工厂，保留 Editor V8 后端。
- 去除 HostJs 标准库运行时动态编译，提供无 TextDecoder 的 UTF-8 解码。
- 修复实际微信 Size 优化下的完全共享泛型委托 ABI，以及 GTAP 对无直接调用桩方法的错误拒绝。
- 提供可复现的微信工具验收副本和仅发往本机的日志收集器；单窗口管理，结束后停止采集。

## 实际测试

| 环境 | JS 矩阵 | Smoke | TS |
| --- | --- | --- | --- |
| 微信开发者工具，平台宿主 VM，OptimizeSize | 56/56 | 10/10 | 3/3 |
| Windows Editor，V8 | 56/56 | 8/8 | 3/3 |
| Win64 IL2CPP，静态 V8 | 56/56 | 10/10 | 3/3 |
| Chrome WebGL，平台宿主 VM | 56/56 | 10/10 | 3/3 |

Chrome 的实际 GC、Reset 生命周期、异常和 UTF-8 边界检查通过。受限宿主测试在禁用动态代码生成、移除 TextDecoder 后通过。微信完整运行中确认 WeakRef、FinalizationRegistry、Proxy 和 BigInt 可用，并观察到实际 HostJs 桥接及 wasm 回调。

没有为了微信跳过重载、委托、泛型、BigInt、Opaque、数组或 Reset；托管工作线程的既有 WebGL 排除扩展到微信目标。

Unity：2022.3.62f3；微信工具：Stable 2.01.2510260；基础库：3.17.6；官方 SDK：202610200817，提交 915d4bf4f60ef0cd1c29316a8d67257185d609a7。

团结目标的代码分支已接入，但本轮没有团结 Editor 或手机真机运行验证；没有进行上传或发布。支持范围遵循 Unity 2022.3 LTS 及之后的 LTS / 基于 2022.3 LTS 的团结，不为旧 Unity 或非 LTS 新增兼容。

构建方法见 [微信接入说明](../wechat/README.md)；运行证据和源文件摘要见 `p6-verification.json`。
