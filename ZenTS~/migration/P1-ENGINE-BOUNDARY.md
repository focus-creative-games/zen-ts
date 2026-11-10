# P1 — CLR 计划与引擎边界

状态：边界抽取、过渡适配器、构建配方与 Win64/Editor 回归交付。
当前可运行引擎仍是 QuickJS；V8 实现属于 P2，HostJs 实现属于 P4。不能据此宣称全平台支持。

## Native C++

`zents-runtime/engine/EngineApi.h` 只包含标准库类型。Context/Runtime/Module 为不透明类型；
Value 是 ZenTS 自有 16 字节 opaque envelope，载荷不向 CLR 内核暴露。它是进程内接口，
不等同 Mono P/Invoke ABI 或 JS/WASM 线协议。别名 `js` 指向 `zents::engine`。

`engine/quickjs/QuickJsEngine.cpp` 是当前唯一包含 QuickJS 的执行适配器；根部
QuickJsCompatible.h 仅供此适配器包含。适配器显式转换值和 callback 参数，
不强转两种函数指针来假装回调 ABI 一致。常见不超过 16 个参数使用栈缓冲；较大参数溢出到 vector。
有捕获值的 native function 用私有 holder 保持捕获值及 callback record 生命周期。
GC finalizer 元数据按 runtime/class 分隔，在 JS_FreeRuntime 完成后清除，避免 GC 期间悬空。

覆盖：基础值、字符串、属性、数组、原型、函数/回调、host payload、异常、模块、微任务、
retain/release、identity、runtime/context 生命周期。所有原 C++ runtime 消费者已迁移到此 API。
未实现 V8 或 HostJs backend；它们必须提供同一行为，不能继续往内核引入 SDK 类型。

## 所有权与异常

- 拷贝 Value 只拷贝借用 token，不自动 retain；New/Get/Call/Eval/Dup 返回拥有引用，须恰好释放一次。
- SetProperty、DefinePropertyGetSet 和 Throw 消耗值参数，即使失败也不能再释放；接收者为借用。
- Callback argv、receiver、captured data 在当前调用期间借用；跨调用保存必须 Dup。
- 返回 Exception sentinel 时错误保存在 Context；外层边界按现有 JsException 规则处理。
- GetIdentity 仅在对象被保活时有效，不能解引用；V8 必须提供不随移动 GC 改变的 identity token。
- 不同 runtime、Reset generation 的值不可混用。P1 保留既有 JsEnv/generation 检查；
  未引入宿主 weak handle 表，不能据此声称已解决 HostJs GC 或引用环。
- GetCompiledModule 是模块专用提取入口，核心层不再假设模块值含原生指针。

## 绑定计划

`binding/BindingPlan.h` 无任何引擎或 libil2cpp SDK include；只前置声明 CLR 类型。
实际 MarshalPlan、MethodDefaultArgs、MethodPlan、FieldPlan、OverloadPlan、MemberPlan、
TypeBindingPlan 定义均迁到这里。模板参数保留类型安全的执行扩展，计划本身没有 JS 值和回调字段。

运行中的 MarshalMetaInfo、MethodMarshalCtx、TypeBinding 继承这些计划；JS writer/invoker、
prototype、type object 留在执行扩展。不是建立一组没人消费的影子 DTO。
生命周期仍由原绑定系统负责；没有引入第二个 plan cache 或新的序列化格式。
当前计划保留旧 arity/params/重载规则，P3 才迁移 ZLua 新语义。

## Editor Mono

`Runtime/Mono/Engine` 提供 EngineApi、IMonoEngineApi、IMonoValueApi、IMonoCallbacks 和
JsValue envelope。引擎操作、值生命周期、callback/error boundary 从同一个 backend 实例取得，
不能分别选择三个不匹配的后端。当前在程序集创建时固定选择 QuickJsTransition，禁止活跃域中途切换。

原 QuickJsDll、QuickJsDllName、引用计数操作、native gate 与系统动态符号查找移入
Engine/QuickJs；原文件移动时保留 Unity GUID。公共门面 JsAppDomain/IJsRuntime 未修改。
泛型 Emit、Marshal、Registry 消费自有 API，不再引用 QuickJsDll 或直接读取 value 的载荷字段。
委托 identity cache 使用适配器提供的 identity。回调注册与错误哨兵也由 backend 提供。

`Runtime/Mono/Binding/BindingPlan.cs` 分离 CLR Type/MethodInfo 与成员键元数据；
现有 TypeBinding/MethodClosureTag 实际继承这些计划。Mono 仍使用自己的 Emit；
没有为了“共享”把 C# Expression 实现强行搬成 C++。

Mono 的过渡插件仍使用既有 pointer-output ABI。新的 V8 原生插件/C ABI 的实现和实机验证留在 P2；
本阶段没有声称旧 quickjs.dll 可以直接替换成 V8 DLL。

## V8 构建配方

`ZenTS~/v8/build.py` 接受既有 checkout 和精确 revision，校验 tracked source clean，
输出 GN 配方并构建 v8_monolith。实际构建产物写 revision/参数/host/命令/hash manifest。
可用 --dry-run 在当前 Windows 检查 Windows/macOS/Android/iOS 配方；iOS 强制 JITless。
P1 未下载 V8，也未固定尚未经 Unity 验证的 shipping revision；该选择在 P2 记录。

## 可重复验收

1. `python ZenTS~/migration/verify-boundaries.py`：阻止 SDK 类型和直接 payload/PInvoke 绕过边界。
2. `python ZenTS~/migration/verify-contract.py`：使用 C++17 编译运行 `contract-smoke.cpp`，只给标准库 include；证明公共引擎/计划头文件独立可编译。
3. 把 package runtime（不含 generated）复制到现有 Build-Win64 runtime 后增量 MSBuild；
   生成 C# 未变的情况下无需重新 Generate/导出。
4. 运行既有 Win64 Player，比较 p0-player-baseline.log 和 p1-player.log 的各套件结果。
5. Unity batchmode 执行 ZenTS.Tests.BatchCommands.RunMonoSmokeTests，验证新的 Editor 源码。

最终验证摘要与日志 hash 见 verification.json。未验证的平台保持未验证，不用跳过用例制造通过。

## P2 开始位置

先实现 Native V8 对上述接口的对等实现；导入官方 revision 对应的 headers/defines/artifacts。
实现 V8 持久/弱 handle、稳定 identity、Scope、TryCatch、模块和微任务、Mono pointer-output callbacks。
重新验证 Win64 ABI 与 Editor Domain Reload，再继续其他平台。不得复用 Win64 trampoline 来宣称 ARM64/WASM 支持。
P3 再迁移 P0 台账中的行为变化；P4 接通宿主 JS，不要求实现 QuickJS 的全部 API。
