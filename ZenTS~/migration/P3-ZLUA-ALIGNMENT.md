# P3：对齐 ZLua 的 CLR 互操作设计

本轮修改直接位于 ZenTSTest 的嵌入 package；没有同步主仓库。验收平台为 Unity 2022.3.62f3、Windows Editor（Mono + V8）和 Win64（IL2CPP + 静态 V8）。P0 台账保留原始基线；P3 另复核当前 ZLua `f95fafbaef31b1dec5d87770f076ad9df12257bf`，规范 revision 为 `6bb39ce5fdb8cfc7d3849ba78292d914ec9f50e0`，不把 JavaScript 差异伪装成 Lua 行为。

## 已落实的设计

- 方法、构造函数共用严格调用计划：必需参数、默认参数、params、ref/in/out 和封送策略；重载按各参数偏序比较，再比较默认参数和 params 惩罚，拒绝交叉优劣造成的歧义。
- 全签名按 miss 懒绑定，覆盖单候选、Alias、继承和扩展方法；短名绑定后冻结，移除 register_method。泛型使用 inflate 或仅接受 System.Type 实例前缀的 GTAP，闭合方法计划缓存并检查约束。
- Attribute 优先于 AOT XML，再到合法的非泛型类型策略；XML 在加载时形成 AOT 程序集快照，不提供 Reload 或运行时热更新入口。泛型成员规则保留定义 token，在闭合后检查实际槽位类型；泛型类型级规则不继承。
- 默认声明门面与对象身份分离；ActualTypeUserData 只用于返回值门面。静态/实例、ByVal/ByObj 域隔离，校验 receiver，保护只读字段和方法绑定。
- Table 支持公开字段与属性、嵌套策略和引用写屏障；区分 own property 缺失与显式 undefined。数组按 length 检查完整输入并拒绝空洞。
- Unity 主线程身份由 Unity 初始化回调捕获，不从第一个调用者推断；公共入口和已绑定委托拒绝工作线程调用，拒绝后主线程仍可继续调用。
- Opaque 借用采用嵌套作用域，内层退出不使外层句柄失效；结束后不能访问，支持 ref 透传和参数策略。
- 默认启用 ZLua 同源的 Unity GetComponent 优化：泛型别名 GetComponentT、字符串别名 GetComponentN、Type 重载保留 GetComponent，并返回实际类型门面。注册 IUnityLinkerProcessor 显式提供 package 的保留规则。
- 修复 Win64 导出 exe 路径归一化、Windows SDK/toolset 配置、静态 CRT 与 Bee 缓存标识；C# 变化时重新导出，原生变化继续复用导出工程增量编译。

## 必须保留的 JavaScript 差异

- Int64/UInt64 默认 number；仅显式 BigInt 策略使用 bigint，并检查有符号/无符号范围。
- out 不占 JS 输入槽，返回 [returnValue, out1, ...]；void 时返回 [out1, ...]，UnpackedValues 输出保持嵌套数组。
- params 接受零到多个尾部参数或一个匹配的 CLR 数组；JS 数组须显式 spread。null 是元素，不是“未传入”。
- Bytes 使用复制的 Uint8Array，不以字符串承载原始字节；普通实例方法遵循 JS this。
- then/Symbol 协议探测返回缺失值；普通未知 CLR 成员报错。禁止用 expando 替换已绑定的方法。
- Opaque 借用不能跨异步边界；模块和 GC 通过自有引擎边界实现。

## 验收

| 验收项 | 结果 |
|---|---|
| Windows Editor JS 矩阵 | 56/56 |
| Windows Editor Smoke / TS demo | 8/8、3/3 |
| Win64 JS 矩阵 | 56/56 |
| Win64 Smoke / TS demo | 10/10、3/3 |
| 新增 P3 细分用例 | 31 项，包含 Unity 优化及闭合泛型 XML |
| 真正的 Domain Reload | V8 运行时数量 1 → 0 → 1 |
| XML 非法策略/重复规则、生成 out/BigInt 声明 | 通过 |
| TypeScript --noEmit | 通过 |
| 自有 ABI / SDK 边界、独立 V8 模块/回调/GC/生命周期测试 | 通过 |
| 普通 MSBuild、安装与导出原生副本一致性 | 通过 |

测试期望按已接受的目标契约更新；没有新增平台跳过。Player 子集 runner 已对齐完整列表。日志和产物哈希由 verify-p3.py 写入 p3-verification.json；该脚本也校验官方 V8 源码 SDK、静态链接依赖和部署 JS 一致性。P0/P1/P2 的历史报告不覆盖、不复用为 P3 证据。

本轮不声称完成 HostJs、WebGL、微信小游戏或 macOS/iOS/Android 的运行验收；这些仍属于后续阶段。
