# P5：macOS / iOS / Android 原生 V8

> 本文记录 P5 原始集成及验收。SDK 和插件的位置已迁移至 Library / Assets，当前构建安装流程见 [SDK 安装说明](../v8/README.md) 和 [只读 UPM 验收](READONLY-UPM.md)。`verify-p5.py --project <项目>` 从 Library 校验 SDK，默认将报告写入 Library。

本阶段直接修改 ZenTSTest 内嵌 package。原生平台静态链接官方 V8；Editor 插件只负责暴露 ZenTS 的 C ABI。WebGL 继续使用平台 JS VM。

## SDK 与构建边界

固定 V8 13.6.233.17、提交 `b0a55a7dad7f536cce1f9aaddba89894c8533946`，来源和 DEPS 摘要沿用 `../v8/official-source-lock.json`。不修改上游源码或 GN 配置文件。

- Windows 沿用 `engine/v8/sdk`，使用 `build_official.py` / `build_win64.py`。
- 新平台 SDK 放在 `engine/v8/sdks/<target>`，分别携带 `include`、`lib`、许可证与 `official-sdk.json`。
- 每个 SDK 提供 `libv8_monolith.a` 和 `libzents_v8_engine.a`。Unity 编译 ZenTS / libil2cpp 时只依赖 `EngineApi.h`，不编译 V8 源码，不要求将 Unity 的 C++ 标准提高到 C++20。
- macOS SDK 另外导出静态链接 V8 的 `plugin/libzents_v8.dylib`。导入后位于 `Plugins/v8/macos-arm64` 或 `macos-x64`，只对匹配架构的 macOS Editor 启用。
- iOS 真机与模拟器分别构建 JITless SDK，关闭 TurboFan、Sparkplug、WebAssembly。初始化时也设置 `--jitless`（WebAssembly 已在 GN 配置中禁用）。
- 当前 Unity 2022.3 iOS 模块的模拟器 `baselib.a` 与 `libiPhone-lib.dylib` 为 x86_64，因此选择 `ios-simulator-x64`；真机选择 `ios-arm64`。SDK、Unity 库与 Xcode 的 ARCHS 必须一致。
- Android 提供 `android-x64`、`android-arm64`。该固定版本的上游 GN 要求在 Linux 主机上构建 Android；使用对应 DEPS 的 Clang/GN/Ninja 和 r23b NDK sysroot，SDK 使用 API 21 基线，以兼容 Unity 2022 的 API 22 原生链接；验收 APK 的 minSdk 为 23。V8 和适配器统一关闭相对虚表 ABI，使用官方 DEPS 的 `__Cr` 命名空间 libc++。其对象并入 ZenTS 静态适配库，未版本化的异常基础类型仍使用 NDK 实现，由 `AndroidLibcxxBridge.cpp` 转接字符串构造函数。

## 构建与导入

先按官方 V8 流程准备固定提交和目标平台的 DEPS 依赖。Android 还需要 `checkout_android` 条件下的依赖，不能只复用 Windows checkout 而省略它们。

```text
python ZenTS~/v8/build_platform.py --source <official-v8> --out <external-out> --export <sdk-export> --package <package-root> --target macos --cpu arm64 --jobs 3
python ZenTS~/v8/build_platform.py --source <official-v8> --out <external-out> --export <sdk-export> --package <package-root> --target ios --cpu arm64 --jobs 3
python ZenTS~/v8/build_platform.py --source <official-v8> --out <external-out> --export <sdk-export> --package <package-root> --target ios-simulator --cpu x64 --jobs 3
python ZenTS~/v8/build_platform.py --source <official-v8> --out <external-out> --export <sdk-export> --package <package-root> --target android --cpu x64 --ndk <r23b-ndk> --jobs 6
python ZenTS~/v8/import_platform.py --sdk <sdk-export>
```

