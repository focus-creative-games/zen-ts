/* Vendored with quickjs-il2cpp. MSVC / Il2Cpp Bee shims. */
#pragma once
#ifndef CONFIG_VERSION
#define CONFIG_VERSION "2026-06-04"
#endif
/* Leave CONFIG_ATOMICS undefined; quickjs.c skips it under _MSC_VER. */
#ifdef _MSC_VER
#pragma warning(disable:4146) /* unary minus on unsigned in dtoa.c */
#ifndef __builtin_expect
#define __builtin_expect(x, y) (x)
#endif
#ifndef likely
#define likely(x) (!!(x))
#endif
#ifndef unlikely
#define unlikely(x) (!!(x))
#endif
#ifndef js_likely
#define js_likely(x) (!!(x))
#endif
#ifndef js_unlikely
#define js_unlikely(x) (!!(x))
#endif
#ifndef __attribute__
#define __attribute__(x)
#endif
#ifndef __attribute
#define __attribute(x)
#endif
#ifndef force_inline
#define force_inline __forceinline
#endif
#ifndef js_force_inline
#define js_force_inline __forceinline
#endif
#ifndef __builtin_unreachable
#define __builtin_unreachable() __assume(0)
#endif
#include <intrin.h>
#include <stdint.h>
#include <stdlib.h>
#include <malloc.h>
#include <math.h>
#include <string.h>
#include <time.h>
/* Redirect QuickJS abort() → log file/line, no CRT "abort() has been called" popup. */
__declspec(noreturn) void zts_qjs_abort(const char* file, int line);
#undef abort
#define abort() zts_qjs_abort(__FILE__, __LINE__)
#ifndef alloca
#define alloca _alloca
#endif
#ifndef ssize_t
typedef intptr_t ssize_t;
#endif
static __forceinline int zts_builtin_clz(unsigned int x) { unsigned long i; _BitScanReverse(&i, x); return (int)(31 - i); }
static __forceinline int zts_builtin_clzll(unsigned long long x) { unsigned long i; _BitScanReverse64(&i, x); return (int)(63 - i); }
static __forceinline int zts_builtin_ctz(unsigned int x) { unsigned long i; _BitScanForward(&i, x); return (int)i; }
static __forceinline int zts_builtin_ctzll(unsigned long long x) { unsigned long i; _BitScanForward64(&i, x); return (int)i; }
#ifndef __builtin_clz
#define __builtin_clz zts_builtin_clz
#endif
#ifndef __builtin_clzll
#define __builtin_clzll zts_builtin_clzll
#endif
#ifndef __builtin_ctz
#define __builtin_ctz zts_builtin_ctz
#endif
#ifndef __builtin_ctzll
#define __builtin_ctzll zts_builtin_ctzll
#endif
#ifndef _SYS_TIME_H_
#define _SYS_TIME_H_
struct timeval { long tv_sec; long tv_usec; };
static __forceinline int gettimeofday(struct timeval *tp, void *tz) { (void)tz; if (tp) { tp->tv_sec = 0; tp->tv_usec = 0; } return 0; }
#endif
#ifndef NAN
static __forceinline double zts_nan_value(void)
{
    union { uint64_t u; double d; } x;
    x.u = 0x7FF8000000000000ULL;
    return x.d;
}
#define NAN (zts_nan_value())
#endif
#endif /* _MSC_VER */
