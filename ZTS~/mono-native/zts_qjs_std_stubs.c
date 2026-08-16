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

/* Minimal stubs if full quickjs-libc is too painful on MSVC. */
#include "quickjs.h"

#ifdef _WIN32
#define ZTS_EXPORT __declspec(dllexport)
#else
#define ZTS_EXPORT
#endif

ZTS_EXPORT void js_std_init_handlers(JSRuntime *rt)
{
    (void)rt;
}

ZTS_EXPORT void js_std_free_handlers(JSRuntime *rt)
{
    (void)rt;
}

ZTS_EXPORT void js_std_add_helpers(JSContext *ctx, int argc, char **argv)
{
    (void)ctx;
    (void)argc;
    (void)argv;
}

ZTS_EXPORT JSModuleDef *js_module_loader(JSContext *ctx,
                                         const char *module_name, void *opaque)
{
    (void)ctx;
    (void)module_name;
    (void)opaque;
    return NULL;
}

ZTS_EXPORT int js_module_set_import_meta(JSContext *ctx, JSValueConst func_val,
                                         JS_BOOL use_realpath, JS_BOOL is_main)
{
    (void)ctx;
    (void)func_val;
    (void)use_realpath;
    (void)is_main;
    return 0;
}
