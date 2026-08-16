# libil2cpp 2022.3 patches (ZTS)

Install: floor — greatest `{X.Y.Z}.patch` with version **<=** Editor.

| File | Covers |
|------|--------|
| `2022.3.0.patch` | `2022.3.x` — hooks `zts::TsAppDomain::Initialize()` from `Runtime.cpp`; Debug `il2cpp_assert` writes `zts_il2cpp_assert.log` (no CRT popup) |

Authored against Unity 2022.3.62f3. Index hunks may need refresh if stock `Runtime.cpp` drifts.
