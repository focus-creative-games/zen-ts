# -*- coding: utf-8 -*-
"""Regenerate quickjs_build.c for MSVC with safe transforms."""
from pathlib import Path
import re

src = Path(r"D:\workspace\zen-ts\quickjs\quickjs.c")
dst = Path(r"D:\workspace\zen-ts\ZenTSTest\Packages\com.code-philosophy.zen-ts\ZenTS~\mono-native\qjs_inc\quickjs_build.c")
c = src.read_text(encoding="utf-8")

c = c.replace(
    "#if defined(__EMSCRIPTEN__)\n"
    "#define DIRECT_DISPATCH  0\n"
    "#else\n"
    "#define DIRECT_DISPATCH  1\n"
    "#endif",
    "#if defined(__EMSCRIPTEN__) || defined(_MSC_VER)\n"
    "#define DIRECT_DISPATCH  0\n"
    "#else\n"
    "#define DIRECT_DISPATCH  1\n"
    "#endif",
)

c = c.replace("#define CONFIG_ATOMICS", "/* CONFIG_ATOMICS disabled for MSVC ZenTS build */")
c = c.replace("#define CONFIG_STACK_CHECK", "/* CONFIG_STACK_CHECK disabled for MSVC ZenTS build */")
c = c.replace("1.0 / 0.0", "INFINITY").replace("-1.0 / 0.0", "-INFINITY")
c = c.replace("1.0/0.0", "INFINITY").replace("-1.0/0.0", "-INFINITY")

c = c.replace(
    '#include "dtoa.h"',
    '#include "dtoa.h"\n\n'
    "#ifdef _MSC_VER\n"
    "#pragma function(fabs, floor, ceil, sqrt, acos, asin, atan, atan2, cos, exp, log, sin, tan, pow, fmod)\n"
    "#endif",
)

lines = c.splitlines(keepends=True)
c = "".join(ln for ln in lines if not re.match(r"^\s*JS_CFUNC_SPECIAL_DEF\(", ln))

# Protect sizeof / pointer casts / compound literals, then strip identity value casts for MSVC.
c = c.replace("sizeof(JSValue)", "sizeof(JSVALUE_SIZE_PLACEHOLDER)")
c = c.replace("(JSValue *)", "(JSVALUE_PTR_PLACEHOLDER)")
c = c.replace("(JSValueConst *)", "(JSVALUECONST_PTR_PLACEHOLDER)")
c = c.replace("(const JSValue *)", "(CONST_JSVALUE_PTR_PLACEHOLDER)")
c = c.replace("(JSValue){", "(JSVALUE_COMPOUND_PLACEHOLDER){")
c = c.replace("(JSValueConst){", "(JSVALUECONST_COMPOUND_PLACEHOLDER){")
c = c.replace("(JSValue)", "")
c = c.replace("(JSValueConst)", "")
c = c.replace("sizeof(JSVALUE_SIZE_PLACEHOLDER)", "sizeof(JSValue)")
c = c.replace("(JSVALUE_PTR_PLACEHOLDER)", "(JSValue *)")
c = c.replace("(JSVALUECONST_PTR_PLACEHOLDER)", "(JSValueConst *)")
c = c.replace("(CONST_JSVALUE_PTR_PLACEHOLDER)", "(const JSValue *)")
c = c.replace("(JSVALUE_COMPOUND_PLACEHOLDER){", "(JSValue){")
c = c.replace("(JSVALUECONST_COMPOUND_PLACEHOLDER){", "(JSValueConst){")

align_user = """#ifdef _MSC_VER
    __declspec(align(8)) uint8_t user_data[];
#else
    __attribute__((aligned(JS_MALLOC_ALIGN))) uint8_t user_data[];
#endif"""
align_blocks = """#ifdef _MSC_VER
    __declspec(align(8)) uint8_t blocks[];
#else
    __attribute__((aligned(JS_MALLOC_ALIGN))) uint8_t blocks[];
#endif"""

c = c.replace(
    "    __attribute__((aligned(JS_MALLOC_ALIGN))) uint8_t user_data[];",
    align_user,
)
c = c.replace(
    "    __attribute__((aligned(JS_MALLOC_ALIGN))) uint8_t blocks[];",
    align_blocks,
)

# MSVC: enum bitfields are signed — JS_CLOSURE_GLOBAL (5) in a :3 field becomes -3.
c = c.replace(
    "    JSClosureTypeEnum closure_type : 3;",
    "#ifdef _MSC_VER\n"
    "    uint8_t closure_type : 3;\n"
    "#else\n"
    "    JSClosureTypeEnum closure_type : 3;\n"
    "#endif",
)

dst.write_text(c, encoding="utf-8", newline="\n")
print("wrote", dst, "len", len(c))
print("sizeof(JSValue)", len(re.findall(r"sizeof\(JSValue\)", c)))
print("SPECIAL_DEF", len(re.findall(r"JS_CFUNC_SPECIAL_DEF", c)))
print("declspec(align", c.count("__declspec(align"))
print("bare (JSValue)", len(re.findall(r"\(JSValue\)", c)))
print("ptr (JSValue *)", len(re.findall(r"\(JSValue \*\)", c)))
