# libil2cpp 6000 (Unity 6) patches (ZenTS)

Series dirs: try `6000.{minor}/` first, then this `6000/` fallback.  
Within a dir: floor — greatest `{X.Y.Z}.patch` **<=** Editor.

| File | Covers | Contents |
|------|--------|----------|
| `6000.0.0.patch` | `6000.x.y` when resolved under `6000/` | `JsAppDomain::Initialize`; Debug assert log |

For **6000.5+**, prefer series dir `6000.5/` (`6000.5.0.patch`) — `Runtime.cpp` context diverged from 6000.0.

No `default.patch`. ZenTS does **not** patch `GenericMethod::IsAnUnresolvedCallStubWasNotFound` (ZLua does); unresolved stubs are unused on the ZenTS bridge path today.
