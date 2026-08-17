#!/usr/bin/env bash
# Build quickjs.dylib (arm64) for ZenTS Editor Mono.
# Usage: ./build_quickjs_darwin.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
QJS_SRC="${ZENTS_QJS_SRC:-$ROOT/ZenTS~/quickjs-il2cpp}"
NATIVE="$(cd "$(dirname "$0")" && pwd)"
OUT_DIR="$ROOT/Plugins/quickjs/darwin-arm64"
STAGE="${TMPDIR:-/tmp}/zents_qjs_darwin_build"
VER="$(tr -d '[:space:]' < "$QJS_SRC/VERSION")"

mkdir -p "$OUT_DIR" "$STAGE"
rm -rf "${STAGE:?}/"*
cd "$STAGE"

CFLAGS=(
  -O2 -fPIC -std=gnu17
  -DCONFIG_VERSION="\"$VER\""
  -UCONFIG_ATOMICS
  -include "$QJS_SRC/zents_il2cpp_config.h"
  -I"$QJS_SRC"
  -Wno-unused-parameter -Wno-unused-function
)

SOURCES=(quickjs.c libregexp.c libunicode.c cutils.c dtoa.c)
OBJS=()
for s in "${SOURCES[@]}"; do
  o="${s%.c}.o"
  echo "cc $s"
  cc "${CFLAGS[@]}" -c -o "$o" "$QJS_SRC/$s"
  OBJS+=("$o")
done

echo "cc zents_qjs_std_stubs.c"
cc "${CFLAGS[@]}" -c -o zents_qjs_std_stubs.o "$QJS_SRC/zents_qjs_std_stubs.c"
OBJS+=(zents_qjs_std_stubs.o)

echo "cc zents_jsvalue_abi.c"
cc "${CFLAGS[@]}" -c -o zents_jsvalue_abi.o "$NATIVE/zents_jsvalue_abi.c"
OBJS+=(zents_jsvalue_abi.o)

OUT="$OUT_DIR/quickjs.dylib"
echo "link $OUT"
cc -dynamiclib -o "$OUT" "${OBJS[@]}" \
  -install_name "@rpath/quickjs.dylib" \
  -compatibility_version 1.0.0 -current_version 1.0.0

# Also provide libquickjs.dylib for loaders that prefer the lib- prefix.
cp -f "$OUT" "$OUT_DIR/libquickjs.dylib"

file "$OUT"
ls -la "$OUT_DIR"
echo "Built $OUT"
