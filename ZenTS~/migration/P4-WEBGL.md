# P4：WebGL 使用平台内嵌 JS VM

代码位于 ZenTSTest 的嵌入 package，没有同步主仓库。平台：Unity 2022.3.62f3、Windows 主机、Chrome WebGL；Editor / Win64 继续使用官方静态 V8。

## 实现

- 在 EngineApi 下实现 HostEngine.cpp 和 HostEngine.jslib，复用 P3 的 CLR 计划、内部调用、委托、封送与注册表；wasm 不链接 V8 SDK。
- 构建期 esbuild 生成同步模块工厂，提前合成 csharp: 模块。保留加载器选源码和 Reset 替换版本的语义。
- 字符串双向转换使用显式长度，保留 Unicode 和内嵌零字符；异常状态独立于异常值，覆盖 throw undefined。
- wasm32 专用结构体委托参数按地址传递；小整数返回做符号/零扩展，IntPtr/UIntPtr 根据实际指针宽度选择委托形状。BigInt 范围解析兼容 Unity 默认 C++11。
- 宿主对象有稳定身份和独立代；WeakRef / FinalizationRegistry 与 Unity 帧泵共同回收 CLR 根。CLR 和 JS 的异常双向传播。
- 移除旧的 HostJs JSON/ccall 桩实现，安装器清理旧安装残留。WebGL 构建明确要求完整异常处理；Native 构建不再清除其他目标的 HostJs define。

## 实测

| 项目 | 结果 |
|---|---|
| Chrome WebGL JS 矩阵 | 56/56 套件 |
| Chrome WebGL Smoke | 10/10 |
| Chrome WebGL TS demo | 3/3 |
| Editor V8 | Smoke 8/8、JS 56/56、TS 3/3 |
| Win64 静态 V8 | Smoke 10/10、JS 56/56、TS 3/3 |
| Editor Domain Reload | 运行时数量 1→0→1 |
| 宿主 GC 压力 | 128 个新增包装对象回收到基线 |
| Reset 生命周期 | 一个活动上下文，旧代句柄 0，待清理队列 0 |

WebGL 明确排除 1 个细分用例：托管工作线程访问测试（浏览器没有托管 Thread）。它在测试开头返回，并记录 `[P4][SKIP]`。没有跳过重载、委托、泛型、BigInt、数组、Opaque 或 Reset。33 个对齐/边界专项中其余 32 项适用；整套矩阵仍逐模块执行。

Chrome 驱动通过 CDP 收集真实运行日志并强制 GC。HTTP 测试服务仅捕获 Unity 实例供 GC 诊断，不修改构建 JS/wasm 或测试期望。Win64 继续复用已有导出工程做原生增量编译。

入口：`ZenTS.Editor.P4Acceptance.BuildWebGL`；构建输出 `ZenTSTest/Build-WebGL`。本机执行：

```powershell
node ZenTS~/host-js/run-webgl.mjs D:/workspace/zts/ZenTSTest
python ZenTS~/migration/verify-p4.py
```

日志、SDK/桥接源码和构建产物哈希见 p4-verification.json。微信小游戏、其他浏览器/设备和 macOS/iOS/Android 不属于本轮运行验收；未预编译的 ESM 不能在运行时临时注入。正常发布请根据应用实际模块目录维护构建输入，当前自动收集标准项目布局。
