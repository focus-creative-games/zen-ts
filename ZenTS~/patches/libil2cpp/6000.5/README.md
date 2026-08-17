# libil2cpp 6000.5 patches (ZenTS)

Series dirs: try `6000.5/` first, then `6000/` fallback.  
Within a dir: floor — greatest `{X.Y.Z}.patch` **<=** Editor.

| File | Covers | Contents |
|------|--------|----------|
| `6000.5.0.patch` | `6000.5.0+` | `JsAppDomain::Initialize`; Debug assert log (`#if IL2CPP_TARGET_WINDOWS`) |

Notes vs `6000/6000.0.0.patch`:
- `Runtime.cpp` include / init line numbers differ
- Stock Win32 `Assert.cpp` guard is `IL2CPP_TARGET_WINDOWS` only

`Object::Unbox` → `GetRawData` on 6000.5+ is handled in `Il2CppCompatible.h` (`ObjectUnbox`), not by patching libil2cpp.
