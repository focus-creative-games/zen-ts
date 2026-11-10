// Copyright 2026 Code Philosophy. MIT License.
#if defined(__EMSCRIPTEN__)
#include "JsLoader.h"
#include "JsEnv.h"
#include <emscripten.h>
#include <cstdlib>
#include <cstring>
extern "C" EMSCRIPTEN_KEEPALIVE char* zents_host_module_source(const char* name)
{
    try {std::string source;zents::JsLoader::LoadModuleSource(name,source);char* p=(char*)std::malloc(source.size()+1);std::memcpy(p,source.c_str(),source.size()+1);return p;}
    catch(...){js::ThrowTypeError(zents::JsEnv::GetContext(),"module loader failed: %s",name);return nullptr;}
}
#endif
