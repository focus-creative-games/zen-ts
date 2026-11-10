#if defined(ZENTS_V8_ANDROID_LIBCXX_BRIDGE)
// Copyright 2026 Code Philosophy. MIT License.
// Bridge Chromium's versioned libc++ strings to the NDK-owned exception ABI.
#include <stdexcept>
#include <string>
namespace std
{
    logic_error::logic_error(const string& message) : logic_error(message.c_str()) {}
    runtime_error::runtime_error(const string& message) : runtime_error(message.c_str()) {}
}
_LIBCPP_BEGIN_NAMESPACE_STD
void __throw_runtime_error(const char* message) { throw runtime_error(message); }
_LIBCPP_END_NAMESPACE_STD

#endif
