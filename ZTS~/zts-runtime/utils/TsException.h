#pragma once

#include "../Il2CppCompatible.h"
#include <string>
#include <cstdarg>

namespace zts
{
    class TsException
    {
    public:
        static void Throw(const char* message);
        static void Throw(const std::string& message);
        static void ThrowFormat(const char* fmt, ...);
        static void Throw(Il2CppException* e);
    };
}
