# 只读 UPM 安装与原生 SDK 本地缓存

本次实现直接修改 `ZenTSTest/Packages/com.code-philosophy.zen-ts`，无需将 Git URL 安装改为嵌入包。当前用法见 [SDK 构建与安装](../v8/README.md)。

## 实现

- `NativeSdkStore` 与 Python `native_sdk` 共用 SDK 目录契约：固定 V8 提交、SDK ABI、目标平台、内容指纹、逐文件 SHA-256 和适配器源码身份。
- SDK 通过暂存目录和原子切换安装到项目 `Library/ZenTS/NativeSDK`，索引更新使用跨进程锁。Windows 与 macOS Editor 插件由缓存 SDK 提供。
- `NativeEditorPlugins` 将当前宿主 Editor 的 DLL/dylib 及 PluginImporter 元数据生成到 `Assets/ZenTSGenerated/Native`，配置为 Editor-only 并限定 OS/CPU；GUID 按逻辑路径确定，SDK 更新和修复不会改变 GUID。
- 插件替换前检查全部候选文件。若原生库已经加载，保留旧文件并明确要求重启。缺失 SDK、无效 ABI、损坏文件不能通过现有 Assets 二进制绕过校验。
- macOS MonoHook 辅助库一起构建和生成。批处理构建在设置 `UNITY_IL2CPP_PATH` 前显式确保 Hook 安装，避免 delayCall 尚未执行时使用错误的开发源码布局。
- Win64/macOS/Android 从 Library 链接静态库。Win64 导出工程复制所需静态库；iOS 导出工程复制库和来源摘要。WebGL 保持使用宿主 VM。
- 构建脚本拒绝把源码 checkout、GN 输出或 SDK 导出目录放入包内；P5 验收器从 Library 解析 SDK，并默认把验收报告写入 Library。

## 只读场景验收

使用独立临时 Git 仓库作为 UPM Git URL 依赖，并通过 Unity 正常解析到 `Library/PackageCache`，不是复制嵌入包后伪装路径。

1. 对解析后的目录施加 Windows 读取/执行 ACL，确认创建新文件和打开既有源码进行写入均被拒绝。
2. 在受限状态运行 `ReadOnlyAcceptance.Run`：确认 `PackageSource.Git`、真实 PackageCache 路径、自动插件和 `.meta`、Editor-only 导入器、重复安装幂等、Install、XML 生成及完整 Editor 测试。
3. 删除生成的 Native 目录与其元数据，重新启动 Unity；完整测试再次通过，插件 GUID 不变。
4. 直接执行受限包内 `build_win64.py`，构建输出全部写入项目 Library；通过包内 CLI 安装重新构建的 SDK，再运行完整 Editor 测试。
5. 对测试包 472 个文件进行前后 SHA-256 比较，内容全部不变。测试结束恢复临时 ACL。
6. `RunSdkSafetyTests` 实际加载 V8 后，验证错误 ABI、文件损坏不修改 SDK 索引；不同插件版本导入后必须重启，已加载 DLL 字节保持原样。

## 回归结果

| 环境 | JS | Smoke | TS |
| --- | --- | --- | --- |
| Windows Editor、macOS ARM64 Editor | 56/56 | 8/8 | 3/3 |
| Win64 Player | 56/56 | 10/10 | 3/3 |
| macOS ARM64、x64 Player | 56/56 | 10/10 | 3/3 |
| Android x64、ARM64（MuMu） | 56/56 | 10/10 | 3/3 |
| WebGL（Chrome 宿主 VM） | 56/56 | 10/10 | 3/3 |
| iOS x64 模拟器 | 56/56 | 10/10 | 3/3 |

Windows/macOS Domain Reload 均验证原生 runtime 数量 `1 → 0 → 1`。WebGL 的真实浏览器 GC、Reset、异常和 UTF-8 边界验收通过。iOS ARM64 设备版本 Xcode 无签名编译通过，没有真机运行验证。

macOS x64 Player 使用 M1 Rosetta；Android ARM64 使用 MuMu 转译。远程 Unity 2022.3.56f1c1/macOS 26 在 Domain Reload 断言通过后仍可出现既有 Curl 退出停滞，保留采样后结束验收进程。微信开发者工具及团结引擎未在这次安装改造中重新验收。

证据摘要和日志 SHA-256 见 [readonly-upm-verification.json](readonly-upm-verification.json)。完整日志保留在测试工程 `readonly-evidence`，不进入版本管理。P5 原有边界、契约、平台运行日志、SDK 来源/哈希和 Android ELF 架构验收器也已通过。
