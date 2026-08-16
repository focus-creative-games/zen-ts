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
