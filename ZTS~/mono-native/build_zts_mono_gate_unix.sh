#!/usr/bin/env bash
# Build libzts_mono_gate.dylib for ZTS Editor Mono (macOS).
# Usage: ./build_zts_mono_gate_unix.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$(cd "$(dirname "$0")" && pwd)/zts_mono_gate.c"
OUT_DIR="$ROOT/Plugins/quickjs"
OUT="$OUT_DIR/libzts_mono_gate.dylib"

mkdir -p "$OUT_DIR"
cc -O2 -fPIC -dynamiclib -o "$OUT" "$SRC" \
  -install_name "@rpath/libzts_mono_gate.dylib" \
  -compatibility_version 1.0.0 -current_version 1.0.0

# DllImport("zts_mono_gate") resolves libzts_mono_gate.dylib on Darwin.
ls -la "$OUT"
echo "Built $OUT"
