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

#include <stdint.h>
#include <string.h>

#ifdef _WIN32
#define ZTS_GATE_EXPORT __declspec(dllexport)
#else
#define ZTS_GATE_EXPORT __attribute__((visibility("default")))
#endif

typedef struct JSContext JSContext;

/* Win64 QuickJS ABI: non-NAN-boxing JSValue is 16 bytes. */
typedef struct JSValue {
    union {
        uint64_t uint64;
        double float64;
        void *ptr;
    } u;
    int64_t tag;
} JSValue;

/* Managed callbacks use pointer ABI for all JSValues (Mono-safe). */
typedef void (*ZtsManagedCallback)(JSContext *ctx, JSValue *this_val, int argc,
                                   JSValue *argv, int magic, JSValue *out);

typedef void (*JSThrowOutFunc)(JSContext *ctx, JSValue *obj, JSValue *out);

#ifndef ZTS_CALLBACK_ERROR_SENTINEL
#define ZTS_CALLBACK_ERROR_SENTINEL ((int32_t)0xFFFF5A12)
#endif

#define MAX_CALLBACKS 8192

static JSThrowOutFunc g_js_throw = 0;
static int32_t g_error_sentinel_tag = ZTS_CALLBACK_ERROR_SENTINEL;
static JSValue g_pending_exception;

static ZtsManagedCallback g_callbacks[MAX_CALLBACKS];
static int g_callback_count = 0;

static int is_error_sentinel(JSValue v)
{
    return (int32_t)v.tag == g_error_sentinel_tag;
}

/* QuickJS sees this as JSCFunctionMagic (MSVC/native ABI). */
static JSValue zts_callback_gate(JSContext *ctx, JSValue this_val, int argc, JSValue *argv, int magic)
{
    JSValue zero;
    JSValue ret;
    memset(&zero, 0, sizeof(zero));
    memset(&ret, 0, sizeof(ret));

    if (magic < 0 || magic >= g_callback_count || g_callbacks[magic] == 0) {
        if (g_js_throw) {
            g_js_throw(ctx, &g_pending_exception, &ret);
            return ret;
        }
        return zero;
    }

    g_callbacks[magic](ctx, &this_val, argc, argv, 0, &ret);
    if (is_error_sentinel(ret)) {
        JSValue exc = g_pending_exception;
        memset(&g_pending_exception, 0, sizeof(g_pending_exception));
        if (g_js_throw) {
            g_js_throw(ctx, &exc, &ret);
            return ret;
        }
        return exc;
    }
    return ret;
}

ZTS_GATE_EXPORT void zts_gate_init(JSThrowOutFunc js_throw, int error_sentinel_tag)
{
    g_js_throw = js_throw;
    g_error_sentinel_tag = (int32_t)error_sentinel_tag;
    memset(&g_pending_exception, 0, sizeof(g_pending_exception));
}

ZTS_GATE_EXPORT JSValue (*zts_get_callback_gate(void))(JSContext *, JSValue, int, JSValue *, int)
{
    return &zts_callback_gate;
}

ZTS_GATE_EXPORT int zts_register_callback(ZtsManagedCallback fn)
{
    if (g_callback_count >= MAX_CALLBACKS || fn == 0) {
        return -1;
    }
    int id = g_callback_count++;
    g_callbacks[id] = fn;
    return id;
}

ZTS_GATE_EXPORT void zts_set_pending_exception(JSValue *value)
{
    if (value) {
        g_pending_exception = *value;
    } else {
        memset(&g_pending_exception, 0, sizeof(g_pending_exception));
    }
}

/* Returns 1 and clears pending into *out when a pending exception exists. */
ZTS_GATE_EXPORT int zts_take_pending_exception(JSValue *out)
{
    if (!out) {
        return 0;
    }
    /* tag==0 and ptr==0 => empty for our ABI zeroing convention */
    if (g_pending_exception.tag == 0 && g_pending_exception.u.uint64 == 0) {
        memset(out, 0, sizeof(*out));
        return 0;
    }
    *out = g_pending_exception;
    memset(&g_pending_exception, 0, sizeof(g_pending_exception));
    return 1;
}

ZTS_GATE_EXPORT void zts_gate_reset(void)
{
    /* Caller must Free any pending exception via zts_take_pending_exception first. */
    memset(&g_pending_exception, 0, sizeof(g_pending_exception));
    memset(g_callbacks, 0, sizeof(g_callbacks));
    g_callback_count = 0;
}

ZTS_GATE_EXPORT int zts_callback_error_sentinel(void)
{
    return (int)g_error_sentinel_tag;
}
