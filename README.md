# ZTS

ZTS 是一个针对 Il2Cpp 优化的现代、简洁、易用的 Unity **TypeScript / JavaScript** 脚本方案，由 **QuickJS** 驱动，设计与 [ZLua](https://github.com/focus-creative-games/zlua) 对齐。

- 文档：[zts.code-philosophy.com](https://zts.code-philosophy.com/)
- 对照产品：[ZLua](https://doc.zlua.cn) / [zlua GitHub](https://github.com/focus-creative-games/zlua)
- English：[README_EN.md](./README_EN.md)

---

## 为什么选择 ZTS

相对 Puerts / xLua-JS 类方案，以及「自管 QuickJS + 手写绑定」：

| | |
|--|--|
| **更易用** | 设计贴近 C#；**零 per-type Wrap 白名单**；类型懒绑定 |
| **更完备** | 标准和完备的 C#↔JS 交互：方法重载、ref/out、struct ByVal/ByObj、Nullable、委托、数组、指针、`[TsMarshalAs]` 等 |
| **更统一** | 与 ZLua **同一套语义契约**（门面 / Marshal / 类型系统 / 生命周期），Lua 与 TS/JS 可共用产品心智 |
| **更高效** | Player **Il2Cpp** 热路径为 C++ 桥接；签名复用 stub；支持少生成甚至 **0 桥接函数** 仍可跑通主路径 |
| **更少 GC** | 引用类型与 struct（含含引用字段的 struct）默认走 Registry / ByVal exotic；另有 OpaqueValue 等策略 |
| **双运行时** | 开发期 **Editor Mono** + 发布 **Il2Cpp Player**，兼顾迭代与线上效率 |
| **TS 一等公民** | 官方 TypeScript 工作流（`TsProject`、`csharp:` 声明、进 Play 闸门）；运行时仍只跑 emit 后的 JS |

### 与 ZLua 的关系

| | ZLua | ZTS |
|--|------|-----|
| 脚本侧 | Lua（PUC-Rio / LuaJIT） | JavaScript（QuickJS）；可选 TypeScript → ES module |
| 宿主门面 | `LuaAppDomain` | `TsAppDomain` |
| 类型入口 | `CSharp[...]` | 同左；另支持 `import { T } from "csharp:…"` |
| 标准库 | `zlua.*` | `zts.*` |
| 属性 | `[LuaMarshalAs]` 等 | `[TsMarshalAs]` / `[TsAlias]` / `[TsExtension]` |

业务侧 API 形态刻意对齐：会用 ZLua，即可很快上手 ZTS。

---

## 特性

- **零配置开箱**：无需为每个类型 Generate C# Wrap；`CSharp[assembly][typeFullName]` 懒绑定
- **完备互操作**：字段 / 属性 / 方法 / 重载 / 扩展方法 / 泛型 / 委托 / 数组 / struct / enum / Nullable / ref·out·in / 指针
- **双 Runtime**：Editor Mono（Expression Emit）+ Player Il2Cpp（C++ `zts-runtime`）
- **TypeScript 工作流**：`ZTS/Init TypeScript Project` → `TsProject/`；`tsc --noEmit` 检查；esbuild 1:1 emit；Player 拷贝到 StreamingAssets
- **原生调试路径预留**：Editor 侧 Debugger Host 接口（持续完善）

### 平台与版本（当前）

| 类别 | 状态 |
|------|------|
| **引擎** | Unity **2022.3.x**（主验证）；其它 LTS / 团结引擎按需扩展 |
| **脚本 VM** | QuickJS（pin 见包内 `ZTS~/` / 文档） |
| **运行时** | Editor **Mono** + Player **Il2Cpp** |
| **平台（开发）** | Windows x64 Editor |
| **平台（Player）** | 以 Win64 Il2Cpp 为主验证；其它 Il2Cpp 目标陆续跟进 |

---

## 用法一：纯 JavaScript

运行时 **只** 加载 ES module（QuickJS）。模块名使用 **canonical specifier**（相对逻辑路径，**不含** `.js`）。

契约细节见文档站与 `Docs/spec/01-HOST-API.md`、`02-TYPE-SYSTEM.md`。

### 1. 初始化（只需 Loader）

```csharp
using System.IO;
using System.Text;
using UnityEngine;
using ZTS;

public static class ZtsBootstrap
{
    static object LoadJsModule(string module)
    {
        // module 为 canonical，例如 "app" / "game/logic"（不含 .js）
#if UNITY_EDITOR
        var path = Path.Combine(Application.dataPath, "..", "JsScripts", module + ".js");
#else
        var path = Path.Combine(Application.streamingAssetsPath, "Js", module + ".js");
#endif
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init() => TsAppDomain.Initialize(LoadJsModule);
}
```

无需 `JsCallCSharp` 列表，无需为每个类型 Generate Wrap。

### 2. C# → JS（`GetFunction`）

```csharp
public class GameEntry : MonoBehaviour
{
    void Start()
    {
        var add = TsAppDomain.GetFunction<System.Func<int, int, int>>("app", "add");
        Debug.Log(add(10, 20)); // 30
    }
}
```

```js
// JsScripts/app.js  →  GetFunction("app", "add")
export function add(a, b) {
  return a + b;
}
```

仅 **named export**；不要对 `csharp:` 模块调用 `GetFunction`。

### 3. JS → C#（懒绑定，语法贴近 C#）

```csharp
// 任意 public 类型，无需导出配置
public class Demo
{
    public int x;
    public static int Add(int a, int b) => a + b;
    public void SetX(int v) => x = v;
}
```

```js
const AC = CSharp['Assembly-CSharp'];
const Demo = AC['Demo'];

console.log(Demo.Add(3, 5)); // 静态方法

const d = new Demo();        // 构造
d.x = 10;                    // 字段
d.SetX(20);                  // 实例方法
console.log(d.x);

// 标准库
const arr = zts.new_szarray_by_element_type(zts.types.int32, 2);
```

手写回归 / 矩阵测试可继续放在 `StreamingAssets/Tests/Js`（纯 JS），**不强制**迁 TypeScript。

---

## 用法二：TypeScript 工作流

ZTS **已支持官方 TypeScript 工作流**：用 TS 写业务、生成 `csharp:` 声明、进 Play 前类型检查；**运行时仍只执行 emit 后的 JS**（不读 `.ts`）。

完整契约：[TypeScript 工作流](https://zts.code-philosophy.com/)（对应 `Docs/spec/14-TYPESCRIPT.md`）。

### 1. 初始化工程

Unity 菜单：

1. **`ZTS/Init TypeScript Project`** — 将包内脚手架复制到工程根 `TsProject/`
2. 在 `TsProject/` 执行 `npm install`（devDependencies：`typescript`、`esbuild`）
3. **`ZTS/Generate Typings`** — 生成 `TsProject/generated/csharp/**`（与 Il2Cpp Generate 类型集同源）
4. **`ZTS/Compile TypeScript`** — `tsc --noEmit` + esbuild/tsc emit

布局摘要：

```text
<UnityProject>/
  TsProject/
    src/           # 业务 .ts（入库）
    generated/     # csharp: 声明（入库）
    out/           # emit 的 .js（gitignore）
  Assets/StreamingAssets/ZTS/   # Player 构建拷贝
```

### 2. 用 TS 写业务

```typescript
// TsProject/src/game/logic.ts
import { Demo } from "csharp:Assembly-CSharp/Demo"; // 示例；以生成声明为准

export function OnTick(dt: number): void {
  const d = new Demo();
  d.SetX(20);
}

export function add(a: number, b: number): number {
  return a + b;
}
```

要点：

| 项 | 约定 |
|----|------|
| Canonical | `game/logic`（**无** `.ts` / `.js`） |
| C# 类型 | `import { T } from "csharp:…"`（**禁止** `import type`，类型对象是运行时值） |
| 相对导入 | 可写 `./foo.js`（Node16 风格）；loader 会规范为 canonical |
| 检查 | `tsc --noEmit`（进 Play 闸门默认开启，Settings 可关） |
| Emit | esbuild **1:1 ESM、不 bundle**（或同 `outDir` 的 tsc emit） |

### 3. C# 绑定 TS 导出

```csharp
// jsModule = canonical，不含后缀
var onTick = TsAppDomain.GetFunction<System.Action<float>>("game/logic", "OnTick");
var add = TsAppDomain.GetFunction<System.Func<int, int, int>>("game/logic", "add");
```

Editor：`moduleLoader` 读 `TsProject/out/{canonical}.js`。  
Player：构建时拷贝 `out/**/*.js` → `StreamingAssets/ZTS/`，运行时 **只** 读 StreamingAssets。

进 Play 前默认跑 `tsc --noEmit`（Settings 可关）。日常也可手动 **`ZTS/Compile TypeScript`**。

---

## 程序集

| 程序集 | 平台 | 说明 |
|--------|------|------|
| `ZTS.Common` | 全平台 | `TsAppDomain` 门面、属性、公共类型 |
| `ZTS.Mono` | Editor | QuickJS P/Invoke + Mono Callback Gate + Emit |
| `ZTS.Il2Cpp` | Player | Il2Cpp 宿主接线（实现于 `ZTS~/zts-runtime`） |
| `ZTS.Editor` | Editor | Install / Export / TypeScript 工具链 / Settings |

---

## Editor 原生依赖（Mono）

```text
Plugins/quickjs/
  win32-x64/quickjs.dll     # QuickJS Win64（非 NAN boxing：JSValue = 16 字节）
  zts_mono_gate.dll         # Editor-only JS→C# callback gate
```

构建（在包目录下）：

```bat
ZTS~\mono-native\build_quickjs_msvc.bat
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ZTS~\mono-native\build_zts_mono_gate.ps1
```

Il2Cpp：菜单 **ZTS / Install...** 将 `ZTS~/zts-runtime` 与 QuickJS 源装入 LocalIl2Cpp；**ZTS / Export Build-Win64...** 导出可 MSBuild 的 Player 工程。

---

## 许可证

MIT。欢迎自由使用、修改和分发。

## 联系我们

- 文档：[https://zts.code-philosophy.com/](https://zts.code-philosophy.com/)
- 邮件：`zts@code-philosophy.com`
- 产品站：[code-philosophy.com](https://code-philosophy.com)
- QQ / Discord：与 ZLua 社区共用渠道（见 [ZLua README](https://github.com/focus-creative-games/zlua)）
