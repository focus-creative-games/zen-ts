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

#include "JsException.h"
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

void JsException::Throw(const char* message)
{
    Il2CppClass* klass = MetadataUtil::GetJsScriptExceptionClass();
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

void JsException::Throw(const std::string& message)
{
    Throw(message.c_str());
}

void JsException::ThrowFormat(const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    const std::string message = FormatStringV(fmt, args);
    va_end(args);
    Throw(message.c_str());
}

void JsException::Throw(Il2CppException* e)
{
    il2cpp::vm::Exception::Raise(e);
}
}
