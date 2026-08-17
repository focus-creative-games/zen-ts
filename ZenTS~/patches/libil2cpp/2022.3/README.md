# libil2cpp 2022.3 patches (ZenTS)

Install: floor — greatest `{X.Y.Z}.patch` with version **<=** Editor (letter suffixes ignored).

| File | Covers | Contents |
|------|--------|----------|
| `2022.3.0.patch` | `2022.3.x` (incl. Tuanjie `…tN` on this line) | `JsAppDomain::Initialize` from `Runtime.cpp`; Debug `il2cpp_assert` → `zents_il2cpp_assert.log` (no CRT popup) |

No `default.patch`. Authored against Unity 2022.3.62f3.

`Array::IndexFromIndices` is **not** patched: early 2022.3 (e.g. 0 / 11) lacks the API; `Il2CppCompatible.h` uses a manual formula when `ZENTS_UNITY_VERSION < 20220324`.

`Assert.cpp.overlay` is the full post-patch Win32 assert source for reference / refresh.
