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
 * Does not modify upstream quickjs sources.
 */
#ifndef ZTS_QUICKJS_MSVC_COMPAT_H
#define ZTS_QUICKJS_MSVC_COMPAT_H

#ifdef _MSC_VER

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

#ifndef __GNUC__
/* silence some attribute-only paths */
#endif

#ifndef force_inline
#define force_inline __forceinline
#endif
#ifndef no_inline
#define no_inline __declspec(noinline)
#endif
#ifndef __maybe_unused
#define __maybe_unused
#endif

#ifndef __builtin_unreachable
#define __builtin_unreachable() __assume(0)
#endif

#include <intrin.h>
#include <stdint.h>
#include <malloc.h>
#include <math.h>

#ifndef alloca
#define alloca _alloca
#endif

#ifndef ssize_t
typedef intptr_t ssize_t;
#endif

/* Approximate GCC atomics for MSVC x64 (single-threaded Editor use is fine; these are still correct). */
#ifndef __atomic_load_n
static __forceinline int __atomic_load_n_int(const volatile int *ptr, int mo)
{
    (void)mo;
    return (int)_InterlockedOr((volatile long *)ptr, 0);
}
#define __atomic_load_n(ptr, mo) \
    _Generic(*(ptr), \
        int: __atomic_load_n_int((const volatile int *)(ptr), (mo)), \
        default: (*(ptr)))
#endif

/* QuickJS mostly uses atomic_add/store on int/refcounts via its own wrappers;
 * provide minimal builtins used by headers. */
#ifndef __atomic_store_n
#define __atomic_store_n(ptr, val, mo) \
    do { (void)(mo); *(ptr) = (val); } while (0)
#endif

#ifndef __atomic_add_fetch
#define __atomic_add_fetch(ptr, val, mo) \
    ((typeof(*(ptr)))(_InterlockedExchangeAdd((volatile long *)(ptr), (long)(val)) + (val)))
#endif

#ifndef __atomic_sub_fetch
#define __atomic_sub_fetch(ptr, val, mo) \
    ((typeof(*(ptr)))(_InterlockedExchangeAdd((volatile long *)(ptr), -(long)(val)) - (val)))
#endif

#ifndef __atomic_compare_exchange_n
#define __atomic_compare_exchange_n(ptr, expected, desired, weak, success, failure) \
    __zts_atomic_compare_exchange_n((volatile long *)(ptr), (long *)(expected), (long)(desired), (weak), (success), (failure))
static __forceinline int __zts_atomic_compare_exchange_n(
    volatile long *ptr, long *expected, long desired, int weak, int success, int failure)
{
    (void)weak; (void)success; (void)failure;
    long prev = _InterlockedCompareExchange(ptr, desired, *expected);
    if (prev == *expected)
        return 1;
    *expected = prev;
    return 0;
}
#endif

#define NAN (*(const double *)(const void *)(const uint64_t[]){0x7FF8000000000000ULL})

#endif /* _MSC_VER */
#endif /* ZTS_QUICKJS_MSVC_COMPAT_H */
