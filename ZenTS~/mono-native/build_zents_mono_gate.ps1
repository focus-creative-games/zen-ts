# Build zents_mono_gate.dll (x64) for ZenTS Editor using MSVC.
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Packages/com.code-philosophy.zen-ts/ZenTS~/mono-native/build_zents_mono_gate.ps1
$ErrorActionPreference = "Stop"
$pkgRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$outDir = Join-Path $pkgRoot "Plugins\quickjs"
# Use a staging dir outside ZenTS~ — tilde paths break MSVC cmd lines on some setups.
$stageDir = Join-Path (Split-Path $pkgRoot -Parent) "_zents_gate_build"
New-Item -ItemType Directory -Force -Path $outDir, $stageDir | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC not found" }
$aux = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

$srcOrig = Join-Path $PSScriptRoot "zents_mono_gate.c"
# Copy out of ZenTS~ — tilde paths break MSVC on some setups (silent stale link).
$src = Join-Path $stageDir "zents_mono_gate.c"
Copy-Item -Force $srcOrig $src
$outDll = Join-Path $outDir "zents_mono_gate.dll"
$obj = Join-Path $stageDir "zents_mono_gate.obj"

$cmd = @"
call "$aux"
cd /d "$stageDir"
cl /nologo /c /O2 /MD /W3 /DWIN32 /D_WIN32 /Fo"zents_mono_gate.obj" "zents_mono_gate.c"
if errorlevel 1 exit /b 1
link /nologo /DLL /OUT:"$outDll" zents_mono_gate.obj
if errorlevel 1 exit /b 1
"@

cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "zents_mono_gate build failed: $LASTEXITCODE" }
Write-Host "Built $outDll"
Get-Item $outDll | Format-List FullName, Length, LastWriteTime
