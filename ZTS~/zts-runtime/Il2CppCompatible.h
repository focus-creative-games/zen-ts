#pragma once

/*
 * Multi Unity / Tuanjie il2cpp API shim (spec 11-MULTI-VERSION §12).
 */

#include "il2cpp-config.h"
#include "il2cpp-api-types.h"
#include "il2cpp-object-internals.h"
#include "il2cpp-class-internals.h"
#include "il2cpp-tabledefs.h"
#include "gc/WriteBarrier.h"
#include "utils/Memory.h"
#include "vm/Array.h"
#include "vm/Class.h"
#include "vm/Object.h"
#include "vm/MetadataCache.h"
#include "vm/Reflection.h"
#if ZTS_UNITY_VERSION >= 20220000
#include "metadata/GenericMethod.h"
#endif

#if IL2CPP_SIZEOF_VOID_P == 8
#define ZTS_ARCH_64 1
#else
#define ZTS_ARCH_32 1
#endif

inline void* ZtsIl2CppCalloc(size_t count, size_t size)
{
#if ZTS_TUANJIE_ENGINE
    return il2cpp::utils::Memory::Calloc(count, size, IL2CPP_MEM_STRING);
#else
    return il2cpp::utils::Memory::Calloc(count, size);
#endif
}

inline void ZtsIl2CppFree(void* memory)
{
    il2cpp::utils::Memory::Free(memory);
}
