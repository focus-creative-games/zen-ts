"""Compile/run the P1 public headers without engine or CLR SDK include paths."""
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile


def main():
    env = os.environ.copy()
    windows = platform.system() == "Windows"
    compiler = shutil.which("cl") if windows else shutil.which("c++")
    if windows and not compiler:
        vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
        install = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-property", "installationPath"], text=True).strip()
        setup = Path(install) / "VC/Auxiliary/Build/vcvars64.bat"
        command = 'cmd.exe /d /s /c ""' + str(setup) + '" >nul && set"'
        result = subprocess.run(command, capture_output=True, text=True, check=True)
        for line in result.stdout.splitlines():
            if "=" in line:
                key, value = line.split("=", 1)
                if key.upper() in {"PATH", "INCLUDE", "LIB", "LIBPATH", "VCTOOLSINSTALLDIR"}:
                    env[key] = value
        compiler = str(Path(env["VCToolsInstallDir"]) / "bin/Hostx64/x64/cl.exe")
    if not compiler:
        raise RuntimeError("C++17 compiler not found")
    source = Path(__file__).with_name("contract-smoke.cpp").resolve()
    with tempfile.TemporaryDirectory(prefix="zents-p1-contract-") as folder:
        output = Path(folder)
        executable = output / ("contract.exe" if windows else "contract")
        args = ([compiler, "/nologo", "/std:c++17", "/utf-8", "/EHsc", str(source), "/Fo" + str(output / "contract.obj"), "/Fe" + str(executable)]
                if windows else [compiler, "-std=c++17", str(source), "-o", str(executable)])
        subprocess.run(args, env=env, check=True, cwd=output)
        subprocess.run([str(executable)], check=True)
    print("P1 SDK-independent contract smoke PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as ex:
        print(str(ex), file=sys.stderr)
        sys.exit(1)
