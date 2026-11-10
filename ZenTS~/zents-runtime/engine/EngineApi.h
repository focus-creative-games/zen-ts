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

#pragma once
#include <cstddef>
#include <cstdint>
#include <exception>

// ZenTS-owned in-process API. No engine or CLR headers are allowed here.
// Value is opaque, borrowed unless stated otherwise. Copying it does not retain it.
// Owned values returned by New/Get/Call/Eval/Dup must be released once.
// SetProperty/GetSet and Throw consume their value arguments, also on failure.
// Values, contexts, atoms and identities cannot cross runtimes or a Reset generation.
// This is a source-level C++ interface, NOT the Mono/plugin or JS/WASM wire ABI.
namespace zents { namespace engine {
struct Runtime;
struct Context;
struct Module;
struct alignas(8) Value { uint64_t opaque[2]; };
using ValueConst = Value;
using Atom = uint32_t;
using ClassID = uint32_t;
using CFunction = Value(Context*, ValueConst, int, ValueConst*);
using CFunctionData = Value(Context*, ValueConst, int, ValueConst*, int, Value*);
using ClassFinalizer = void(Runtime*, Value);
struct ClassDef { const char* class_name; ClassFinalizer* finalizer; };
using ModuleNormalize = char*(Context*, const char*, const char*, void*);
using ModuleLoader = Module*(Context*, const char*, void*);
enum class Backend : uint8_t { V8 = 1, HostJs = 2 };
Backend GetBackend();
// Backend-independent flags. Adapters translate or validate these values.
constexpr int EvalGlobal = 0;
constexpr int EvalModule = 1;
constexpr int EvalCompileOnly = 32;
constexpr int PropertyConfigurableWritableEnumerable = 7;
constexpr int PromisePending = 0;
constexpr int PromiseRejected = 2;
Value Undefined();
Value Null();
Value Exception();
// An identity is an opaque cache key, never a dereferenceable JS heap address.
// Valid only while the corresponding object is retained; V8 must use stable IDs.
void* GetIdentity(ValueConst);
Module* GetCompiledModule(ValueConst);
using ExternalExceptionHandler = Value(Context*, std::exception_ptr);
void SetExternalExceptionHandler(Runtime* rt, ExternalExceptionHandler* handler);
Runtime* NewRuntime();
Context* NewContext(Runtime* rt);
void FreeContext(Context* ctx);
Runtime* GetRuntime(Context* ctx);
void RunGC(Runtime* rt);
Value DupValue(Context* ctx, ValueConst v);
void FreeValue(Context* ctx, Value v);
Value NewObject(Context* ctx);
Value NewArray(Context* ctx);
int HasOwnProperty(Context* ctx, ValueConst object, const char* name);
Value NewByteArrayCopy(Context* ctx, const uint8_t* data, size_t length);
const uint8_t* GetByteArrayData(Context* ctx, ValueConst value, size_t* length);
Value NewObjectClass(Context* ctx, int id);
Value NewBool(Context* ctx, int v);
Value NewInt32(Context* ctx, int32_t v);
Value NewUint32(Context* ctx, uint32_t v);
Value NewInt64(Context* ctx, int64_t v);
Value NewFloat64(Context* ctx, double v);
Value NewString(Context* ctx, const char* v);
Value NewStringLen(Context* ctx, const char* v, size_t size);
int ToBool(Context* ctx, ValueConst v);
int ToInt32(Context* ctx, int32_t* out, ValueConst v);
int ToInt64(Context* ctx, int64_t* out, ValueConst v);
int ToFloat64(Context* ctx, double* out, ValueConst v);
const char* ToCString(Context* ctx, ValueConst v);
const char* ToCStringLen(Context* ctx, size_t* size, ValueConst v);
void FreeCString(Context* ctx, const char* v);
Value GetGlobalObject(Context* ctx);
Value GetException(Context* ctx);
Value Throw(Context* ctx, Value v);
int HasException(Context* ctx);
Value GetPropertyStr(Context* ctx, ValueConst obj, const char* key);
Value GetPropertyUint32(Context* ctx, ValueConst obj, uint32_t key);
int SetPropertyStr(Context* ctx, ValueConst obj, const char* key, Value value);
int DefinePropertyValueStr(Context* ctx, ValueConst obj, const char* key, Value value, int flags);
int SetPropertyUint32(Context* ctx, ValueConst obj, uint32_t key, Value value);
int SetPrototype(Context* ctx, ValueConst obj, ValueConst proto);
int SetConstructorBit(Context* ctx, ValueConst obj, int flag);
Atom NewAtom(Context* ctx, const char* key);
void FreeAtom(Context* ctx, Atom key);
int DefinePropertyGetSet(Context* ctx, ValueConst obj, Atom key, Value get, Value set, int flags);
ClassID NewClassID(ClassID* id);
ClassID GetClassID(Value v);
void SetOpaque(Value obj, void* opaque);
void* GetOpaque(ValueConst obj, ClassID id);
int IsArray(Context* ctx, ValueConst v);
int IsFunction(Context* ctx, ValueConst v);
int IsBigInt(Context* ctx, ValueConst v);
int StrictEq(Context* ctx, ValueConst a, ValueConst b);
Value Eval(Context* ctx, const char* src, size_t len, const char* file, int flags);
Value LoadModule(Context* ctx, const char* base, const char* name);
int PromiseState(Context* ctx, Value v);
Value PromiseResult(Context* ctx, Value v);
int IsJobPending(Runtime* rt);
void StdInitHandlers(Runtime* rt);
void StdFreeHandlers(Runtime* rt);
void StdAddHelpers(Context* ctx, int argc, char** argv);
int IsNumber(ValueConst v);
int IsBool(ValueConst v);
int IsNull(ValueConst v);
int IsUndefined(ValueConst v);
int IsException(ValueConst v);
int IsObject(ValueConst v);
int IsString(ValueConst v);
void FreeRuntime(Runtime*);
int ExecutePendingJob(Runtime*, Context**);
Value Call(Context*, ValueConst function, ValueConst receiver, int argc, ValueConst* argv);
Value NewCFunction(Context*, CFunction*, const char* name, int length);
Value NewCFunctionData(Context*, CFunctionData*, int length, int magic, int count, ValueConst* data);
int NewClass(Runtime*, ClassID, const ClassDef*);
void SetModuleLoaderFunc(Runtime*, ModuleNormalize*, ModuleLoader*, void*);
Value ThrowTypeError(Context*, const char* format, ...);
Value ThrowRangeError(Context*, const char* format, ...);
Value ThrowReferenceError(Context*, const char* format, ...);
Value ThrowInternalError(Context*, const char* format, ...);
} }
namespace js = zents::engine;
