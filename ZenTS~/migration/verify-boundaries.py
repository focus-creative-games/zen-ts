"""P1 architecture guard; run with Python 3.9+, no third-party packages."""
from pathlib import Path
import re
import sys
ROOT = Path(__file__).resolve().parents[2]
runtime = ROOT / "ZenTS~" / "zents-runtime"
errors = []
for p in runtime.rglob("*"):
    if p.suffix not in (".h", ".cpp") or "generated" in p.relative_to(runtime).parts:
        continue
    if any(p.is_relative_to(runtime / "engine" / name) for name in ("v8",)):
        continue
    text = p.read_text(encoding="utf-8")
    text = re.sub(r"/\*.*?\*/|//[^\n]*", "", text, flags=re.S)
    code = re.sub(r'"(?:\\.|[^"\\])*"', '""', text)
    if re.search(r"\b(?:JSValue(?:Const)?|JSContext|JSRuntime|JS_[A-Za-z0-9_]+)\b", code):
        errors.append(f"native consumer bypasses EngineApi: {p.relative_to(ROOT)}")
    if re.search(r'#include.*(?:v8\.h|sdk/)', text, re.I):
        errors.append(f"native consumer imports an engine SDK: {p.relative_to(ROOT)}")
mono = ROOT / "Runtime" / "Mono"
for p in mono.rglob("*.cs"):
    if any(p.is_relative_to(mono / "Engine" / name) for name in ("V8",)):
        continue
    text = p.read_text(encoding="utf-8")
    text = re.sub(r"/\*.*?\*/|//[^\n]*", "", text, flags=re.S)
    if re.search(r"\b(?:JSValue)\b|\[DllImport", text):
        errors.append(f"managed consumer bypasses EngineApi: {p.relative_to(ROOT)}")
    if p.name != "EngineValue.cs" and re.search(r"\.UInt64\b|\.Ptr\b|\.Tag\b|\.Float64\b", text):
        # TypeCode.UInt64 is an enum member, not a payload read.
        stripped = text.replace("TypeCode.UInt64", "")
        if re.search(r"\.UInt64\b|\.Ptr\b|\.Tag\b|\.Float64\b", stripped):
            errors.append(f"managed consumer reads backend value payload: {p.relative_to(ROOT)}")
for p in (mono / "Binding").rglob("*.cs"):
    text = p.read_text(encoding="utf-8")
    if re.search(r"\b(?:JsValue|JSValue|EngineApi|JsEnv|JsCFunction)\b|using ZenTS\.Jvm", text):
        errors.append(f"managed metadata plan contains engine state: {p.relative_to(ROOT)}")
plan = (runtime / "binding" / "BindingPlan.h").read_text(encoding="utf-8")
if re.search(r"#include.*(?:EngineApi|ZenTSCommon|Il2CppCompatible|v8)", plan):
    errors.append("BindingPlan.h must build without engine / CLR SDK headers")
if errors:
    print("\n".join(errors))
    sys.exit(1)
print("P1 boundaries PASS: native and managed consumers use owned APIs; CLR plans have no engine SDK dependency")
