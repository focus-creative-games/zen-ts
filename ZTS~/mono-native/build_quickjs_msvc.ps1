# Build quickjs.dll (x64) for ZTS Editor using MSVC.
# Usage: from Developer PowerShell or after vcvars64.
$ErrorActionPreference = "Stop"
$pkgRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$qjsSrc = "D:\workspace\zts\quickjs"
$outDir = Join-Path $pkgRoot "Plugins\quickjs\win32-x64"
$stageDir = Join-Path $PSScriptRoot "_build_qjs"
New-Item -ItemType Directory -Force -Path $outDir, $stageDir | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC not found" }
$aux = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

$ver = (Get-Content (Join-Path $qjsSrc "VERSION") -Raw).Trim()
$shim = Join-Path $PSScriptRoot "win_msvc_shim.h"
$def = Join-Path $PSScriptRoot "quickjs.def"

# Export major C API symbols via .def generated later if needed; use /LD with default exports of public symbols.
# Compile as C with JS_NAN_BOXING for uint64 JSValue ABI.

$sources = @(
  "quickjs.c",
  "libregexp.c",
  "libunicode.c",
  "cutils.c",
  "dtoa.c",
  "quickjs-libc.c"
)

$objs = @()
$compileCmd = @"
call "$aux"
cd /d "$stageDir"
"@

foreach ($s in $sources) {
  $base = [IO.Path]::GetFileNameWithoutExtension($s)
  $obj = "$base.obj"
  $objs += $obj
  $srcPath = Join-Path $qjsSrc $s
  $compileCmd += @"

cl /nologo /c /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS /DCONFIG_VERSION=`"$ver`" /DJS_NAN_BOXING /DWIN32 /D_WIN32 /DCONFIG_WIN32 /UCONFIG_ATOMICS /FI`"$shim`" /I`"$qjsSrc`" /Fo$obj `"$srcPath`"
if errorlevel 1 exit /b 1
"@
}

$outDll = Join-Path $outDir "quickjs.dll"
$compileCmd += @"

link /nologo /DLL /OUT:"$outDll" $($objs -join ' ') /DEF:"$def"
if errorlevel 1 exit /b 1
"@

# Minimal .def - we'll expand after dumpbin if needed
if (-not (Test-Path $def)) {
  @"
LIBRARY quickjs
EXPORTS
    JS_NewRuntime
    JS_FreeRuntime
    JS_NewContext
    JS_FreeContext
    JS_GetRuntime
    JS_Eval
    JS_Call
    JS_GetException
    JS_IsError
    JS_Throw
    JS_ThrowTypeError
    JS_NewInt32
    JS_NewFloat64
    JS_NewBool
    JS_NewString
    JS_NewStringLen
    JS_NewObject
    JS_NewArray
    JS_NewCFunction2
    JS_SetPropertyStr
    JS_GetPropertyStr
    JS_DefinePropertyValueStr
    JS_ToInt32
    JS_ToFloat64
    JS_ToBool
    JS_ToCStringLen2
    JS_FreeCString
    JS_IsFunction
    JS_IsObject
    JS_IsString
    JS_IsNumber
    JS_IsUndefined
    JS_IsNull
    JS_IsException
    JS_IsBool
    JS_VALUE_GET_NORM_TAG
    JS_DupValueRT
    JS_FreeValueRT
    __JS_FreeValue
    JS_SetModuleLoaderFunc
    JS_SetModuleLoaderFunc2
    JS_ResolveModule
    JS_EvalFunction
    JS_GetGlobalObject
    JS_SetHostData
    JS_GetHostData
    JS_NewClassID
    JS_NewClass
    JS_NewObjectClass
    JS_SetOpaque
    JS_GetOpaque
    JS_GetOpaque2
    JS_NewPromiseCapability
    js_std_init_handlers
    js_std_free_handlers
    js_std_add_helpers
    js_std_loop
    js_module_set_import_meta
    js_module_loader
"@ | Set-Content $def -Encoding ASCII
}

cmd /c $compileCmd
if ($LASTEXITCODE -ne 0) { throw "quickjs build failed: $LASTEXITCODE" }
Write-Host "Built $outDll"
Get-Item $outDll | Format-List FullName, Length, LastWriteTime
