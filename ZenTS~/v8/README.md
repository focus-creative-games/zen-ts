# 官方 V8 SDK：构建与 Unity 安装

ZenTS 使用固定版本的官方 V8 源码，独立编译静态库。包目录只包含源码、版本锁与工具；Git URL 安装后的 `Library/PackageCache` 不需要写权限，也不需要嵌入 `Packages`。

## 文件位置

| 内容 | 位置 |
| --- | --- |
| V8 源码、GN/Ninja 输出 | 开发者指定的包外目录 |
| SDK 不可变快照 | `<Unity项目>/Library/ZenTS/NativeSDK/<指纹>/<target>` |
| 已选 SDK 目录索引 | `Library/ZenTS/NativeSDK/installed.json` |
| 本地构建工作目录（Windows 默认） | `Library/ZenTS/NativeBuild/windows-x64` |
| Editor 插件及自动生成的元数据 | `Assets/ZenTSGenerated/Native/<target>/` |
| IL2CPP 补丁、生成代码及链接配置 | `Library/ZenTS/LocalIl2CppData-<Editor平台>/` |

SDK 中包含 `official-sdk.json`、`installation.json`、头文件、许可证、静态库，以及适用平台的 Editor 插件。安装会校验固定提交、来源、ABI、平台、源码身份和文件 SHA-256；文件损坏或适配器源码不匹配时必须重新构建。
源码身份忽略 CRLF/LF 差异；二进制与 SDK 内容仍逐字节校验。

## Windows

先按官方流程准备 V8 checkout 和 DEPS：<https://v8.dev/docs/source-code>。
版本为 `13.6.233.17`，提交 `b0a55a7dad7f536cce1f9aaddba89894c8533946`；完整锁定配置见 `official-source-lock.json`。上游源码不应用补丁。

下列 `<工具目录>` 指解析后的 package 中 `ZenTS~/v8`，可通过 `PackageInfo.resolvedPath` 获取；不要硬编码 PackageCache 哈希目录。

```text
python <工具目录>/build_official.py --source <官方V8源码> --depot-tools <depot_tools> --out <包外GN输出> --export <包外原始SDK> --jobs 4
python <工具目录>/build_win64.py --project <Unity项目> --sdk <包外原始SDK>
python <工具目录>/import_platform.py --project <Unity项目> --sdk <Unity项目>/Library/ZenTS/NativeBuild/windows-x64/sdk
```

第二步生成静态适配库和内部静态链接 V8 的 Editor DLL；第三步安装完整 SDK 到 Library。已有本地构建 SDK 时，`build_win64.py --project <项目>` 可以重建适配层，无需重编 V8。
也可在 Unity 菜单 `ZenTS/Native SDK/Import SDK...` 选择完整 SDK。

## macOS / iOS / Android

```text
python <工具目录>/build_platform.py --package <解析后的包目录> --source <官方V8源码> --out <包外GN输出> --export <包外完整SDK> --target <macos|ios|ios-simulator|android> --cpu <arm64|x64>
python <工具目录>/import_platform.py --project <Unity项目> --sdk <包外完整SDK>
```

Android 构建在 Linux 执行，额外提供 `--ndk <NDK路径>`。iOS 使用 JITless 配置；Unity 2022 的模拟器验收目标为 x64。macOS SDK 同时编译 MonoHook 辅助插件，安装时与 V8 Editor 插件一起生成。具体构建要求见 [P5 平台说明](../migration/P5-NATIVE-PLATFORMS.md)；其中历史产物位置以本文为准。

## 自动生成与版本管理

Unity 启动、CLI 导入后的目录索引变化、进入 Play Mode 和构建前都会检查当前 Editor 所需插件。二进制和 `.meta` 成对生成；GUID 按平台及文件名确定，重建 SDK、删除生成目录、重新克隆工程后都保持稳定。插件只允许对应 OS/CPU 的 Editor 使用，不会作为动态插件混入 Player。

已加载的 DLL/dylib 不允许覆盖。导入不同 SDK 后若显示需要重启，关闭再打开 Unity 即可激活；原文件会保留。`ZenTS/Native SDK/Repair Editor Plugins` 可以手动修复缺失文件。

项目 `.gitignore` 建议增加：

```gitignore
/Assets/ZenTSGenerated.meta
/Assets/ZenTSGenerated/Native/
/Assets/ZenTSGenerated/Native.meta
```

Library 已由 Unity 常规忽略。原生构建产物与生成的 Native 元数据不提交 Git；源码文件的 `.meta` 正常提交。团队可以在 CI 构建 SDK 后分发完整 SDK 文件夹，每个项目导入到自己的 Library。删除 Library 后需要重新导入 SDK；Assets 中保留的旧 DLL 不会绕过 SDK 校验。

Win64 Player 静态链接 Library 中的库；导出的 VS 工程会带上所需静态库，可脱离原工程增量编译。macOS/Android 链接 Library 的静态库；iOS 导出时复制到 Xcode 工程。WebGL/小游戏使用平台宿主 JS VM，不链接 V8 SDK；在原生 Editor 中调试仍需要对应 Editor SDK。
