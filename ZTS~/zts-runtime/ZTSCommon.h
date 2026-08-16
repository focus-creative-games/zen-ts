#pragma once

/*
 * ZTS Il2Cpp common include (spec 11-MULTI-VERSION §12).
 * Include order: ZTSConf.inc → QuickJsCompatible → Il2CppCompatible.
 */

#include "generated/ZTSConf.inc"

#ifndef ZTS_UNITY_VERSION
#error "ZTS_UNITY_VERSION must be defined by generated/ZTSConf.inc"
#endif
#ifndef ZTS_TUANJIE_ENGINE
#error "ZTS_TUANJIE_ENGINE must be defined by generated/ZTSConf.inc"
#endif

#include "QuickJsCompatible.h"
#include "Il2CppCompatible.h"

namespace zts
{
}
