# ZenTS package data

| Path | Role |
|------|------|
| `zents-runtime/` | Installed native runtime, CLR bridge and static V8 SDK |
| `v8/` | Official-source locks, SDK build tooling and native acceptance test |
| `patches/libil2cpp/` | Stock libil2cpp hooks |
| `jslib/` | Engine-independent JS facade and host-JS prototype |
| `types/` | TypeScript declarations and project scaffold |
| `host-js/` | Host VM transport contract and milestones |
| `migration/` | Alignment evidence, architecture guards and measured acceptance |

## Win64 iteration

1. Build the pinned official V8 SDK outside Unity and run `v8/build_win64.py --sdk <exported SDK>`.
2. Run **ZenTS/Install**, then export **Build-Win64** once.
3. Edit the embedded package and synchronize `zents-runtime/` to the existing export, preserving `generated/` and its configured `V8Libraries.h`.
4. Incrementally build and test the exported project. Regenerate C# output when managed Player code changes.
5. Reinstall when package/native configuration changes.

Current validated targets: Windows x64 Editor and Win64 Player. Other native SDK integrations and the host-JS backend remain pending.
