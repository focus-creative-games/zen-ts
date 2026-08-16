# quickjs-il2cpp (vendored)

Il2Cpp Player 用的 QuickJS 白名单源码。Install **整目录拷贝**到 `Local.../libil2cpp/quickjs`，**无** patch 流水线。

| 项 | 值 |
|----|-----|
| Upstream pin | `VERSION` → `2026-06-04` |
| 不含 | `quickjs-libc.c`、`qjs.c`、`qjsc.c`、`run-test262.c` |
| libc 替代 | `zts_qjs_std_stubs.c` + `quickjs-libc.h` |

## MSVC / Bee 适配（相对上游）

- `zts_il2cpp_config.h`：仅由各 `.c` 顶部 `#include`（**不要**再塞进 `quickjs.h`，以免与 winsock `timeval` 冲突）
- `DIRECT_DISPATCH=0`（MSVC 无 computed goto）
- 关闭 `CONFIG_ATOMICS` / `CONFIG_STACK_CHECK`（无 pthread）
- 去掉 `sys/time.h`；`1.0/0.0` → `INFINITY`；`buf` 初始化（C4703）
- `JS_MKVAL`/`JS_NAN` C++ 安全写法；去掉无意义的 `(JSValue)`/`(JSValueConst)` 强制转换
- Bee 合编：`libregexp` 的 `is_digit`→`lre_is_digit`；`dtoa` 与 `quickjs` 冲突的 static 加 `js_dtoa_` 前缀；`unicode_char_range_s`；`dtoa.h` include guard
- `quickjs.h`：MSVC 下提供 `NAN`；`__attribute` 空宏在 config 中
- **MSVC enum 位域**：`JSClosureVar.closure_type` 改为 `uint8_t : 3`（有符号 `:3` 会把 4..7 读坏，eval 闭包时 `abort()`）
- Dev：`zts_il2cpp_config.h` 把 QuickJS `abort()` 重定向到写 `zts_il2cpp_assert.log` + `_exit(3)`（无 CRT 弹窗）

升版本：覆盖上游后保留上述改动，更新 `VERSION`。
