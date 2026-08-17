#include "zents_il2cpp_config.h"
#include "quickjs.h"
#include "quickjs-libc.h"

/* Win/Il2Cpp: replace POSIX quickjs-libc.c with no-op / minimal stubs. */

void js_std_init_handlers(JSRuntime *rt) { (void)rt; }
void js_std_free_handlers(JSRuntime *rt) { (void)rt; }
void js_std_add_helpers(JSContext *ctx, int argc, char **argv)
{
    (void)ctx;
    (void)argc;
    (void)argv;
}

void js_std_loop(JSContext *ctx) { (void)ctx; }
JSValue js_std_await(JSContext *ctx, JSValue obj)
{
    (void)ctx;
    return obj;
}

void js_std_dump_error(JSContext *ctx) { (void)ctx; }

uint8_t *js_load_file(JSContext *ctx, size_t *pbuf_len, const char *filename)
{
    (void)ctx;
    (void)filename;
    if (pbuf_len)
        *pbuf_len = 0;
    return NULL;
}

JSModuleDef *js_init_module_std(JSContext *ctx, const char *module_name)
{
    (void)ctx;
    (void)module_name;
    return NULL;
}

JSModuleDef *js_init_module_os(JSContext *ctx, const char *module_name)
{
    (void)ctx;
    (void)module_name;
    return NULL;
}

JSModuleDef *js_module_loader(JSContext *ctx, const char *module_name, void *opaque,
                              JSValueConst attributes)
{
    (void)ctx;
    (void)module_name;
    (void)opaque;
    (void)attributes;
    return NULL;
}

int js_module_set_import_meta(JSContext *ctx, JSValueConst func_val, JS_BOOL use_realpath,
                              JS_BOOL is_main)
{
    (void)ctx;
    (void)func_val;
    (void)use_realpath;
    (void)is_main;
    return 0;
}

int js_module_test_json(JSContext *ctx, JSValueConst attributes)
{
    (void)ctx;
    (void)attributes;
    return 0;
}

int js_module_check_attributes(JSContext *ctx, void *opaque, JSValueConst attributes)
{
    (void)ctx;
    (void)opaque;
    (void)attributes;
    return 0;
}

void js_std_eval_binary(JSContext *ctx, const uint8_t *buf, size_t buf_len, int flags)
{
    (void)ctx;
    (void)buf;
    (void)buf_len;
    (void)flags;
}

void js_std_eval_binary_json_module(JSContext *ctx, const uint8_t *buf, size_t buf_len,
                                    const char *module_name)
{
    (void)ctx;
    (void)buf;
    (void)buf_len;
    (void)module_name;
}

void js_std_promise_rejection_tracker(JSContext *ctx, JSValueConst promise, JSValueConst reason,
                                      JS_BOOL is_handled, void *opaque)
{
    (void)ctx;
    (void)promise;
    (void)reason;
    (void)is_handled;
    (void)opaque;
}

void js_std_set_worker_new_context_func(JSContext *(*func)(JSRuntime *rt))
{
    (void)func;
}

#include <stdio.h>
#include <stdlib.h>

/* Called via #define abort() in zents_il2cpp_config.h (QuickJS .c TUs only). */
#ifdef _MSC_VER
__declspec(noreturn) void zents_qjs_abort(const char* file, int line)
#else
__attribute__((noreturn)) void zents_qjs_abort(const char* file, int line)
#endif
{
    char message[2048];
    snprintf(
        message,
        sizeof(message),
        "========== QUICKJS abort() ==========\n"
        "file: %s\n"
        "line: %d\n"
        "=====================================\n\n",
        file != NULL ? file : "(null)",
        line);

    fputs(message, stderr);
    fflush(stderr);

    {
        const char* primary = getenv("ZENTS_ASSERT_LOG");
        if (primary != NULL && primary[0] != '\0')
        {
            FILE* f = fopen(primary, "a");
            if (f != NULL)
            {
                fputs(message, f);
                fflush(f);
                fclose(f);
            }
        }
    }

    {
        FILE* f = fopen("zents_il2cpp_assert.log", "a");
        if (f != NULL)
        {
            fputs(message, f);
            fflush(f);
            fclose(f);
        }
    }

#ifdef _MSC_VER
    {
        const char* tempDir = getenv("TEMP");
        if (tempDir == NULL || tempDir[0] == '\0')
            tempDir = getenv("TMP");
        if (tempDir != NULL && tempDir[0] != '\0')
        {
            char tempPath[1024];
            snprintf(tempPath, sizeof(tempPath), "%s\\zents_il2cpp_assert.log", tempDir);
            FILE* f = fopen(tempPath, "a");
            if (f != NULL)
            {
                fputs(message, f);
                fflush(f);
                fclose(f);
            }
        }
    }

    /* Never show CRT "abort() has been called" dialog. */
    _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
    _exit(3);
#else
    _Exit(3);
#endif
}
