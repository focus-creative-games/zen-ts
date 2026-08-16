#pragma once

/*
 * Multi Unity / Tuanjie il2cpp API shim (spec 11-MULTI-VERSION §12).
 */

#ifndef ZTS_UNITY_VERSION
#error "ZTS_UNITY_VERSION must be defined by generated/ZTSConf.inc"
#endif
#ifndef ZTS_TUANJIE_ENGINE
#error "ZTS_TUANJIE_ENGINE must be defined by generated/ZTSConf.inc"
#endif

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

namespace zts
{

// Array::IndexFromIndices exists from ~Unity 2022.3.24; earlier 2022.3.x / 2021 use the same formula.
inline il2cpp_array_size_t ArrayIndexFromIndices(Il2CppArray* array, const int32_t* indices)
{
#if ZTS_UNITY_VERSION < 20220324
    Il2CppClass* ac = array->klass;
    il2cpp_array_size_t pos = (il2cpp_array_size_t)(indices[0] - array->bounds[0].lower_bound);
    for (int32_t i = 1; i < ac->rank; i++)
    {
        pos = pos * array->bounds[i].length + (il2cpp_array_size_t)(indices[i] - array->bounds[i].lower_bound);
    }
    return pos;
#else
    return il2cpp::vm::Array::IndexFromIndices(array, indices);
#endif
}

// Unity 6000.5.0+: Object::Unbox renamed to GetRawData (same: payload after Il2CppObject header).
inline void* ObjectUnbox(Il2CppObject* obj)
{
#if ZTS_UNITY_VERSION >= 60000500
    return il2cpp::vm::Object::GetRawData(obj);
#else
    return il2cpp::vm::Object::Unbox(obj);
#endif
}

} // namespace zts
