# P2 — V8 / Windows Editor 与 Win64

本轮在 `ZenTSTest/Packages/com.code-philosophy.zen-ts` 实施；主仓库未同步。
交付范围是 Unity 2022.3.62f3 Windows x64 Editor 和既有 Win64 导出工程。

## 实现

- Native CLR 内核继续消费 ZenTS 自有 EngineApi；V8 实现位于 `engine/v8/V8Engine.cpp`。
- V8 使用固定预编译静态 SDK，ZenTS 适配器也预编译为静态库；Unity 不编译 V8 源码，不要求整个 Il2Cpp 工程升级到 C++20。
- Win64 Player 和 Editor 插件统一链接 /MT 官方 SDK 与适配器。Editor 以指针输入/输出 C ABI 服务 Mono，内存分配/释放留在原生模块内部。
- 对象身份使用稳定 slot 与 identity-hash 桶，碰撞通过严格相等判断；Global/weak handles、引用计数与 finalizer 协作。
- native function 捕获值保存在 JS 对象边上，避免因 C++ persistent 捕获形成不可回收引用环。
- 通过 TryCatch 保存异常，托管异常不会直接穿过反向 P/Invoke；保留已有错误文案、构造权限和回调名字。
- 模块编译、静态导入、同步 GetFunction、微任务沿用既有行为；帧泵额外处理 V8 前台队列，包括 FinalizationRegistry 清理。
- 销毁 isolate 前 NotifyIsolateShutdown 取消平台任务；Domain Reload、Editor 退出和 Play Mode 切换清理旧运行时。

## SDK 与构建

版本为 V8 13.6.233.17，直接从官方仓库/官方镜像检出提交 `b0a55a7dad7f536cce1f9aaddba89894c8533946` 自行构建 `v8_monolith`。
**不使用第三方二进制 SDK，也不应用任何上游源码补丁。**
固定来源和构建参数见 `../v8/official-source-lock.json`；源码/依赖洁净性、实际依赖提交、工具链与头文件/静态库摘要见 `../v8/sdk-lock.json`。
产物摘要在 `../v8/win64-build.json`；许可证保留于 `../v8/licenses`。

Unity Debug 默认 CRT 与 Release SDK 不匹配。构建处理器使用 MSVC `_CL_=/MT /U_DEBUG` 选择一致 CRT，保留 Debug 优化配置与符号；不屏蔽 RuntimeLibrary mismatch。
额外参数包含缓存标记和显式静态库依赖，Bee 能跟踪 SDK/适配库变更。
构建处理器在完成、失败或程序集重载时恢复原有环境与 AdditionalIl2CppArgs。
导出 VS 项目的 NMake 命令保存相同设置；普通 MSBuild 不需要临时手工设置环境。

官方参数依据：https://learn.microsoft.com/en-us/cpp/build/reference/cl-environment-variables

## 验收

| 环境 | Smoke | JS 套件 | TypeScript 示例 |
|---|---:|---:|---:|
| Windows Editor / Mono / V8 | 8/8 | 55/55 | 3/3 |
| Win64 / Il2Cpp / 静态 V8 | 10/10 | 55/55 | 3/3 |

另行验收：

- 自有引擎接口的 C++17 独立编译；CLR 内核无引擎 SDK 依赖。
- 真正静态链接的原生验收：回调、错误、模块、微任务、移动 GC 身份、FinalizationRegistry、finalizer、三次运行时重建。
- 真实 Editor Domain Reload，原生 runtime 数量 `1 → 0 → 1`。
- Install 与构建环境配置/恢复。

既有测试预期未修改，也未增加平台跳过。Win64 复用了已有导出工程和生成 C#，没有重新 Generate / 导出；该导出工程执行的矩阵为 55 套。

## 复现

1. 准备 `official-source-lock.json` 固定的官方 V8 checkout、depot_tools 与 DEPS 依赖。
2. 执行 `python ZenTS~/v8/build_official.py --source <checkout> --depot-tools <depot_tools> --out <外部输出> --export <全新 SDK 目录> --jobs 4`。
3. 执行 `python ZenTS~/v8/build_win64.py --sdk <SDK 目录>`；只重建适配器时可省略 --sdk。
4. Unity 执行 `ZenTS/Install`。本测试工程可通过 `ZenTS.Editor.V8Acceptance.InstallAndConfigureWin64` 同时配置已有导出工程。
5. Unity batchmode 执行 `ZenTS.Editor.V8Acceptance.RunEditorTests`。
6. Unity batchmode 执行 `ZenTS.Editor.V8Acceptance.RunDomainReloadTest`。
7. MSBuild 既有 `Build-Win64/ZLuaDemo.sln`，Debug/x64；运行 `build/bin/x64/Debug/ZLuaDemo.exe -batchmode -nographics -logFile <日志>`。
8. `verify-boundaries.py`、`verify-contract.py` 继续可用；`engine-smoke.cpp` 对应独立 V8 引擎验收。

本机官方源码、构建输出和导出 SDK 位于 `D:/Temp/zents-v8-official`。旧 SDK、第三方构建脚本及旧验收记录已移到外部备份，不参与当前构建。
官方 GN 生成头的搜索位置在构建输出内补齐，所有上游受控文件仍保持洁净。

## 后续范围

P3 的 ZLua 行为对齐、显式 BigInt 与 XML 限制不属于本轮后端切换，本轮保持已有测试语义。
macOS/iOS/Android 的静态 SDK/链接流程及 WebGL/小游戏宿主 VM 尚未验收；本记录不宣称这些平台已完成。

## 官方来源修正

早期 P2 曾使用第三方预编译产物；该结果不能作为当前官方源码构建的验收依据。
本次从官方源码重新构建库、适配器与插件，并重新执行 Editor、Win64、Domain Reload 和原生 GC/模块用例。
SDK 导入和 Unity 安装现在检查官方来源与洁净性；旧发布包导入入口已移除。
`verify-p2.py` 会拒绝旧来源记录、旧库残留及未显式链接官方静态库的构建。

运行时仅保留 V8 和独立 HostJs 接口。Windows x64 之外的构建目前明确阻止，待相应 SDK/后端接入后开放。
