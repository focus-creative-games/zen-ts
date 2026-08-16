# Build zts_mono_gate.dll (x64) for ZTS Editor using MSVC.
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Packages/com.code-philosophy.zts/ZTS~/mono-native/build_zts_mono_gate.ps1
$ErrorActionPreference = "Stop"
$pkgRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$outDir = Join-Path $pkgRoot "Plugins\quickjs"
# Use a staging dir outside ZTS~ — tilde paths break MSVC cmd lines on some setups.
$stageDir = Join-Path (Split-Path $pkgRoot -Parent) "_zts_gate_build"
New-Item -ItemType Directory -Force -Path $outDir, $stageDir | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC not found" }
$aux = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

$srcOrig = Join-Path $PSScriptRoot "zts_mono_gate.c"
# Copy out of ZTS~ — tilde paths break MSVC on some setups (silent stale link).
$src = Join-Path $stageDir "zts_mono_gate.c"
Copy-Item -Force $srcOrig $src
$outDll = Join-Path $outDir "zts_mono_gate.dll"
$obj = Join-Path $stageDir "zts_mono_gate.obj"

$cmd = @"
call "$aux"
cd /d "$stageDir"
cl /nologo /c /O2 /MD /W3 /DWIN32 /D_WIN32 /Fo"zts_mono_gate.obj" "zts_mono_gate.c"
if errorlevel 1 exit /b 1
link /nologo /DLL /OUT:"$outDll" zts_mono_gate.obj
if errorlevel 1 exit /b 1
"@

cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "zts_mono_gate build failed: $LASTEXITCODE" }
Write-Host "Built $outDll"
Get-Item $outDll | Format-List FullName, Length, LastWriteTime
