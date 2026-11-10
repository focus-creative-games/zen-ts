# P0 — ZLua 对齐与引擎迁移基线

状态：P0 基线盘点完成；语义迁移安排在 P3，不能把“已登记”解释为“已对齐”。

本目录随嵌入包交付；不修改主仓库和文档站。规范快照以 SHA256 记录，实际实现以
`ZenTSTest/Packages/com.code-philosophy.zen-ts` 为准。完整机器记录在 p0-baseline.json、
p0-rules.json、p0-spec-sections.json。章节目录全量登记；其中未专项检查的细则明确保留待审状态。

## 固定版本

- test_project_revision: `13e06df3095be157ded855edc36d159267e25961`
- zlua_code_revision: `b029c9d7176b08e9b7aecf484df2960adf10f53b`
- zlua_spec_revision: `6bb39ce5fdb8cfc7d3849ba78292d914ec9f50e0`
- zents_spec_revision: `b8a053cd947cd1270cb9de07481e4e565103d943`

规范每个文件和嵌入包修改前源文件均记录 SHA256；不能只用日期判断设计版本。

## 已运行的改造前基线

- Unity 2022.3.62f3，既有 Build-Win64 Debug 导出产物，QuickJS。
- p0-player-baseline.log：退出 0，JS 矩阵 55/55、smoke 10/10、三个 TS demo 全通过。
- 这是既有导出 C# 的运行结果。当前源码 Il2CppJsMatrixRunner 的数组只有 12 项；不能宣称
  “12 个源码套件已导出验证”，也不能把 55 套乘以内部用例数伪装成全部平台覆盖。
- Editor 改造前没有重新跑独立基线；已有历史日志不能当作本轮运行结果。
- V8、HostJs、macOS、Android、iOS、小游戏本轮 P0 均未验证。

## 规则台账

“contract-aligned”表示规范层对齐，不表示所有边界已经验证；“regression-covered”表示现有
用例有覆盖，不代表新规则测试已完备。tests 中的 missing 是明确的测试缺口。

