"""Configure/build an existing, pinned V8 checkout. Does not download or change revisions."""
import argparse
import datetime
import hashlib
import json
import platform
from pathlib import Path
import re
import shutil
import subprocess
import sys


def configuration(target, cpu):
    allowed = {"windows": {"x64"}, "macos": {"x64", "arm64"},
               "android": {"arm64", "x64", "arm"}, "ios": {"arm64"}}
    if cpu not in allowed[target]:
        raise ValueError(f"Unsupported target/cpu pair: {target}/{cpu}")
    args = {"is_debug": False, "is_component_build": False, "v8_monolithic": True,
            "v8_use_external_startup_data": False, "v8_enable_i18n_support": False,
            "use_custom_libcxx": False, "target_cpu": cpu,
            "target_os": {"windows": "win", "macos": "mac", "android": "android", "ios": "ios"}[target]}
    if target == "ios":
        args.update(v8_jitless=True, v8_enable_pointer_compression=False, v8_enable_sandbox=False)
    return args


def host_supports(target):
    host = platform.system()
    return (target == "windows" and host == "Windows" or
            target in {"macos", "ios"} and host == "Darwin" or
            target == "android" and host == "Linux")


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--revision", required=True, help="Exact 40-character source commit; never main/latest")
    parser.add_argument("--target", required=True, choices=["windows", "macos", "android", "ios"])
    parser.add_argument("--cpu", required=True, choices=["x64", "arm64", "arm"])
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--dry-run", action="store_true")
    opts = parser.parse_args()
    if not re.fullmatch(r"[0-9a-fA-F]{40}", opts.revision):
        parser.error("--revision must be an exact full git commit")
    source, out = opts.source.resolve(), opts.out.resolve()
    if source.is_relative_to(Path(__file__).resolve().parents[2]) or out.is_relative_to(Path(__file__).resolve().parents[2]):
        parser.error("V8 checkout and outputs must be outside the read-only package")
    if out == source or out in source.parents:
        parser.error("--out cannot be the source root or its ancestor")
    try:
        args = configuration(opts.target, opts.cpu)
    except ValueError as ex:
        parser.error(str(ex))
    gn_args = " ".join(f"{k}={str(v).lower() if isinstance(v, bool) else json.dumps(v)}" for k, v in sorted(args.items()))
    commands = [["gn", "gen", str(out), "--args=" + gn_args], ["autoninja", "-C", str(out), "v8_monolith"]]
    plan = {"revision": opts.revision.lower(), "source": str(source), "out": str(out),
            "target": opts.target, "cpu": opts.cpu, "jitless": opts.target == "ios",
            "host": platform.platform(), "supported_on_this_host": host_supports(opts.target),
            "gn_args": args, "commands": commands,
            "abi_status": "recipe only; P2 validates and pins a V8 revision plus Unity toolchain"}
    if opts.dry_run:
        print(json.dumps(plan, indent=2))
        return 0
    if not plan["supported_on_this_host"]:
        parser.error("This target requires a supported build host (macOS for iOS/macOS, Linux for Android)")
    actual = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if actual.lower() != opts.revision.lower():
        parser.error(f"Source revision mismatch: expected {opts.revision}, found {actual}")
    dirty = subprocess.check_output(["git", "-C", str(source), "status", "--porcelain", "--untracked-files=no"], text=True)
    if dirty.strip():
        parser.error("Tracked V8 source is dirty; pin patches explicitly before building")
    for cmd in commands:
        executable = shutil.which(cmd[0])
        if not executable:
            parser.error(f"Required depot_tools executable not found: {cmd[0]}")
        cmd[0] = executable
        subprocess.run(cmd, cwd=source, check=True)
    artifacts = [f for f in (out / "obj").rglob("*") if f.name in {"v8_monolith.lib", "libv8_monolith.a"}]
    if not artifacts:
        raise RuntimeError("Build completed without a v8_monolith artifact")
    plan["artifacts"] = [{"path": str(f), "sha256": file_hash(f)} for f in artifacts]
    plan["built_utc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    (out / "zents-v8-build.json").write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as ex:
        print(str(ex), file=sys.stderr)
        sys.exit(1)
