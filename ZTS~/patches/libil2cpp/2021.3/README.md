# libil2cpp 2021.3 patches (ZTS)

Install: floor — greatest `{X.Y.Z}.patch` with version **<=** Editor (letter suffixes ignored).

| File | Covers | Contents |
|------|--------|----------|
| `2021.3.0.patch` | `2021.3.0`–`2021.3.13` | `TsAppDomain::Initialize` hook; Debug `il2cpp_assert` → `zts_il2cpp_assert.log` |
| `2021.3.14.patch` | `2021.3.14`–`2021.3.30` | same (Runtime context drift) |
| `2021.3.31.patch` | `2021.3.31`+ | same (Runtime context drift) |

No `default.patch`. ZTS does **not** backport `GenericMetadata::ContainsGenericParameters` (unlike ZLua); early-2021 gaps are handled in `zts-runtime/Il2CppCompatible.h` where needed.