`import_platform.py` 校验官方来源、固定提交、JITless 约束及全部头文件/静态库摘要，再导入 package。构建脚本在输出目录中处理生成的 `v8-gn.h` 搜索路径，不改上游文件；跨平台迁移 checkout 后按实际内容比较 HEAD，避免 Windows 索引时间戳造成假阳性。

修改适配器时可追加 `--adapter-only`，复用并校验原始 V8 库、头文件和 GN 配置；Android 还需要保留 libc++ 对象缓存。该模式不会重新编译 V8。

导入后执行 `ZenTS/Install`。构建时也会按 manifest 自动同步当前目标 SDK，避免继续链接旧缓存。macOS / Android 构建处理器选择目标 SDK，将两份静态库添加到 Bee 的显式链接依赖；构建结束、失败或退出后恢复临时 AdditionalIl2CppArgs；切换目标时清理 ZenTS SDK 遗留参数，保留用户自己的构建参数。iOS 后处理将 SDK 复制到 Xcode 工程的 `Libraries/ZenTSV8`，加入 UnityFramework 的链接输入和搜索路径。

Android 目前每次构建选择一个 ABI；多 ABI APK 必须等多目标静态库选择流程实现后再启用，当前会明确拒绝混合配置。macOS universal 构建需要独立的 universal SDK，不能把单架构库冒充 universal 库。

## 验收入口

`ZenTS.Editor.P5Acceptance` 提供 `BuildMacArm64`、`BuildMacX64`、`BuildAndroidX64`、`BuildAndroidArm64`、`ExportIosSimulator`、`ExportIosDevice`。批处理启动 Unity 时先切换对应 `-buildTarget`，以加载平台 Editor API。

Editor 使用 `ZenTS.Editor.V8Acceptance.RunEditorTests` 和 `RunDomainReloadTest`。Player 使用现有测试场景：56 个 JS suite、10 个 IL2CPP smoke、3 个 TypeScript 示例；不能用构建成功替代运行测试通过。

## 最终结果

| 验证目标 | JS suite | Smoke | TypeScript 示例 |
| --- | --- | --- | --- |
| Windows x64 Editor | 56/56 | 8/8 | 3/3 |
| macOS arm64 Editor | 56/56 | 8/8 | 3/3 |
| Win64 Player | 56/56 | 10/10 | 3/3 |
| macOS arm64 / x64 Player | 各 56/56 | 各 10/10 | 各 3/3 |
| iOS x86_64 模拟器 | 56/56 | 10/10 | 3/3 |
| Android x86_64 / arm64（MuMu） | 各 56/56 | 各 10/10 | 各 3/3 |
| WebGL 宿主 VM | 56/56 | 10/10 | 3/3 |

两端 Editor 的 Domain Reload 运行时计数断言均通过 `1 → 0 → 1`。WebGL 保留其平台本身不支持的 managed Thread 用例早返回，未为原生平台新增跳过项。iOS arm64 真机工程完成无签名编译；未连接物理设备，因此不声称通过真机运行测试。macOS x64 在 M1 上通过 Rosetta 执行，Android arm64 在 MuMu 的原生桥接环境执行。

macOS 的 Node/npm 子进程会在现有 PATH 后追加 Homebrew 常用目录；已在只有系统 PATH 的 Unity 批处理中通过 TypeScript 检查，不依赖从终端启动 Unity。

已知环境边界：远程机的 Unity 2022.3.56f1c1 在 macOS 26 上，Domain Reload 断言通过后退出可能卡在 Unity Curl Job。采样已定位到宿主 Job/Curl 关闭路径，验收进程在断言完成后终止；这不算作正常退出通过。日志采样保存在测试工程的 `p5-macos-editor-quit-sample.txt`。Intel macOS Editor、物理 iOS/Android 设备及团结引擎未在本环境验收。

机器可校验记录见 [p5-verification.json](p5-verification.json)，执行 `python ZenTS~/migration/verify-p5.py` 可重新检查运行日志、SDK 摘要、APK 原生架构与边界契约。
