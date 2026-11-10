# ZLua / ZenTS 测试覆盖审计与补充

## 结论及统计口径

对照本机 `D:/workspace/zlua/ZLuaTest` 的 Lua 矩阵，以及 `D:/workspace/zts/ZenTSTest` 的 JS 矩阵：

| 项目 | 矩阵脚本文件数 | 命名 `test_*` 用例声明数 |
|---|---:|---:|
| ZLua | 71 | 581 |
| ZenTS 修改前 | 56 | 374 |
| ZenTS 本轮修改后 | 56 | 435 |

数字来自源码清单，排除注释中的测试声明；不是断言数、代码行数或分支覆盖率，也不包括另行执行的 C# smoke、配置校验及 TS demo。新增 61 项，同时将原先完全重复的 5 项替换为独立的冻结绑定测试，不靠复制用例增加数字。

此前日志的 **56/56 是模块通过数，并非只有 56 个测试**。56 个模块中，52 个包含独立命名 JS 测试；其余 4 个是 C# 驱动的 JS 导出/初始化支持模块，`run()` 不能作为功能验证依据，其行为由外围 C# smoke 检查。`jsinvoke` 目录没有 `test_*`，不表示 C#→JS 没有测试，已有 `TC_JsInvokeMarshal` 等 C# 调用链测试。

差距仍然存在：ZLua 的类型、封送、方法选择、适配器和库函数测试更细；ZenTS 的一些早期移植用例保留了过期 SKIP 注释或空套件，另一些场景合并在 P3 或 C# smoke 中。两者存在 Lua / JS 语义差异，不能用总数量证明设计或正确性相等。本轮优先补充可跨 Editor / IL2CPP / Host VM 执行且有实质断言的互操作边界。

## 新增及恢复的场景

- 基础封送：有符号/无符号整数边界、UTF-16 char、严格 bool、默认 Int64/UInt64 number、空字符串/null、嵌入 NUL、非 BMP/组合字符、长 UTF-8 字符串。
- Nullable：恢复 int/enum 的 null 输入输出，并检查零值不是 null。
- 属性：恢复 ref-return 实例/静态属性读取；补齐 Vector3 读写、只读属性、C# 预创建对象以及结构体复制语义。
- 数组：非零/负下界、多维坐标越界、SZArray 读写越界及失败后恢复、独立数组/`to_array` 复制、字符串元素 null/Unicode。
- 二进制：Point2D/Vector3 数组的长度及实际字节内容、复制隔离、拒绝普通 JS 数组/引用类型数组/含托管引用的结构体数组。
- 委托及事件：五参数混合整数/浮点/Int64 ABI、JS 函数身份、null 委托、嵌套回调、回调异常恢复、替换已保存回调、重复订阅/移除一个/实例隔离/多播顺序。
- JavaScript 特有边界：BigInt 零/高位/溢出/错误类型、抛出 null/字符串后恢复；冻结静态方法不能替换、删除或动态定义。

## 测试发现并修复的问题

1. Editor 的 ref-return 属性使用 `PropertyInfo.GetValue` 会报错。改为生成读取托管引用当前值的 IL，并按元素类型封送，不把地址暴露给 JS。
2. Editor 的 `to_bytes` 使用 `Buffer.BlockCopy`，无法复制 blittable 结构体数组。增加递归元素合法性检查，使用托管 `sizeof` 计算元素步长，固定数组后按真实布局复制并释放固定句柄。
3. IL2CPP 的 ref-return 把返回槽按元素大小分配，`int&` 在 Win64 上会写入 8 字节地址，导致栈越界。改为按指针分配返回槽，然后读取引用指向的值。
4. 原生数组通过地址宏直接读写，没有检查坐标。增加逐维检查和下界归一化，并为引用元素写入补齐 GC write barrier。
5. 原生委托缺少五参数混合 ABI 的跳板。增加对应 ABI 类别的静态跳板；微信 OptimizeSize 继续使用已有 fully shared generic 路径。
6. 原生 `to_bytes` 未拒绝带引用的值类型。改用 IL2CPP blittable 元数据限制，避免把引用地址当普通字节复制。
7. 微信官方转换器将框架中的 `console.error` 替换为 `err`，也改写了原先使用明文源码作为键的预编译表，导致新增测试模块无法加载。模块/全局脚本查找键改为无碰撞的 UTF-16 十六进制编码；保留源码校验，不回退到按模块名加载或动态求值。受限宿主测试模拟官方替换并验证加载及异常路径。编码会增加未压缩注册表大小，后续可在保留完整身份检查的前提下优化。

## 防止统计误导

- `defineSuite` 执行全部命名用例，聚合失败；不在首个失败后悄悄停止。
- 空套件直接报错。修复原先空的 ref-return 套件。
- 修正两个文件共用 `binding_frozen` ID 的问题，源码审计拒绝重名套件/用例。
- `[ZenTS][case-summary]` 单独输出用例总数、通过、失败和跳过数，以及每套件失败/跳过原因。
- Host VM 的受限工作线程测试明确计为 skipped，不能再当作 passed；其他平台不得跳过。
- 报告保存在当前 JS 环境内，避免导入路径别名造成统计实例分裂；Reset 仍会创建新环境。
- 浏览器/微信验收除原有模块、smoke、TS 标记外，还核对当前源文件对应的每个套件及用例总数；遗漏测试或额外跳过均失败。

## 可复现命令

```powershell
node Packages/com.code-philosophy.zen-ts/ZenTS~/migration/test-suite-runner.mjs D:/workspace/zts/ZenTSTest
python Packages/com.code-philosophy.zen-ts/ZenTS~/migration/audit-test-coverage.py --project D:/workspace/zts/ZenTSTest --zlua D:/workspace/zlua/ZLuaTest --log coverage-editor.log --log p5-player.log --log p4-webgl-browser.log --log wechat-player.log --output Library/ZenTS/Verification/test-coverage-inventory.json
```

测试日志必须来自本轮构建和运行；不要用早期 56/56 日志冒充新增用例通过。C# 运行器变化已重新导出 Win64；后续纯原生修复使用现有工程增量编译。

## 边界

本轮未宣称与 ZLua 全量覆盖等价。适配器、更多封送组合、生成声明/XML 配置矩阵仍需逐项按规范扩充；Lua table、Lua 多返回值、Lua VM 专用 API 不应直接复制为 JS 测试。没有把 macOS、iOS、Android 或手机真机的历史通过结果算成本轮新增用例的验收。

## 本轮实际验收

| 环境 | 命名用例通过 / 失败 / 跳过 | 外围 smoke / TS demo |
|---|---|---|
| Windows Editor（Mono + V8） | 435 / 0 / 0 | 8/8、3/3 |
| Win64（IL2CPP + V8） | 435 / 0 / 0 | 10/10、3/3 |
| WebGL（浏览器 Host VM） | 434 / 0 / 1 | 10/10、3/3 |
| 微信开发工具（Host VM，OptimizeSize） | 434 / 0 / 1 | 10/10、3/3 |

两个 Host VM 环境跳过的都是同一个托管工作线程用例，不是功能失败豁免。浏览器真实 GC、异常/NUL/UTF-8 边界，以及禁用 eval/Function/TextDecoder 的受限宿主回归同时通过。微信最终验收没有修改框架文件或测试期望，使用正式导出的构建内容，仅在独立副本增加现有日志收集入口。

摘要和日志指纹见 `test-coverage-verification.json`；完整源码清单及逐平台核验输出位于测试工程 `Library/ZenTS/Verification/test-coverage-inventory.json`。本轮未提交 Git。
