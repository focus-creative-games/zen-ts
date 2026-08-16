# ZTS

ZTS is a modern, concise, and easy-to-use Unity **TypeScript / JavaScript** scripting solution powered by **QuickJS**, with strong Il2Cpp optimization. Its design parallels [ZLua](https://github.com/focus-creative-games/zlua).

- Documentation: [zts.code-philosophy.com](https://zts.code-philosophy.com/)
- Sister product (Lua): [ZLua](https://doc.zlua.cn) / [zlua GitHub](https://github.com/focus-creative-games/zlua)
- Unreal edition (WIP): [zts-ue](https://github.com/focus-creative-games/zts-ue)
- 中文：[README.md](./README.md)

---

## Why ZTS

Compared with Puerts / xLua-style JS bridges, or “hand-rolled QuickJS + custom bindings”:

| | |
|--|--|
| **Easier** | C#-like DX; **no per-type Wrap whitelist**; lazy type binding |
| **More complete** | Full C#↔JS interop: overloads, ref/out, struct ByVal/ByObj, Nullable, delegates, arrays, pointers, `[JsMarshalAs]`, etc. |
| **Unified with ZLua** | Same semantic contract (host API / marshal / type system / lifetime)—one mental model for Lua and TS/JS |
| **Faster** | Player **Il2Cpp** hot paths are C++ bridges; same-signature stub reuse; even **0 generated bridges** can cover most paths |
| **Less GC** | Reference types and structs (including structs with reference fields) use Registry / ByVal exotic by default; plus OpaqueValue strategies |
| **Dual runtime** | **Editor Mono** for iteration + **Il2Cpp Player** for shipping |
| **First-class TypeScript** | Official `TsProject` workflow, `csharp:` declarations, Play-mode type gate; runtime still executes emitted JS only |

### Relation to ZLua

| | ZLua | ZTS |
|--|------|-----|
| Script side | Lua (PUC-Rio / LuaJIT) | JavaScript (QuickJS); optional TypeScript → ES modules |
| Host façade | `LuaAppDomain` | `JsAppDomain` |
| Type entry | `CSharp[...]` | Same; plus `import { T } from "csharp:…"` |
| Stdlib | `zlua.*` | `zts.*` |
| Attributes | `[LuaMarshalAs]`, etc. | `[JsMarshalAs]` / `[JsAlias]` / `[JsExtension]` |

Host APIs are intentionally aligned: if you know ZLua, ZTS feels familiar.

### Sister product: Unreal (zts-ue)

For a modern TypeScript solution on **Unreal Engine** (aggressively optimized for C++), see **[zts-ue](https://github.com/focus-creative-games/zts-ue)**. It is **still under development**. This package and docs site cover **Unity / Tuanjie** only; follow that repo for UE progress and usage.

---

## Features

- **Zero config**: no per-type C# Wrap generate; lazy `CSharp[assembly][typeFullName]`
- **Complete interop**: fields / properties / methods / overloads / extensions / generics / delegates / arrays / struct / enum / Nullable / ref·out·in / pointers
- **Dual runtime**: Editor Mono (Expression Emit) + Player Il2Cpp (`ZTS~/zts-runtime` C++)
- **TypeScript workflow**: `ZTS/Init TypeScript Project` → `TsProject/`; `tsc --noEmit`; esbuild 1:1 emit; copy to StreamingAssets for Player
- **Debugger hooks**: Editor Debugger Host interface (work in progress)

### Platforms and versions (current)

| Category | Status |
|----------|--------|
| **Engine** | Unity **2021.3.x** / **2022.3.x** / **6000.0.x** / **6000.3.x** / **6000.5.x**; **Tuanjie Engine 1.x.y** |
| **VM** | QuickJS (see pin under `ZTS~/` / docs) |
| **Runtime** | Editor **Mono** + Player **Il2Cpp** |
| **Dev platform** | Windows x64 Editor |
| **Player** | Win64 Il2Cpp primary; other Il2Cpp targets follow |

---

## Usage A: Plain JavaScript

The runtime loads **ES modules only** (QuickJS). Module names use a **canonical specifier** (logical path **without** `.js`).

Contracts: [docs site](https://zts.code-philosophy.com/) and `Docs/spec/01-HOST-API.md`, `02-TYPE-SYSTEM.md`.

### 1. Initialize (loader only)

```csharp
using System.IO;
using System.Text;
using UnityEngine;
using ZTS;

public static class ZtsBootstrap
{
    static object LoadJsModule(string module)
    {
        // canonical, e.g. "app" / "game/logic" (no .js)
#if UNITY_EDITOR
        var path = Path.Combine(Application.dataPath, "..", "JsScripts", module + ".js");
#else
        var path = Path.Combine(Application.streamingAssetsPath, "Js", module + ".js");
#endif
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init() => JsAppDomain.Initialize(LoadJsModule);
}
```

No `JsCallCSharp` lists. No per-type Wrap generate.

### 2. C# → JS (`GetFunction`)

```csharp
public class GameEntry : MonoBehaviour
{
    void Start()
    {
        var add = JsAppDomain.GetFunction<System.Func<int, int, int>>("app", "add");
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

**Named exports only.** Do not `GetFunction` against `csharp:` modules.

### 3. JS → C# (lazy binding, C#-like)

```csharp
// Any public type — no export config
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

console.log(Demo.Add(3, 5)); // static

const d = new Demo();        // ctor
d.x = 10;                    // field
d.SetX(20);                  // instance method
console.log(d.x);

// stdlib
const arr = zts.new_szarray_by_element_type(zts.types.int32, 2);
```

Hand-written regression / matrix tests can stay under `StreamingAssets/Tests/Js` (plain JS). Migrating to TypeScript is **optional**.

---

## Usage B: TypeScript workflow

ZTS ships an **official TypeScript workflow**: author in TS, generate `csharp:` declarations, type-check before Play; the runtime **still executes emitted JS only** (never `.ts`).

Full contract: [TypeScript workflow](https://zts.code-philosophy.com/) (`Docs/spec/14-TYPESCRIPT.md`).

### 1. Scaffold the project

Unity menus:

1. **`ZTS/Init TypeScript Project`** — copy package scaffold to project-root `TsProject/`
2. Run `npm install` under `TsProject/` (`typescript`, `esbuild` as devDependencies)
3. **`ZTS/Generate Typings`** — emit `TsProject/generated/csharp/**` (same type set as Il2Cpp Generate)
4. **`ZTS/Compile TypeScript`** — `tsc --noEmit` + esbuild/tsc emit

Layout:

```text
<UnityProject>/
  TsProject/
    src/           # business .ts (versioned)
    generated/     # csharp: decls (versioned)
    out/           # emitted .js (gitignore)
  Assets/StreamingAssets/ZTS/   # Player build copy
```

### 2. Author in TypeScript

```typescript
// TsProject/src/game/logic.ts
import { Demo } from "csharp:Assembly-CSharp/Demo"; // example; follow generated decls

export function OnTick(dt: number): void {
  const d = new Demo();
  d.SetX(20);
}

export function add(a: number, b: number): number {
  return a + b;
}
```

Rules of thumb:

| Topic | Rule |
|-------|------|
| Canonical | `game/logic` (**no** `.ts` / `.js`) |
| C# types | `import { T } from "csharp:…"` (**no** `import type`—type objects are runtime values) |
| Relative imports | `./foo.js` allowed (Node16 style); loader normalizes to canonical |
| Check | `tsc --noEmit` (Play gate on by default; Settings can disable) |
| Emit | esbuild **1:1 ESM, no bundle** (or tsc emit to the same `outDir`) |

### 3. Bind TS exports from C#

```csharp
// jsModule = canonical, no extension
var onTick = JsAppDomain.GetFunction<System.Action<float>>("game/logic", "OnTick");
var add = JsAppDomain.GetFunction<System.Func<int, int, int>>("game/logic", "add");
```

Editor: `moduleLoader` reads `TsProject/out/{canonical}.js`.  
Player: build copies `out/**/*.js` → `StreamingAssets/ZTS/`; runtime reads **only** StreamingAssets.

Play mode runs `tsc --noEmit` by default. You can also run **`ZTS/Compile TypeScript`** manually.

---

## Assemblies

| Assembly | Platform | Role |
|----------|----------|------|
| `ZTS.Common` | All | `JsAppDomain` façade, attributes, shared types |
| `ZTS.Mono` | Editor | QuickJS P/Invoke + Mono callback gate + Emit |
| `ZTS.Il2Cpp` | Player | Il2Cpp host wiring (`ZTS~/zts-runtime`) |
| `ZTS.Editor` | Editor | Install / Export / TypeScript toolchain / Settings |

---

## Editor native deps (Mono)

```text
Plugins/quickjs/
  win32-x64/quickjs.dll     # QuickJS Win64 (no NAN boxing: JSValue = 16 bytes)
  zts_mono_gate.dll         # Editor-only JS→C# callback gate
```

Build (from the package root):

```bat
ZTS~\mono-native\build_quickjs_msvc.bat
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ZTS~\mono-native\build_zts_mono_gate.ps1
```

Il2Cpp: menu **ZTS / Install...** installs `ZTS~/zts-runtime` + QuickJS into LocalIl2Cpp; **ZTS / Export Build-Win64...** exports an MSBuildable Player solution.

---

## License

MIT. Free to use, modify, and distribute.

## Contact

- Docs: [https://zts.code-philosophy.com/](https://zts.code-philosophy.com/)
- Email: `zts@code-philosophy.com`
- Site: [code-philosophy.com](https://code-philosophy.com)
- QQ group: `1095435513` (ZTS community)
- Discord: [https://discord.gg/5bT7w9aRMz](https://discord.gg/5bT7w9aRMz)
- Unreal (WIP): [zts-ue](https://github.com/focus-creative-games/zts-ue)
