#pragma once

#include "../ZTSCommon.h"

namespace zts
{
class JsLib
{
public:
    static void RegisterGlobals(JSContext* ctx);
    static void Reset(JSContext* ctx);
};
}