| ID | 规则 | 状态 | 后续动作 |
|---|---|---|---|
| HOST-01 | 唯一宿主门面 / 反射创建后端 | contract-aligned | 保持 Initialize/GetFunction/Reset 门面；引擎与 CLR 宿主是两个维度 |
| HOST-02 | 重复 Initialize 拒绝 | regression-covered | P2 为 V8 重跑 |
| LIFE-01 | Reset 帧末执行、旧委托失效 | regression-covered | HostJs 只能重建 ZenTS 管理世界，不能关闭宿主 VM |
| OVER-01 | 最终名字分组、Alias 替换默认名 | contract-aligned | P3 对照继承与扩展方法竞争细则 |
| OVER-02 | 全签名键 miss 懒解析，单候选亦适用 | confirmed-drift | P3 移植 lazy lookup/cache，禁止预挂所有签名 |
| OVER-03 | 取消运行时 register_method，短名冻结 | confirmed-drift | P3 移除并把旧用例替换为冻结断言；P1 不改变旧语义 |
| OVER-04 | GTAP 泛型类型前缀 | confirmed-drift | P3 加 System.Type 前缀、泛型闭合、约束与竞争测试 |
| OVER-05 | params 展开及候选惩罚 | confirmed-drift | P3 0..N、整段数组、optionalUsed/paramsExpanded/paramsKind |
| OVER-06 | 必选参数缺失与范围检查 | source-spec-conflict | P3 direct/dispatch/ctor 共用 arity 与匹配规则 |
| OVER-07 | 逐参数 better member，而非总分吞掉歧义 | confirmed-drift | P3 迁移逐参数偏序、默认值和 params tie-break |
| MAR-01 | 声明门面与对象身份 | needs-targeted-test | P3 增加基类、接口、同一对象多门面对照 |
| MAR-02 | ActualTypeUserData 返回值策略 | confirmed-drift | P3 追加枚举值且不重排旧值，支持实际 string 的 ByObj 门面 |
| MAR-03 | 泛型成员配置在闭合后校验 | confirmed-drift | P3 泛型成员定义匹配、闭合类型校验、非法配置处理 |
| MAR-04 | struct ByVal / ByObj 与复制 | needs-targeted-test | P3 验证含引用字段 struct 的复制/持有与回收 |
| MAR-05 | 写入 GC 堆槽必须执行 write barrier | needs-targeted-test | P3 单独审计字段、数组、Composite、byref 的堆写屏障 |
| MAR-06 | ref/in 临时槽与可写 payload | contract-aligned | P3 补 nullable/ref reference identity 对照 |
| MAR-07 | out 不占输入、多返回值 | javascript-adaptation | 已接受方案：JS [returnValue,out...]；void+[out...]。P3 修改声明及测试 |
| MAR-08 | C#→JS byref Opaque scope | regression-covered | P2 保持 scope/generation；异步完成后禁止访问 |
| MAR-09 | Table / UnpackedValues | javascript-adaptation | P3 定稿展开返回与 out 组合规则；P1 保留旧实现 |
| MAR-10 | null 与 undefined 区分 | javascript-adaptation | 保留差异；direct/dispatch/optional/nullable 一致 |
| MAR-11 | Int64/UInt64 精确表示 | javascript-adaptation | P3 按已接受方案引入 BigInt 并生成 TS bigint；不在 P1 偷改规则 |
| MAR-12 | 数组、空洞、字节 | javascript-adaptation | P3 连续性/空洞/复制与共享边界定稿 |
| MAR-13 | delegate 双向绑定与身份 | regression-covered | P2 按 ABI 重建桥；不将 Win64 trampolines 当通用签名解法 |
| MAR-14 | Nullable 无值/有值 | needs-targeted-test | P3 null、Table、ref Nullable 分开验收 |
| META-01 | XML 三表优先级与非法配置 | needs-targeted-test | P3 对照 Attribute 优先级、重复项硬失败、Mono soft fallback |
| META-02 | InteropMetadata 热更 XML | confirmed-drift | 先解决 ZLua Settings 与 Host API 对热更 XML 的描述冲突，再移植 |
| META-03 | Unity 优化总开关 | confirmed-drift | P3 移植 GetComponentT/GetComponentN/ActualType 与默认开关 |
| META-04 | 实例与 extension 同名竞争 | needs-targeted-test | P3 对照统一 overload 竞争和签名去 this |
| JS-01 | JS this / callable / Symbol 协议 | javascript-adaptation | 保持 .call 接收者语义；补 then/Symbol 探测测试 |
| JS-02 | ESM 与 Promise | javascript-adaptation | P4 用构建器处理宿主模块，不使用运行时正则实现完整 ESM |
| ENG-01 | Native V8 / HostJs 最终矩阵 | planned-engine-migration | P2 Win64 Editor+Player V8；P4 HostJs；P5 iOS JITless |
| ENG-02 | HostJs 文档与实现错位 | source-spec-conflict | 以嵌入包为实现基线，不将旧文档声明记为能力 |
| BUILD-01 | 测试覆盖不能混同目标平台 | source-export-divergence | 记录导出产物自己的基线，后续 C# 变更才重新导出 |
| SPEC-01 | ZLua 文档内部版本冲突 | upstream-spec-conflict | 以源码+针对性测试确认，登记冲突而非盲目复制 |

## 已接受的 JS 差异与实施边界

- null/undefined 保持区别；不能机械映射为 Lua nil。
- JS→CLR out 不占输入，以数组返回 `[returnValue, out1, ...]`；void 返回 `[out1, ...]`。
- C#→JS 的 byref 仍为作用域内 OpaqueValue；await 后使用必须失败。
- Int64/UInt64 计划用 BigInt，安全整数 Number 可按显式规则接受；旧 Number-only 规则在 P1 保留。
- JS 普通对象、Array、TypedArray 分别定义输入形态；字节不通过 UTF-8 文本中转。
- JS 方法接收者使用 this；ESM、Promise、Symbol、then 按语言/宿主协议适配。
- UnpackedValues 返回与 out 的组合、宿主微任务时序仍须在 P3/P4 定稿；不能用 Lua 栈行为代替设计。

## 尚待处理的规范冲突

1. ZLua Host API 的 params 摘要落后于 overload 与 ParamsArrayUtil 实现；跟随新版行为前要补针对性测试。
2. ZLua Host API 允许 InteropMetadata 热更 XML，但 Settings 对热更新 XML 的说明不同；单独确认生命周期和加载顺序。
3. ZenTS 文档 build/06-HOSTJS.md 描述 P4 已验收，但嵌入包只有原型；以源码和实际运行结果判定能力。

## 后续同步流程

ZLua 每次规则变更，先更新 revision 与台账，再迁移对应 JS 规则和成对测试。
P1 只重构边界，不悄悄移除 register_method、不更改 params/out/BigInt 期望；这些破坏性变化在 P3 同步规范与测试。
