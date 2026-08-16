#pragma once

#include "../marshal/MarshalDefs.h"

namespace zts
{
struct MethodBridgeEntry
{
    const char* stubName;
    FnJs2CsInvoker js2CsInvoker;
};
}
