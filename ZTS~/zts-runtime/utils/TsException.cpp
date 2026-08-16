#include "TsException.h"
#include "MetadataUtil.h"

#include "vm/Exception.h"
#include "vm/Object.h"
#include "vm/Runtime.h"
#include "vm/String.h"
#include "gc/WriteBarrier.h"

#include <cstdarg>
#include <cstdio>
#include <string>

namespace zts
{
namespace
{
std::string FormatStringV(const char* format, va_list args)
{
    va_list argsCopy;
    va_copy(argsCopy, args);
#if IL2CPP_COMPILER_MSVC
    int n = _vscprintf(format, argsCopy);
#else
    int n = vsnprintf(nullptr, 0, format, argsCopy);
#endif
    va_end(argsCopy);
    if (n < 0)
        return std::string();

    std::string ret((size_t)n + 1, '\0');
    vsnprintf(&ret[0], ret.size(), format, args);
    if (!ret.empty() && ret[ret.size() - 1] == '\0')
        ret.resize(ret.size() - 1);
    return ret;
}
} // namespace

void TsException::Throw(const char* message)
{
    Il2CppClass* klass = MetadataUtil::GetTsScriptExceptionClass();
    Il2CppException* ex = nullptr;
    if (klass != nullptr)
    {
        ex = (Il2CppException*)il2cpp::vm::Object::New(klass);
        il2cpp::vm::Runtime::ObjectInit((Il2CppObject*)ex);
        if (message != nullptr)
            IL2CPP_OBJECT_SETREF(ex, message, il2cpp::vm::String::New(message));
    }
    else
    {
        ex = il2cpp::vm::Exception::GetInvalidOperationException(
            message != nullptr ? message : "zts error");
    }
    il2cpp::vm::Exception::Raise(ex);
}

void TsException::Throw(const std::string& message)
{
    Throw(message.c_str());
}

void TsException::ThrowFormat(const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    const std::string message = FormatStringV(fmt, args);
    va_end(args);
    Throw(message.c_str());
}

void TsException::Throw(Il2CppException* e)
{
    il2cpp::vm::Exception::Raise(e);
}
}
