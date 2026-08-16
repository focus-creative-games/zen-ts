@echo off
setlocal EnableExtensions
call "d:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" || exit /b 1

set QJS=D:\workspace\zts\quickjs
set OUTDIR=D:\workspace\zts\ZTSTest\Packages\com.code-philosophy.zts\Plugins\quickjs\win32-x64
set STAGEDIR=D:\workspace\zts\ZTSTest\Packages\com.code-philosophy.zts\ZTS~\mono-native\_build_qjs
set SHIM=D:\workspace\zts\ZTSTest\Packages\com.code-philosophy.zts\ZTS~\mono-native\win_msvc_shim.h
set DEF=D:\workspace\zts\ZTSTest\Packages\com.code-philosophy.zts\ZTS~\mono-native\quickjs.def
set VER=2026-06-04

if not exist "%OUTDIR%" mkdir "%OUTDIR%"
if not exist "%STAGEDIR%" mkdir "%STAGEDIR%"
cd /d "%STAGEDIR%" || exit /b 1

set INC=D:\workspace\zts\ZTSTest\Packages\com.code-philosophy.zts\ZTS~\mono-native\qjs_inc
rem Win64: do NOT define JS_NAN_BOXING (upstream uses 16-byte JSValue on PTR64).
set CFLAGS=/nologo /c /O2 /MD /W3 /std:c17 /D_CRT_SECURE_NO_WARNINGS /DCONFIG_VERSION=\"%VER%\" /DWIN32 /D_WIN32 /DCONFIG_WIN32 /UCONFIG_ATOMICS /FI"%SHIM%" /I"%INC%" /I"%QJS%"

echo Compiling quickjs.c ...
cl %CFLAGS% /Foquickjs.obj "%INC%\quickjs_build.c" || exit /b 1
echo Compiling libregexp.c ...
cl %CFLAGS% /Folibregexp.obj "%QJS%\libregexp.c" || exit /b 1
echo Compiling libunicode.c ...
cl %CFLAGS% /Folibunicode.obj "%QJS%\libunicode.c" || exit /b 1
echo Compiling cutils.c ...
cl %CFLAGS% /Focutils.obj "%QJS%\cutils.c" || exit /b 1
echo Compiling dtoa.c ...
cl %CFLAGS% /Fodtoa.obj "%QJS%\dtoa.c" || exit /b 1
echo Compiling zts_qjs_std_stubs.c ...
cl %CFLAGS% /Fozts_qjs_std_stubs.obj "%~dp0zts_qjs_std_stubs.c" || exit /b 1
echo Compiling zts_jsvalue_abi.c ...
cl %CFLAGS% /Fozts_jsvalue_abi.obj "%~dp0zts_jsvalue_abi.c" || exit /b 1

echo Linking quickjs.dll ...
link /nologo /DLL /OUT:"%OUTDIR%\quickjs.dll" quickjs.obj libregexp.obj libunicode.obj cutils.obj dtoa.obj zts_qjs_std_stubs.obj zts_jsvalue_abi.obj /DEF:"%DEF%" || exit /b 1

dir "%OUTDIR%\quickjs.dll"
echo OK
exit /b 0
