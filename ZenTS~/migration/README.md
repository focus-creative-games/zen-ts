# ZenTS engine and CLR boundaries

- [P5 原生平台集成](P5-NATIVE-PLATFORMS.md)
- `verify-p5.py` / `p5-verification.json`: SDK provenance, native platform tests and regressions.
- [P4 WebGL platform JS VM](P4-WEBGL.md)
- `verify-p4.py` / `p4-verification.json`: WebGL browser, GC and native regressions.
- [P3 ZLua design alignment and acceptance](P3-ZLUA-ALIGNMENT.md)
- `verify-p3.py` / `p3-verification.json`: current alignment acceptance and source/deployment hashes.
- [V8 Windows Editor / Win64 implementation and acceptance](P2-V8-WIN64.md)
- `verify-boundaries.py`: SDK/ABI dependency guard.
- `verify-contract.py` / `contract-smoke.cpp`: SDK-independent C++17 contract.
- `verify-p2.py` / `p2-verification.json`: runtime, SDK and acceptance evidence.

Native V8 is built from pristine official source as static libraries. HostJs implements the same owned engine boundary using the WebGL platform VM.

P0/P1 baseline documents, rule registers and hash inventories are preserved as historical ZLua alignment evidence. Their engine descriptions reflect the captured baseline, not current backend availability.

- `engine-cleanup-verification.json`: retired backend removal scan and final acceptance summary.
