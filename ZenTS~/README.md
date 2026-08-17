# ZenTS~ package data

See `Docs/spec/11-MULTI-VERSION.md`.

## Layout

| Path | Role |
|------|------|
| `zents-runtime/` | Install → `Local.../libil2cpp/zents` |
| `quickjs-il2cpp/` | Vendored QuickJS for Il2Cpp（整目录拷贝，**无** patch） |
| `patches/libil2cpp/` | Stock libil2cpp hooks only |
| `jslib/` | Embedded JS (`zentslib.js`) |
| `mono-native/` | Editor QuickJS / Mono gate builds |

## Dev loop (Win64)

1. Menu **ZenTS/Install...** (or batch `InstallIl2Cpp`)
2. Menu **ZenTS/Export Build-Win64...** → `Build-Win64/`
3. Edit `Build-Win64/Il2CppOutputProject/IL2CPP/libil2cpp/zents`（及必要时 `quickjs`）
4. Build/test in the exported project
5. `sync-runtime-zents.bat` → `ZenTS~/zents-runtime`（排除 `generated`）；QuickJS 改动手工回写 `ZenTS~/quickjs-il2cpp`
6. Re-Install + clean rebuild when needed
