# ZenTS HostJs：WebGL 平台 VM 后端

P4 已实现并在 Unity 2022.3.62f3 + Chrome WebGL 中验收。wasm 不嵌入或链接 V8；用户 JS 直接运行在浏览器 JS VM 中。微信小游戏仍需后续平台打包与真机验收。

## 架构

```text
JS 模块 / zentslib.js（浏览器 VM）
  ↕ 16 字节 Value + 同步 wasm 调用
Plugins/WebGL/HostEngine.jslib
  ↕ EngineApi
engine/host/HostEngine.cpp
  ↕ 共享 CLR 计划 / MethodBridge / Marshal / Registry
IL2CPP / Unity / C#
```

HostJs 与原生 V8 使用同一个 EngineApi 和 P3 CLR 内核。GetFunction、重载、泛型、ref/out、BigInt、Table、Bytes、声明/实际门面、Opaque 以及 Reset 不再有第二套 JSON dispatcher。旧 M0/M1 HostDispatch、HostRegistry、HostInternalCalls 和 zents_host.js 原型已移除。

## wasm 边界

- HostOperation 是 JS 实现的同步导入；Value 为 ZenTS 自有 16 字节表示，不是 V8 SDK 的值类型。数值直接写入 wasm 内存；对象/字符串/BigInt 使用宿主句柄，无 JSON 精度丢失。
- zents_host_callback 从 JS 调回共享 C++ 方法入口；CLR 异常转换为 JS 异常，JS 异常按共享边界回传 C#。
- JS WeakMap 提供稳定对象身份，引用计数根仅在 C++ 持有值时保持；无根对象使用 WeakRef。
- FinalizationRegistry 仅入队。Unity 帧泵经 IsJobPending/ExecutePendingJob 同步回收 CLR 根，避免在异步 GC 回调里重入 IL2CPP。
- Reset 清空当前模块缓存和句柄、使旧函数失效，并释放原生 callback；代标识和句柄 ID 不复用。

## 模块与加载器

HostJsModulesBuild 在 TS 发布及测试 Resources 同步后运行。它收集发布 JS，以项目的 esbuild 在构建期转成同步模块工厂，并合成用到的 csharp: 模块。生成物为 Assets/ZenTSHostGenerated/ZenTSModules.jslib。

运行时仍尊重 IJsRuntime 的 moduleLoader：按当前加载器返回的源码选择预编译工厂；因此 Reset 更换加载器/模块版本仍有效。源码换行规范化为 LF。任意新源码必须先加入构建输入并重新构建，不能在 Player 中临时引入未编译 ESM；不会在浏览器异步加载模块后假装同步 GetFunction 已完成。

默认收集项目 TS 输出、StreamingAssets/ZenTS、测试 JS 和对应 Resources；项目工具链路径由 TsProjectPaths 提供。全局桥接脚本仍使用宿主的 JS 执行能力，不包含任何额外解释器。

## 构建与要求

- Unity 自动选择 UNITY_WEBGL / ZENTS_HOST_JS；Editor 始终保留原生 V8。
- WebGL 开启 Full exception support（可关闭错误栈），否则构建阶段明确报错。
- 浏览器需要 WebAssembly、BigInt、WeakRef、FinalizationRegistry。
- 当前验收为单主线程 WebGL；托管 Thread 测试在用例开头明确返回并记录 SKIP。
- 运行 P4Acceptance.BuildWebGL 生成测试工程的 Build-WebGL；运行 host-js/run-webgl.mjs 启动本机 Chrome 验收。

完整结果和范围见 ../migration/P4-WEBGL.md。原生 V8 后端保留，未以 HostJs 替换 Editor 或 Win64。
