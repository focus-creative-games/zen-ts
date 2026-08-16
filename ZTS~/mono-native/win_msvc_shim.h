// Copyright 2026 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

/*
 * Force-include for building QuickJS with MSVC (ZTS Editor plugin).
 */
#ifndef ZTS_QUICKJS_MSVC_COMPAT_H
#define ZTS_QUICKJS_MSVC_COMPAT_H

#ifdef _MSC_VER

#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <time.h>
#include <io.h>
#include <process.h>
#include <fcntl.h>
#include <malloc.h>
#include <math.h>
#include <float.h>
#include <intrin.h>

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

/* GCC attribute forms used by QuickJS (safe to drop on MSVC for this tree). */
#ifndef __attribute__
#define __attribute__(x)
#endif
#ifndef __attribute
#define __attribute(x)
#endif

#undef force_inline
#define force_inline __forceinline
#undef no_inline
#define no_inline __declspec(noinline)
#ifndef __maybe_unused
#define __maybe_unused
#endif
#ifndef __builtin_unreachable
#define __builtin_unreachable() __assume(0)
#endif
#ifndef __builtin_frame_address
/* Only used when CONFIG_STACK_CHECK is enabled. */
#define __builtin_frame_address(n) ((void *)_AddressOfReturnAddress())
#endif
#ifndef alloca
#define alloca _alloca
#endif
#ifndef ssize_t
typedef intptr_t ssize_t;
#endif

#ifndef NAN
static __forceinline double zts_nan(void)
{
    uint64_t x = 0x7FF8000000000000ULL;
    double d;
    memcpy(&d, &x, sizeof(d));
    return d;
}
#define NAN (zts_nan())
#endif

#ifndef INFINITY
#define INFINITY HUGE_VAL
#endif

/* GCC builtins used by QuickJS / dtoa */
static __forceinline int __builtin_clz(unsigned int x)
{
    unsigned long idx;
    if (x == 0)
        return 32;
    _BitScanReverse(&idx, x);
    return 31 - (int)idx;
}

static __forceinline int __builtin_clzll(unsigned long long x)
{
    unsigned long idx;
    if (x == 0)
        return 64;
    _BitScanReverse64(&idx, x);
    return 63 - (int)idx;
}

static __forceinline int __builtin_ctz(unsigned int x)
{
    unsigned long idx;
    if (x == 0)
        return 32;
    _BitScanForward(&idx, x);
    return (int)idx;
}

static __forceinline int __builtin_ctzll(unsigned long long x)
{
    unsigned long idx;
    if (x == 0)
        return 64;
    _BitScanForward64(&idx, x);
    return (int)idx;
}

#endif /* _MSC_VER */
#endif
