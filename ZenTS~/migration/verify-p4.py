"""Verify P4 browser acceptance and native regressions against current artifacts."""
from pathlib import Path
import datetime, hashlib, json, re, subprocess, sys
ROOT=Path(__file__).resolve().parents[2]
PROJECT=ROOT.parents[1]
def sha(p):
    h=hashlib.sha256()
    with p.open("rb") as f:
        for chunk in iter(lambda:f.read(1024*1024),b""):h.update(chunk)
    return h.hexdigest()
def require(text,*markers):
    for m in markers:
        if m not in text:raise RuntimeError("Missing acceptance: "+m)
def main():
    names=["p4-webgl-build.log","p4-webgl-browser.log","p4-editor.log","p4-domain-reload.log","p4-msbuild.log","p4-player.log","p4-player-native.log"]
    logs={n:(PROJECT/n).read_text(encoding="utf-8-sig",errors="replace") for n in names}
    require(logs["p4-webgl-build.log"],"[ZenTS][P4] WebGL build PASS")
    browser=logs["p4-webgl-browser.log"]
    require(browser,"Player engine=HostJs (platform VM)","summary: total=56 passed=56 failed=0","Test summary: total=10 passed=10 failed=0","[ZenTS.TsDemo] ALL OK","Browser GC PASS","Browser exception/UTF8 boundary PASS","Browser HostJs full acceptance PASS")
    if "BROWSER EXCEPTION" in browser or "[matrix] FAIL" in browser:raise RuntimeError("Browser failures recorded")
    skips=[x for x in browser.splitlines() if "[P4][SKIP]" in x]
    if len(skips)!=1 or "managed worker-thread" not in skips[0]:raise RuntimeError("Unexpected skipped cases")
    require(logs["p4-editor.log"],"Actual Editor engine=V8:13.6.233.17","summary: total=56 passed=56 failed=0","Test summary: total=8 passed=8 failed=0","Editor V8 acceptance ALL OK","[ZenTS.TsDemo] ALL OK")
    require(logs["p4-player.log"],"summary: total=56 passed=56 failed=0","Test summary: total=10 passed=10 failed=0","Il2CppSmokeRunner ALL OK","[ZenTS.TsDemo] ALL OK")
    require(logs["p4-player.log"]+logs["p4-player-native.log"],"Player engine=V8 (static)")
    require(logs["p4-domain-reload.log"],"Domain Reload PASS: native runtime count 1 -> 0 -> 1")
    if re.search(r"error (?:LNK|C[0-9]|MSB)",logs["p4-msbuild.log"]):raise RuntimeError("Win64 compile errors")
    wasm=PROJECT/"Build-WebGL/Build/Build-WebGL.wasm"
    code="const fs=require('fs');const m=new WebAssembly.Module(fs.readFileSync(process.argv[1]));console.log(JSON.stringify({imports:WebAssembly.Module.imports(m),exports:WebAssembly.Module.exports(m)}));"
    metadata=json.loads(subprocess.check_output(["node","-e",code,str(wasm)],text=True))
    if not any(x["name"]=="HostOperation" for x in metadata["imports"]):raise RuntimeError("Missing host JS import")
    for name in ["zents_host_callback","zents_host_finalize","zents_host_module_source"]:
        if not any(x["name"]==name for x in metadata["exports"]):raise RuntimeError("Missing wasm export "+name)
    if any("v8" in x["name"].lower() for x in metadata["imports"]):raise RuntimeError("Embedded V8 import in WebGL")
    raw=wasm.read_bytes()
    if b"v8::Isolate" in raw or b"v8::Context" in raw:raise RuntimeError("V8 SDK symbols in WebGL")
    source=ROOT/"ZenTS~/zents-runtime"
    copies={"local_install":PROJECT/"Library/ZenTS/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/zents","win64_export":PROJECT/"Build-Win64/Il2CppOutputProject/IL2CPP/libil2cpp/zents"}
    for label,destination in copies.items():
        for p in source.rglob("*"):
            if not p.is_file():continue
            rel=p.relative_to(source)
            if "generated" in rel.parts or p.name=="V8Libraries.h":continue
            if not (destination/rel).exists() or sha(p)!=sha(destination/rel):raise RuntimeError("Runtime copy mismatch: "+str(destination/rel))
        if any((destination/"hostjs").glob("*.cpp")):raise RuntimeError("Retired HostJs stub remains in "+label)
    lock=json.loads((ROOT/"ZenTS~/v8/sdk-lock.json").read_text(encoding="utf-8"))
    if lock.get("provider")!="official-v8-source" or lock.get("upstream_patches"):raise RuntimeError("V8 provenance mismatch")
    for item in json.loads((ROOT/"ZenTS~/v8/win64-build.json").read_text(encoding="utf-8"))["artifacts"]:
        if sha(ROOT/item["path"])!=item["sha256"]:raise RuntimeError("Native V8 artifact changed")
    for script in ["verify-boundaries.py","verify-contract.py"]:subprocess.run([sys.executable,str(Path(__file__).with_name(script))],check=True)
    subprocess.run(["node",str(PROJECT/"TsProject/node_modules/typescript/bin/tsc"),"--noEmit","-p",str(PROJECT/"TsProject/tsconfig.json")],check=True)
    # Recompile the recorded module input with the current builder; generated output
    # must match the exact .jslib used by the WebGL build.
    import tempfile
    compiler=ROOT/"ZenTS~/host-js/build-modules.mjs"
    toolchain=PROJECT/"TsProject"
    diagnostics=[]
    with tempfile.TemporaryDirectory(prefix="zents-p4-modules-") as folder:
        folder=Path(folder);output=folder/"modules.jslib"
        subprocess.run(["node",str(compiler),str(PROJECT/"Library/ZenTS/HostJs/modules.json"),str(output),str(toolchain)],check=True,capture_output=True)
        if sha(output)!=sha(PROJECT/"Assets/ZenTSHostGenerated/ZenTSModules.jslib"):raise RuntimeError("Current module compiler differs from built JS")
        for name,code in [("dynamic_import","export const load=()=>import('./other.js');"),("import_meta","export const path=import.meta.url;"),("top_level_await","export const value=await Promise.resolve(1);")]:
            fixture=folder/(name+".json");fixture.write_text(json.dumps({"modules":[{"name":name,"source":code}]}),encoding="utf-8")
            run=subprocess.run(["node",str(compiler),str(fixture),str(output),str(toolchain)],capture_output=True,text=True,encoding="utf-8",errors="replace")
            if run.returncode==0:raise RuntimeError("Unsupported module syntax accepted: "+name)
            diagnostics.append(name)
    gc=json.loads(re.search(r"Browser GC PASS (\{[^\n]+\})",browser).group(1))
    case=PROJECT/"Assets/StreamingAssets/Tests/Js/cases/p3/tc_alignment.js"
    artifacts=[wasm,PROJECT/"Build-WebGL/Build/Build-WebGL.framework.js",PROJECT/"Build-WebGL/Build/Build-WebGL.data",PROJECT/"Assets/ZenTSHostGenerated/ZenTSModules.jslib",PROJECT/"Build-Win64/build/bin/x64/Debug/GameAssembly.dll"]
    owned=[]
    for folder in [ROOT/"Runtime",ROOT/"Editor",source,ROOT/"Plugins/WebGL",ROOT/"ZenTS~/host-js"]:
        for p in folder.rglob("*"):
            if p.is_file() and p.suffix in [".cs",".cpp",".h",".jslib",".mjs"] and "sdk" not in p.relative_to(ROOT).parts and "generated" not in p.relative_to(ROOT).parts:owned.append(p)
    result={"verified_utc":datetime.datetime.now(datetime.timezone.utc).isoformat(),"scope":"P4 Unity 2022.3.62f3 Chrome WebGL + Windows Editor and Win64 regressions","webgl":{"engine":"platform VM; no embedded V8","js_suites":{"passed":56,"total":56},"smoke":{"passed":10,"total":10},"typescript_demos":{"passed":3,"total":3},"skipped_cases":skips,"gc":gc,"wasm_imports":metadata["imports"],"host_exports":[x for x in metadata["exports"] if x["name"].startswith("zents_host_")],"exception_utf8_boundary":"passed"},"editor":{"engine":"official V8","smoke":"8/8","js_suites":"56/56","typescript_demos":"3/3","domain_reload":[1,0,1]},"win64":{"engine":"official static V8","smoke":"10/10","js_suites":"56/56","typescript_demos":"3/3","existing_export_reused":True},"module_diagnostics_rejected":diagnostics,"current_module_compiler_matches_build":True,"alignment_boundary_cases":len(re.findall(r"\btest_\w+\s*\(",case.read_text(encoding="utf-8"))),"architecture_guard":"passed","sdk_independent_cpp_contract":"passed","typescript_no_emit":"passed","runtime_copies_match":True,"artifacts":{str(p.relative_to(PROJECT)):sha(p) for p in artifacts},"source_hashes":{p.relative_to(ROOT).as_posix():sha(p) for p in sorted(set(owned))},"test_case_sha256":sha(case),"logs":{n:{"path":str(PROJECT/n),"sha256":sha(PROJECT/n)} for n in names},"not_verified":["WeChat","other browsers/devices","macOS","iOS","Android"]}
    Path(__file__).with_name("p4-verification.json").write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print("P4 PASS: WebGL JS 56/56, Smoke 10/10, TS 3/3; host GC + Reset; Editor/Win64 regressions; no embedded V8.")
if __name__=="__main__":main()
