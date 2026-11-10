# Editor V8 Inspector

内置调试器使用官方 V8 Inspector，直接关联 ZenTS Editor 已有的 isolate/context。默认关闭，Windows 与 macOS 共用同一套原生 Inspector 和托管传输实现。

## 开启

1. 使用当前 package 重建并导入 Native SDK 适配层。已有官方 V8 静态库时只需重编适配层，不必重编 V8。构建/导入命令见 [SDK 说明](../v8/README.md)。SDK 位于 `Library`，Editor 插件和稳定 GUID 的 `.meta` 自动生成到 `Assets/ZenTSGenerated/Native`，兼容只读 PackageCache。
2. 如果 Unity 已加载旧 DLL/dylib，重启 Editor 激活新插件；不会覆盖已加载的模块。
3. 在 Project Settings 的 ZenTS 页面找到 **V8 Inspector (Editor)**，勾选 **Enable JS debugger**。保留 **Host override** 为空，默认端口 **9230**。
4. 初始化脚本域（例如进入 Play）。Console 会输出 `Listening on ws://127.0.0.1:9230/<target-id>`。仅开启设置不会立即创建脚本域或监听端口；设置在下次脚本域初始化时生效。

默认不等待调试器，支持运行中附加。需要捕获业务模块第一行时勾选 **Wait for attach**：主线程会等待客户端发送 `Runtime.runIfWaitingForDebugger`。断点暂停同样会暂停 Unity 主线程，调试消息仍可处理，游戏与 Editor update 不会在暂停循环里重入。

## 连接

### Chrome DevTools

打开 `chrome://inspect/#devices`，在 Configure 中添加 `127.0.0.1:9230`，找到 **ZenTS Editor** 目标后选择 inspect。也可访问 `http://127.0.0.1:9230/json/list`，使用其中的 `devtoolsFrontendUrl` 或 `webSocketDebuggerUrl`。

### VS Code

将 [launch.example.json](launch.example.json) 中的配置合并到 Unity 项目的 `.vscode/launch.json`。工作区根目录应为 Unity 项目根目录；更改调试端口时同步更改配置里的端口和 source-map URL 前缀。该配置使用 Inspector attach 客户端，运行时仍是 ZenTS 自己的 V8。

配置字段说明参考 [VS Code 官方 Inspector attach 与 source maps 文档](https://code.visualstudio.com/docs/nodejs/nodejs-debugging)。本轮自动验收使用真实 CDP 客户端，不包含 VS Code 图形界面验收。

## TypeScript 与源文件

现有 TypeScript emit 流程生成 `TsProject/out/**/*.js` 和 `.js.map`。Inspector 对 canonical 路径进行 UTF-8 URL 转义（包括中文和空格），映射到 `http://127.0.0.1:<port>/source/out/<module>.js`，调试服务在同一端口提供 `TsProject` 内的 JS、TS 和 map 文件。source map 的相对路径据此解析；不需要把源码复制进 package。

自定义 loader 产生的虚拟模块仍可按 Inspector URL 断点，并通过 `Debugger.getScriptSource` 查看实际加载内容。额外源码目录可通过客户端路径映射或自定义 `IZentsJsDebuggerHost` 处理；设置中的 Extra source paths 提供给自定义 host，内置 HTTP 源码服务限定在项目 `TsProject` 内。

## 已实现

- 标准 Inspector HTTP 发现与 WebSocket 消息通道，默认端口 9230，仅监听 IPv4 loopback，单个客户端。
- JS 断点、TS source-map 定位、单步、局部变量/表达式、异常暂停，以及 JS→C#→JS 回调中的暂停。
- 协议 I/O 在后台线程执行；通过官方 `RequestInterrupt` 唤醒 V8 所属线程处理协议，可暂停 CPU 密集循环。
- 求值异常不污染后续脚本调用，包括监视表达式调用 CLR 后发生参数错误。
- Reset、结束 Play、Domain Reload 与退出 Editor 复用脚本域关闭钩子，关闭连接、停止线程、释放 Inspector；每代使用新的目标 ID，Reset 后客户端需重新连接。
- 暂停时客户端断开会恢复执行。正在派发的会话保留到调用返回，旧连接响应不会发往新连接，包含 Inspector 自身 `Runtime.evaluate` 暂停期间断开的情形。
- WebSocket 分片文本、ping/pong、关闭帧、UTF-8 校验和有界队列；原生回调不会向 C++ 栈展开托管异常。

自定义调试器 host 接口继续保留。WebGL/小游戏继续使用平台自带开发工具；本次 Inspector 集成属于 Editor 原生插件，不给 wasm 或 Player 增加调试监听服务。

## 验收

```text
node <resolved-package>/ZenTS~/debugger/run-inspector.mjs <Unity-project> [Unity-executable]
```

脚本默认在本机启动两个先后执行的无界面 Unity Editor 进程（验收限制为一个 Job Worker），使用实际 HTTP/WebSocket/CDP 验证映射断点、单步、变量、Unicode、监视求值异常、CLR 回调、忙循环暂停、异常恢复、三代 Reset、暂停断开及实际 Domain Reload。临时 TS/JS/map 测试文件在结束时删除，不修改已有 launch 配置。失败时只结束脚本启动的 Unity 测试进程。

Windows 使用 Unity 2022.3.62f3；macOS ARM64 使用 Unity 2022.3.56f1。macOS 的 Unity 在 Domain Reload 断言通过后可能挂在退出阶段；此时工具会记录 **非正常完成** 并保存进程采样，不把退出超时误报为整体通过。详细执行结果见同目录 `verification.json`。

可追加 `--protocol-only` 单独运行连接/断点/Reset 测试，或 `--reload-only` 单独运行重载测试。默认仍执行两组；两种单组模式不能同时使用。单组通过不替代另一组结果。

当前结果：Windows 全部调试及重载验收正常退出；macOS ARM64 协议验收正常退出，实际 Domain Reload 的清理/重建断言通过，但 Unity 在退出阶段挂起，限制为一个 Job Worker 的复核仍未消除。采样中主线程位于 Unity 的 JobSystem shutdown，另有 CurlExecutor 活动。macOS Intel 完成插件编译，未运行 Intel Editor。Windows Editor、Win64、macOS ARM64 Editor 的现有 435 个命名用例及各自 smoke/TS demo 均通过。
