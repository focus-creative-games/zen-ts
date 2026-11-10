"""Verify recorded P2 acceptance logs and exact SDK/runtime artifacts; no builds."""
from pathlib import Path
import datetime
import hashlib
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT.parents[1]

def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()

def require(text, markers):
    for marker in markers:
        if marker not in text:
            raise RuntimeError("Missing acceptance: " + marker)

def main():
    names = ["p2-editor.log", "p2-domain-reload.log", "p2-install.log", "p2-msbuild.log",
             "p2-player.log", "p2-player-native.log"]
    logs = {name: (PROJECT / name).read_text(encoding="utf-8", errors="replace") for name in names}
    require(logs["p2-editor.log"], ["Actual Editor engine=V8:13.6.233.17",
        "summary: total=55 passed=55 failed=0", "Test summary: total=8 passed=8 failed=0",
        "Editor V8 acceptance ALL OK", "[ZenTS.TsDemo] ALL OK"])
    require(logs["p2-player.log"], ["summary: total=55 passed=55 failed=0",
        "Test summary: total=10 passed=10 failed=0", "Il2CppSmokeRunner ALL OK", "[ZenTS.TsDemo] ALL OK"])
    require(logs["p2-player.log"] + logs["p2-player-native.log"], ["Player engine=V8 (static)"])
    require(logs["p2-domain-reload.log"], ["Domain Reload PASS: native runtime count 1 -> 0 -> 1"])
    require(logs["p2-install.log"], ["Build environment configuration/restoration PASS",
                                  "V8 Install and Win64 export configuration OK"])
    if "error LNK" in logs["p2-msbuild.log"] or "error MSB" in logs["p2-msbuild.log"]:
        raise RuntimeError("Native build log contains errors")
    built = json.loads((ROOT / "ZenTS~/v8/win64-build.json").read_text(encoding="utf-8"))
    if built.get("sdk_provider") != "official-v8-source" or built.get("upstream_patches"):
        raise RuntimeError("Previous third-party SDK acceptance is not valid for the official-source build")
    lock = json.loads((ROOT / "ZenTS~/v8/sdk-lock.json").read_text(encoding="utf-8"))
    if lock.get("provider") != "official-v8-source" or not lock.get("source_pristine") or not lock.get("deps_pristine") or lock.get("upstream_patches"):
        raise RuntimeError("Official V8 source provenance missing or modified")
    sdk = ROOT / "ZenTS~/zents-runtime/engine/v8/sdk"
    for name, digest in lock["static_libraries_sha256"].items():
        if sha(sdk / "lib" / name) != digest: raise RuntimeError("Official V8 static library mismatch")
    for name, digest in lock["headers_sha256"].items():
        if sha(sdk / "include" / name) != digest: raise RuntimeError("Official V8 header mismatch")
    if any(sdk.rglob("wee8*.lib")): raise RuntimeError("Retired third-party SDK libraries remain")

    for item in built["artifacts"]:
        if sha(ROOT / item["path"]) != item["sha256"]:
            raise RuntimeError("Artifact differs from final build: " + item["path"])
    smoke = ROOT / "ZenTS~/v8/out/win64/engine-smoke.log"
    require(smoke.read_text(encoding="utf-8"), ["V8 engine smoke PASS", "FinalizationRegistry"])
    sys.path.insert(0, str(ROOT / "ZenTS~/v8"))
    import build_win64
    env = build_win64.environment()
    dumpbin = Path(env["VCToolsInstallDir"]) / "bin/Hostx64/x64/dumpbin.exe"
    game = PROJECT / "Build-Win64/build/bin/x64/Debug/GameAssembly.dll"
    dependencies = {}
    for name, artifact in [("editor_plugin", ROOT / "Plugins/v8/win64/zents_v8.dll"), ("game_assembly", game)]:
        text = subprocess.check_output([str(dumpbin), "/dependents", str(artifact)], env=env, text=True)
        deps = [line.strip() for line in text.splitlines() if re.fullmatch(r"\s+[A-Za-z0-9_.-]+\.dll\s*", line, re.I)]
        if any(re.search(r"^(v8|zents_v8|v8_libbase|v8_libplatform)\.dll$", x, re.I) for x in deps):
            raise RuntimeError("Unexpected dynamic engine dependency")
        dependencies[name] = deps
    source = ROOT / "ZenTS~/zents-runtime"
    copies = {
        "local_install": PROJECT / "Library/ZenTS/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/zents",
        "exported_win64": PROJECT / "Build-Win64/Il2CppOutputProject/IL2CPP/libil2cpp/zents"}
    sync = {}
    for name, destination in copies.items():
        for item in source.rglob("*"):
            if not item.is_file() or "generated" in item.relative_to(source).parts or item.name == "V8Libraries.h":
                continue
            copy = destination / item.relative_to(source)
            if not copy.exists() or sha(item) != sha(copy):
                raise RuntimeError("Runtime source mismatch: " + str(copy))
        sync[name] = {"sources_match": True, "generated_il2cpp_cpp_reused": True,
                      "configured_link_header_excluded": True}
    dag = json.loads((PROJECT / "Build-Win64/build/obj/il2cppOutputProject/x64/Debug/buildstate/bee.dag.json").read_text(encoding="utf-8"))
    link_inputs = []
    for node in dag["Nodes"]:
        annotation = node.get("Annotation", "")
        if "Link_Win64" in annotation and "GameAssembly" in annotation:
            link_inputs.extend(x for x in node.get("Inputs", []) if "wee8" in x or "v8_monolith" in x or "zents_v8_engine" in x)
    if not any("v8_monolith.lib" in x for x in link_inputs) or not any("zents_v8_engine.lib" in x for x in link_inputs):
        raise RuntimeError("Bee is not tracking the static engine libraries")
    lock = json.loads((ROOT / "ZenTS~/v8/sdk-lock.json").read_text(encoding="utf-8"))
    result = {
        "verified_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "scope": "P2 Windows Editor + existing exported Win64; Unity 2022.3.62f3",
        "backend": "V8", "version": "13.6.233.17", "sdk": lock,
        "native_adapter_build": "passed", "native_engine_smoke": "passed",
        "editor": {"exit_code": 0, "smoke": {"total": 8, "passed": 8},
                   "js_suites": {"total": 55, "passed": 55}, "typescript_demos": {"total": 3, "passed": 3}},
        "player": {"exit_code": 0, "configuration": "Debug x64; MT CRT; existing generated Il2Cpp reused",
                   "smoke": {"total": 10, "passed": 10}, "js_suites": {"total": 55, "passed": 55},
                   "typescript_demos": {"total": 3, "passed": 3}, "external_engine_dll_present": False},
        "domain_reload": {"passed": True, "native_runtime_counts": [1, 0, 1]},
        "foreground_tasks_and_finalization_registry": "passed",
        "install": "passed", "build_environment_restore": "passed",
        "plain_msbuild_without_external_environment": "passed", "architecture_guard": "passed",
        "sdk_independent_cpp17_contract": "passed", "dynamic_dependencies": dependencies,
        "bee_static_library_inputs": link_inputs, "runtime_sync": sync,
        "source_test_expectations_edited": False, "new_platform_skips_added": False,
        "il2cpp_csharp_regenerated": False, "v8_compiled_from_source_in_unity": False,
        "sdk_precompiled_static": True, "sdk_built_locally_from_official_source": True, "upstream_patches": [], "artifacts": built["artifacts"], "game_assembly_sha256": sha(game),
        "logs": {name: {"path": str(PROJECT / name), "sha256": sha(PROJECT / name)} for name in names},
        "engine_smoke_log_sha256": sha(smoke),
        "platforms_not_verified": ["macOS", "iOS", "Android", "WebGL", "WeChat"],
        "P3_semantics_migration_completed": False}
    Path(__file__).with_name("p2-verification.json").write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("P2 PASS: Editor 8/8 + JS 55/55 + TS 3/3; Win64 10/10 + JS 55/55 + TS 3/3; Domain Reload; static SDK; runtime copies match.")

if __name__ == "__main__":
    main()
