#!/usr/bin/env bash
# Build libzents_mono_gate.dylib for ZenTS Editor Mono (macOS).
# Usage: ./build_zents_mono_gate_unix.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$(cd "$(dirname "$0")" && pwd)/zents_mono_gate.c"
OUT_DIR="$ROOT/Plugins/quickjs"
OUT="$OUT_DIR/libzents_mono_gate.dylib"

mkdir -p "$OUT_DIR"
cc -O2 -fPIC -dynamiclib -o "$OUT" "$SRC" \
  -install_name "@rpath/libzents_mono_gate.dylib" \
  -compatibility_version 1.0.0 -current_version 1.0.0

# DllImport("zents_mono_gate") resolves libzents_mono_gate.dylib on Darwin.
ls -la "$OUT"
echo "Built $OUT"
