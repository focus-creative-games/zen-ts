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

// V8 is supplied as a precompiled static SDK; no V8 source is embedded here.
#if defined(ZENTS_V8_ENGINE_IMPLEMENTATION)
#include "../EngineApi.h"
#include "sdk/include/libplatform/libplatform.h"
#include "sdk/include/v8.h"
#include <algorithm>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
namespace zents
{
namespace engine
{
struct Slot;
struct Module
{
    Context *owner;
    std::string name;
    v8::Global<v8::Module> value;
};
struct Runtime
{
    v8::Isolate *isolate = nullptr;
    v8::Platform *platform = nullptr;
    bool draining = false;
    v8::ArrayBuffer::Allocator *allocator = nullptr;
    std::vector<Context *> contexts;
    std::unordered_set<Slot *> slots;
    std::unordered_map<int, std::vector<Slot *>> identities;
    std::unordered_map<ClassID, ClassFinalizer *> classes;
    ModuleNormalize *normalize = nullptr;
    ModuleLoader *loader = nullptr;
    void *loaderData = nullptr;
    bool jobs = false;
    bool disposing = false;
    ExternalExceptionHandler* exceptionHandler = nullptr;
};
struct Context
{
    Runtime *runtime;
    v8::Global<v8::Context> value;
    Value exception;
    void *opaque = nullptr;
    std::unordered_map<std::string, std::unique_ptr<Module>> modules;
};
struct Slot
{
    Runtime *runtime;
    v8::Global<v8::Value> value;
    int refs = 1;
    ClassID classId = 0;
    void *opaque = nullptr;
    bool finalized = false;
    int identity = 0;
    void *callback = nullptr;
};
namespace
{
int liveRuntimeCount = 0;
constexpr int64_t ObjectTag = -1, StringTag = -7, BigIntTag = -9, SymbolTag = -8, ModuleTag = -3;
int64_t Tag(Value v)
{
    return static_cast<int64_t>(v.opaque[1]);
}
Value Imm(uint64_t bits, int64_t tag)
{
    return Value{{bits, static_cast<uint64_t>(tag)}};
}
Slot *S(Value v)
{
    return reinterpret_cast<Slot *>(v.opaque[0]);
}
bool Heap(Value v)
{
    auto t = Tag(v);
    return t == ObjectTag || t == StringTag || t == BigIntTag || t == SymbolTag;
}
struct Scope
{
    v8::Isolate::Scope isolate;
    v8::HandleScope handles;
    v8::Local<v8::Context> local;
    v8::Context::Scope context;
    Scope(Context *c)
        : isolate(c->runtime->isolate), handles(c->runtime->isolate), local(c->value.Get(c->runtime->isolate)),
          context(local)
    {
    }
};
// Separate helper avoids allowing null input to reach NewFromUtf8.
v8::Local<v8::String> Text(v8::Isolate *i, const char *s)
{
    return v8::String::NewFromUtf8(i, s ? s : "").ToLocalChecked();
}
v8::Local<v8::Value> Local(Context *c, Value v)
{
    auto *i = c->runtime->isolate;
    switch (Tag(v))
    {
    case 0:
        return v8::Integer::New(i, static_cast<int32_t>(v.opaque[0]));
    case 1:
        return v8::Boolean::New(i, v.opaque[0] != 0);
    case 2:
        return v8::Null(i);
    case 3:
        return v8::Undefined(i);
    case 8: {
        double d;
        std::memcpy(&d, &v.opaque[0], 8);
        return v8::Number::New(i, d);
    }
    default:
        if (Heap(v) && S(v) && !S(v)->value.IsEmpty())
            return S(v)->value.Get(i);
        return v8::Undefined(i);
    }
}
void Finalize(Slot *s)
{
    if (s->finalized)
        return;
    s->finalized = true;
    auto f = s->runtime->classes.find(s->classId);
    if (f != s->runtime->classes.end() && f->second)
        f->second(s->runtime, Imm(reinterpret_cast<uint64_t>(s), ObjectTag));
}
void RemoveSlot(Slot *s)
{
    auto *r = s->runtime;
    if (s->identity)
    {
        auto found = r->identities.find(s->identity);
        if (found != r->identities.end())
        {
            auto &bucket = found->second;
            bucket.erase(std::remove(bucket.begin(), bucket.end(), s), bucket.end());
            if (bucket.empty())
                r->identities.erase(found);
        }
    }
    r->slots.erase(s);
    delete s;
}
void Weak(const v8::WeakCallbackInfo<Slot> &info)
{
    auto *s = info.GetParameter();
    Finalize(s);
    s->value.Reset();
    RemoveSlot(s);
}
Value Wrap(Context *c, v8::Local<v8::Value> v)
{
    if (v->IsUndefined())
        return Undefined();
    if (v->IsNull())
        return Null();
    if (v->IsBoolean())
        return NewBool(c, v->BooleanValue(c->runtime->isolate));
    if (v->IsInt32())
        return NewInt32(c, v.As<v8::Int32>()->Value());
    if (v->IsNumber())
        return NewFloat64(c, v.As<v8::Number>()->Value());
    int64_t tag = v->IsString() ? StringTag : v->IsBigInt() ? BigIntTag : v->IsSymbol() ? SymbolTag : ObjectTag;
    // Stable hash buckets intern all objects, including Proxy and module namespace objects.
    auto *i = c->runtime->isolate;
    int identity = v->IsObject() ? v.As<v8::Object>()->GetIdentityHash() : 0;
    if (identity)
    {
        for (auto *old : c->runtime->identities[identity])
        {
            if (!old->value.IsEmpty() && old->value.Get(i)->StrictEquals(v))
            {
                if (old->refs++ == 0)
                    old->value.ClearWeak();
                return Imm(reinterpret_cast<uint64_t>(old), tag);
            }
        }
    }
    auto *s = new Slot();
    s->runtime = c->runtime;
    s->identity = identity;
    s->value.Reset(i, v);
    c->runtime->slots.insert(s);
    if (identity)
        c->runtime->identities[identity].push_back(s);
    return Imm(reinterpret_cast<uint64_t>(s), tag);
}
Value Capture(Context *c, v8::TryCatch &tc)
{
    if (!tc.HasCaught())
        return ThrowInternalError(c, "V8 operation failed without an exception");
    return Throw(c, Wrap(c, tc.Exception()));
}
Context *Current(v8::Isolate *i)
{
    return static_cast<Context *>(i->GetCurrentContext()->GetAlignedPointerFromEmbedderData(1));
}
struct Callback
{
    CFunction *simple = nullptr;
    CFunctionData *data = nullptr;
    int magic = 0;
    int count = 0;
    bool constructible = false;
};
void CallbackInvoke(const v8::FunctionCallbackInfo<v8::Value> &info)
{
    auto *i = info.GetIsolate();
    auto *c = Current(i);
    v8::HandleScope scope(i);
    auto holder = info.Data().As<v8::Object>();
    auto *cb = static_cast<Callback *>(holder->GetAlignedPointerFromInternalField(0));
    if (info.IsConstructCall() && !cb->constructible)
    {
        i->ThrowException(v8::Exception::TypeError(Text(i, "value is not a constructor")));
        return;
    }
    std::vector<Value> args(info.Length());
    for (int n = 0; n < info.Length(); ++n)
        args[n] = Wrap(c, info[n]);
    Value receiver = Wrap(c, info.This()), result = Undefined();
    std::vector<Value> data(cb->count);
    for (int n = 0; n < cb->count; ++n)
    {
        v8::Local<v8::Value> x;
        if (holder->Get(c->value.Get(i), n).ToLocal(&x))
            data[n] = Wrap(c, x);
        else
            data[n] = Undefined();
    }
    try
    {
        result = cb->simple ? cb->simple(c, receiver, info.Length(), args.data())
                            : cb->data(c, receiver, info.Length(), args.data(), cb->magic, data.data());
    }
    catch (...)
    {
        if(c->runtime->exceptionHandler)
        {
            try {result=c->runtime->exceptionHandler(c,std::current_exception());}
            catch (...) {result=ThrowInternalError(c,"zents: external exception translator failed");}
        }
        else result = ThrowInternalError(c, "native callback threw outside its CLR boundary");
    }
    if (IsException(result))
    {
        Value ex = GetException(c);
        i->ThrowException(Local(c, ex));
        FreeValue(c, ex);
    }
    else
    {
        info.GetReturnValue().Set(Local(c, result));
        FreeValue(c, result);
    }
    FreeValue(c, receiver);
    for (auto x : args)
        FreeValue(c, x);
    for (auto x : data)
        FreeValue(c, x);
}
ClassID callbackClass = 0;
void FreeCallback(Runtime *, Value v)
{
    delete static_cast<Callback *>(GetOpaque(v, callbackClass));
}
Value MakeCallback(Context *c, Callback *cb, const char *name, int length, ValueConst *values)
{
    Scope s(c);
    auto *i = c->runtime->isolate;
    if (!callbackClass)
        NewClassID(&callbackClass);
    ClassDef def{"ZenTS.Callback", FreeCallback};
    NewClass(c->runtime, callbackClass, &def);
    auto holderValue = NewObjectClass(c, callbackClass);
    SetOpaque(holderValue, cb);
    auto holder = Local(c, holderValue).As<v8::Object>();
    // Callback code pointer is internal; captured JS values are ordinary object edges so cycles can be collected.
    holder->SetAlignedPointerInInternalField(0, cb);
    for (int n = 0; n < cb->count; ++n)
        holder->Set(s.local, n, Local(c, values[n])).FromMaybe(false);
    auto f =
        v8::Function::New(s.local, CallbackInvoke, holder, length, v8::ConstructorBehavior::kAllow).ToLocalChecked();
    if (name)
        f->SetName(Text(i, name));
    Value result = Wrap(c, f);
    S(result)->callback = cb;
    FreeValue(c, holderValue);
    return result;
}
v8::MaybeLocal<v8::Module> Resolve(v8::Local<v8::Context> context, v8::Local<v8::String> specifier,
                                   v8::Local<v8::FixedArray>, v8::Local<v8::Module> referrer)
{
    auto *c = Current(context->GetIsolate());
    std::string base;
    for (auto &p : c->modules)
        if (p.second->value.Get(c->runtime->isolate) == referrer)
        {
            base = p.first;
            break;
        }
    v8::String::Utf8Value text(c->runtime->isolate, specifier);
    std::string name = *text ? *text : "";
    if (c->runtime->normalize)
    {
        char *n = c->runtime->normalize(c, base.c_str(), name.c_str(), c->runtime->loaderData);
        if (!n)
            return {};
        name = n;
        std::free(n);
    }
    else if (name.size() > 0 && name[0] == '.')
    {
        auto pos = base.find_last_of('/');
        std::string full = (pos == std::string::npos ? "" : base.substr(0, pos + 1)) + name;
        std::vector<std::string> parts;
        size_t off = 0;
        while (off <= full.size())
        {
            size_t end = full.find('/', off);
            auto p = full.substr(off, end == std::string::npos ? end : end - off);
            if (p == "..")
            {
                if (!parts.empty())
                    parts.pop_back();
            }
            else if (p != "." && !p.empty())
                parts.push_back(p);
            if (end == std::string::npos)
                break;
            off = end + 1;
        }
        name.clear();
        for (auto &p : parts)
        {
            if (!name.empty())
                name += '/';
            name += p;
        }
    }
    auto it = c->modules.find(name);
    Module *m = it != c->modules.end() ? it->second.get()
                : c->runtime->loader   ? c->runtime->loader(c, name.c_str(), c->runtime->loaderData)
                                       : nullptr;
    if (!m)
    {
        Value ex = GetException(c);
        if (IsNull(ex) || IsUndefined(ex))
        {
            FreeValue(c, ex);
            ex = NewString(c, ("cannot load module: " + name).c_str());
        }
        c->runtime->isolate->ThrowException(Local(c, ex));
        FreeValue(c, ex);
        return {};
    }
    return m->value.Get(c->runtime->isolate);
}
} // namespace
Backend GetBackend()
{
    return Backend::V8;
}
Value Undefined()
{
    return Imm(0, 3);
}
Value Null()
{
    return Imm(0, 2);
}
Value Exception()
{
    return Imm(0, 6);
}
void *GetIdentity(ValueConst v)
{
    return Heap(v) ? S(v) : nullptr;
}
Module *GetCompiledModule(ValueConst v)
{
    return Tag(v) == ModuleTag ? reinterpret_cast<Module *>(v.opaque[0]) : nullptr;
}
Runtime *NewRuntime()
{
    static std::once_flag once;
    static std::unique_ptr<v8::Platform> platform;
    std::call_once(once, [] {
  #if defined(ZENTS_V8_JITLESS)
      v8::V8::SetFlagsFromString("--jitless");
#endif
      platform = v8::platform::NewDefaultPlatform();
        v8::V8::InitializePlatform(platform.get());
        v8::V8::Initialize();
    });
    auto *r = new Runtime();
    r->platform = platform.get();
    r->allocator = v8::ArrayBuffer::Allocator::NewDefaultAllocator();
    v8::Isolate::CreateParams p;
    p.array_buffer_allocator = r->allocator;
    r->isolate = v8::Isolate::New(p);
    ++liveRuntimeCount;
    r->isolate->SetMicrotasksPolicy(v8::MicrotasksPolicy::kExplicit);
    return r;
}
Context *NewContext(Runtime *r)
{
    v8::Isolate::Scope is(r->isolate);
    v8::HandleScope hs(r->isolate);
    auto *c = new Context();
    c->runtime = r;
    c->exception = Undefined();
    auto local = v8::Context::New(r->isolate);
    local->SetAlignedPointerInEmbedderData(1, c);
    c->value.Reset(r->isolate, local);
    r->contexts.push_back(c);
    return c;
}
void FreeContext(Context *c)
{
    Scope s(c);
    FreeValue(c, c->exception);
    c->exception = Undefined();
    for (auto &p : c->modules)
        p.second->value.Reset();
    c->modules.clear();
    c->value.Reset();
    auto &list = c->runtime->contexts;
    list.erase(std::remove(list.begin(), list.end(), c), list.end());
    delete c;
}
Runtime *GetRuntime(Context *c)
{
    return c->runtime;
}
void RunGC(Runtime *r)
{
    v8::Isolate::Scope is(r->isolate);
    r->isolate->LowMemoryNotification();
}
Value DupValue(Context *, ValueConst v)
{
    if (Heap(v))
    {
        auto *s = S(v);
        if (s->refs++ == 0)
            s->value.ClearWeak();
    }
    return v;
}
void FreeValue(Context *, Value v)
{
    if (Heap(v))
    {
        auto *s = S(v);
        if (--s->refs == 0 && !s->value.IsEmpty())
        {
            if (Tag(v) == ObjectTag)
                s->value.SetWeak(s, Weak, v8::WeakCallbackType::kParameter);
            else
            {
                s->value.Reset();
                RemoveSlot(s);
            }
        }
    }
}
void SetExternalExceptionHandler(Runtime* runtime, ExternalExceptionHandler* handler) { runtime->exceptionHandler=handler; }
Value NewObject(Context *c)
{
    Scope s(c);
    return Wrap(c, v8::Object::New(c->runtime->isolate));
}
Value NewArray(Context *c)
{
    Scope s(c);
    return Wrap(c, v8::Array::New(c->runtime->isolate));
}
int DefinePropertyValueStr(Context *c, ValueConst object, const char *key, Value value, int flags)
{
    Scope scope(c);v8::TryCatch tc(c->runtime->isolate);
    v8::PropertyDescriptor descriptor(Local(c,value),(flags&2)!=0);
    descriptor.set_configurable((flags&1)!=0);descriptor.set_enumerable((flags&4)!=0);
    auto result=Local(c,object).As<v8::Object>()->DefineProperty(scope.local,Text(c->runtime->isolate,key),descriptor);
    FreeValue(c,value);if(result.IsNothing()){Capture(c,tc);return -1;}return result.FromJust()?1:0;
}
int HasOwnProperty(Context *c, ValueConst object, const char *name)
{
    Scope s(c); v8::TryCatch tc(c->runtime->isolate); auto value=Local(c,object);
    if(!value->IsObject())return 0;
    auto result=value.As<v8::Object>()->HasOwnProperty(s.local,Text(c->runtime->isolate,name));
    if(result.IsNothing()){Capture(c,tc);return -1;}return result.FromJust()?1:0;
}
Value NewByteArrayCopy(Context *c, const uint8_t *data, size_t length)
{
    Scope s(c);
    auto buffer = v8::ArrayBuffer::New(c->runtime->isolate, length);
    if (length) std::memcpy(buffer->Data(), data, length);
    return Wrap(c, v8::Uint8Array::New(buffer, 0, length));
}
const uint8_t *GetByteArrayData(Context *c, ValueConst value, size_t *length)
{
    Scope s(c); auto local = Local(c, value); *length = 0;
    if (!local->IsUint8Array()) return nullptr;
    auto array = local.As<v8::Uint8Array>(); auto buffer = array->Buffer();
    if (buffer->WasDetached() || buffer->IsSharedArrayBuffer()) return nullptr;
    *length = array->ByteLength();
    // Empty typed arrays are valid; their backing pointer may be null.
    static const uint8_t empty = 0;
    return *length ? static_cast<const uint8_t*>(buffer->Data()) + array->ByteOffset() : &empty;
}
Value NewObjectClass(Context *c, int id)
{
    Scope s(c);
    auto t = v8::ObjectTemplate::New(c->runtime->isolate);
    t->SetInternalFieldCount(1);
    auto v = Wrap(c, t->NewInstance(s.local).ToLocalChecked());
    S(v)->classId = id;
    return v;
}
Value NewBool(Context *, int v)
{
    return Imm(v ? 1 : 0, 1);
}
Value NewInt32(Context *, int32_t v)
{
    return Imm(static_cast<uint32_t>(v), 0);
}
Value NewUint32(Context *c, uint32_t v)
{
    return v <= INT32_MAX ? NewInt32(c, v) : NewFloat64(c, v);
}
Value NewInt64(Context *c, int64_t v)
{
    return NewFloat64(c, static_cast<double>(v));
}
Value NewFloat64(Context *, double d)
{
    Value v = Imm(0, 8);
    std::memcpy(v.opaque, &d, 8);
    return v;
}
Value NewString(Context *c, const char *v)
{
    return NewStringLen(c, v ? v : "", v ? std::strlen(v) : 0);
}
Value NewStringLen(Context *c, const char *v, size_t n)
{
    Scope s(c);
    v8::Local<v8::String> str;
    if (!v8::String::NewFromUtf8(c->runtime->isolate, v ? v : "", v8::NewStringType::kNormal, static_cast<int>(n))
             .ToLocal(&str))
        return Exception();
    return Wrap(c, str);
}
int ToBool(Context *c, ValueConst v)
{
    Scope s(c);
    return Local(c, v)->BooleanValue(c->runtime->isolate);
}
int ToInt32(Context *c, int32_t *out, ValueConst v)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    auto n = Local(c, v)->Int32Value(s.local);
    if (n.IsNothing())
    {
        Capture(c, tc);
        return -1;
    }
    *out = n.FromJust();
    return 0;
}
int ToInt64(Context *c, int64_t *out, ValueConst v)
{
    double d;
    if (ToFloat64(c, &d, v) < 0)
        return -1;
    *out = static_cast<int64_t>(d);
    return 0;
}
int ToFloat64(Context *c, double *out, ValueConst v)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    auto n = Local(c, v)->NumberValue(s.local);
    if (n.IsNothing())
    {
        Capture(c, tc);
        return -1;
    }
    *out = n.FromJust();
    return 0;
}
const char *ToCStringLen(Context *c, size_t *n, ValueConst v)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    v8::Local<v8::String> str;
    if (!Local(c, v)->ToString(s.local).ToLocal(&str))
    {
        Capture(c, tc);
        return nullptr;
    }
    v8::String::Utf8Value text(c->runtime->isolate, str);
    if (!*text)
        return nullptr;
    size_t len = text.length();
    char *p = static_cast<char *>(std::malloc(len + 1));
    std::memcpy(p, *text, len);
    p[len] = 0;
    if (n)
        *n = len;
    return p;
}
const char *ToCString(Context *c, ValueConst v)
{
    return ToCStringLen(c, nullptr, v);
}
void FreeCString(Context *, const char *v)
{
    std::free(const_cast<char *>(v));
}
Value GetGlobalObject(Context *c)
{
    Scope s(c);
    return Wrap(c, s.local->Global());
}
Value GetException(Context *c)
{
    Value v = c->exception;
    c->exception = Undefined();
    return v;
}
Value Throw(Context *c, Value v)
{
    FreeValue(c, c->exception);
    c->exception = v;
    return Exception();
}
int HasException(Context *c)
{
    return !IsUndefined(c->exception);
}
Value GetPropertyStr(Context *c, ValueConst o, const char *key)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    v8::Local<v8::Value> v;
    if (!Local(c, o)->IsObject())
    {
        ThrowTypeError(c, "property access on non-object");
        return Exception();
    }
    if (!Local(c, o).As<v8::Object>()->Get(s.local, Text(c->runtime->isolate, key)).ToLocal(&v))
        return Capture(c, tc);
    return Wrap(c, v);
}
Value GetPropertyUint32(Context *c, ValueConst o, uint32_t key)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    v8::Local<v8::Value> v;
    if (!Local(c, o)->IsObject())
        return ThrowTypeError(c, "indexed access on non-object");
    if (!Local(c, o).As<v8::Object>()->Get(s.local, key).ToLocal(&v))
        return Capture(c, tc);
    return Wrap(c, v);
}
int SetPropertyStr(Context *c, ValueConst o, const char *key, Value v)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    auto result = Local(c, o).As<v8::Object>()->Set(s.local, Text(c->runtime->isolate, key), Local(c, v));
    FreeValue(c, v);
    if (result.IsNothing())
    {
        Capture(c, tc);
        return -1;
    }
    return result.FromJust() ? 1 : 0;
}
int SetPropertyUint32(Context *c, ValueConst o, uint32_t key, Value v)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    auto result = Local(c, o).As<v8::Object>()->Set(s.local, key, Local(c, v));
    FreeValue(c, v);
    if (result.IsNothing())
    {
        Capture(c, tc);
        return -1;
    }
    return result.FromJust() ? 1 : 0;
}
int SetPrototype(Context *c, ValueConst o, ValueConst proto)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    auto result = Local(c, o).As<v8::Object>()->SetPrototype(s.local, Local(c, proto));
    if (result.IsNothing())
    {
        Capture(c, tc);
        return -1;
    }
    return result.FromJust() ? 1 : 0;
}
int SetConstructorBit(Context *, ValueConst value, int flag)
{
    if (!IsObject(value) || !S(value)->callback)
        return -1;
    static_cast<Callback *>(S(value)->callback)->constructible = flag != 0;
    return 0;
}
Atom NewAtom(Context *c, const char *key)
{
    static Atom next = 1;
    auto id = next++;
    extern std::unordered_map<Atom, std::string> atoms;
    atoms[id] = key;
    return id;
}
std::unordered_map<Atom, std::string> atoms;
void FreeAtom(Context *, Atom key)
{
    atoms.erase(key);
}
int DefinePropertyGetSet(Context *c, ValueConst o, Atom key, Value get, Value set, int flags)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    auto g = Local(c, get), t = Local(c, set);
    v8::PropertyDescriptor d(g, t);
    d.set_enumerable((flags & 4) != 0);
    d.set_configurable((flags & 1) != 0);
    auto result =
        Local(c, o).As<v8::Object>()->DefineProperty(s.local, Text(c->runtime->isolate, atoms.at(key).c_str()), d);
    FreeValue(c, get);
    FreeValue(c, set);
    if (result.IsNothing())
    {
        Capture(c, tc);
        return -1;
    }
    return result.FromJust() ? 1 : 0;
}
ClassID NewClassID(ClassID *id)
{
    static ClassID next = 1;
    *id = next++;
    return *id;
}
ClassID GetClassID(Value v)
{
    return Heap(v) ? S(v)->classId : 0;
}
void SetOpaque(Value v, void *p)
{
    if (Heap(v))
        S(v)->opaque = p;
}
void *GetOpaque(ValueConst v, ClassID id)
{
    return Heap(v) && S(v)->classId == id ? S(v)->opaque : nullptr;
}
int IsArray(Context *c, ValueConst v)
{
    Scope s(c);
    return Local(c, v)->IsArray();
}
int IsFunction(Context *c, ValueConst v)
{
    Scope s(c);
    return Local(c, v)->IsFunction();
}
int IsBigInt(Context *, ValueConst v)
{
    return Tag(v) == BigIntTag;
}
int StrictEq(Context *c, ValueConst a, ValueConst b)
{
    Scope s(c);
    return Local(c, a)->StrictEquals(Local(c, b));
}
Value Eval(Context *c, const char *src, size_t len, const char *file, int flags)
{
    Scope s(c);
    auto *i = c->runtime->isolate;
    v8::TryCatch tc(i);
    v8::Local<v8::String> text;
    if (!v8::String::NewFromUtf8(i, src, v8::NewStringType::kNormal, static_cast<int>(len)).ToLocal(&text))
        return Capture(c, tc);
    bool module = (flags & 3) == EvalModule;
    v8::ScriptOrigin origin(Text(i, file), 0, 0, false, -1, v8::Local<v8::Value>(), false, false, module);
    v8::ScriptCompiler::Source source(text, origin);
    if (module)
    {
        auto found = c->modules.find(file);
        if (found != c->modules.end())
            return Imm(reinterpret_cast<uint64_t>(found->second.get()), ModuleTag);
        v8::Local<v8::Module> compiled;
        if (!v8::ScriptCompiler::CompileModule(i, &source).ToLocal(&compiled))
            return Capture(c, tc);
        auto m = std::make_unique<Module>();
        m->owner = c;
        m->name = file;
        m->value.Reset(i, compiled);
        auto *result = m.get();
        c->modules.emplace(file, std::move(m));
        return Imm(reinterpret_cast<uint64_t>(result), ModuleTag);
    }
    v8::Local<v8::Script> script;
    v8::Local<v8::Value> result;
    if (!v8::ScriptCompiler::Compile(s.local, &source).ToLocal(&script) || !script->Run(s.local).ToLocal(&result))
        return Capture(c, tc);
    c->runtime->jobs = true;
    return Wrap(c, result);
}
Value LoadModule(Context *c, const char *, const char *name)
{
    Scope s(c);
    auto *i = c->runtime->isolate;
    v8::TryCatch tc(i);
    auto found = c->modules.find(name);
    Module *m = found != c->modules.end() ? found->second.get()
                : c->runtime->loader      ? c->runtime->loader(c, name, c->runtime->loaderData)
                                          : nullptr;
    if (!m)
    {
        if (!HasException(c))
            ThrowReferenceError(c, "cannot load module: %s", name);
        return Exception();
    }
    auto mod = m->value.Get(i);
    if (mod->GetStatus() == v8::Module::kUninstantiated && mod->InstantiateModule(s.local, Resolve).IsNothing())
        return Capture(c, tc);
    v8::Local<v8::Value> evaluated;
    if (mod->GetStatus() == v8::Module::kInstantiated)
    {
        if (!mod->Evaluate(s.local).ToLocal(&evaluated))
            return Capture(c, tc);
        i->PerformMicrotaskCheckpoint();
        if (evaluated->IsPromise() && evaluated.As<v8::Promise>()->State() == v8::Promise::kRejected)
            return Throw(c, Wrap(c, evaluated.As<v8::Promise>()->Result()));
        if (evaluated->IsPromise() && evaluated.As<v8::Promise>()->State() == v8::Promise::kPending)
            return Wrap(c, evaluated);
    }
    if (mod->GetStatus() == v8::Module::kErrored)
        return Throw(c, Wrap(c, mod->GetException()));
    auto promise = v8::Promise::Resolver::New(s.local).ToLocalChecked();
    promise->Resolve(s.local, mod->GetModuleNamespace()).FromMaybe(false);
    return Wrap(c, promise->GetPromise());
}
int PromiseState(Context *c, Value v)
{
    Scope s(c);
    auto local = Local(c, v);
    return local->IsPromise() ? static_cast<int>(local.As<v8::Promise>()->State()) : -1;
}
Value PromiseResult(Context *c, Value v)
{
    Scope s(c);
    auto local = Local(c, v);
    return local->IsPromise() ? Wrap(c, local.As<v8::Promise>()->Result()) : Undefined();
}
int IsJobPending(Runtime *r)
{
    return r->jobs;
}
void StdInitHandlers(Runtime *)
{
}
void StdFreeHandlers(Runtime *)
{
}
void StdAddHelpers(Context *c, int, char **)
{
    Value global = GetGlobalObject(c);
    SetPropertyStr(c, global, "__zents_engine", NewString(c, "V8"));
    SetPropertyStr(c, global, "__zents_engine_version", NewString(c, v8::V8::GetVersion()));
    FreeValue(c, global);
}
int IsNumber(ValueConst v)
{
    return Tag(v) == 0 || Tag(v) == 8;
}
int IsBool(ValueConst v)
{
    return Tag(v) == 1;
}
int IsNull(ValueConst v)
{
    return Tag(v) == 2;
}
int IsUndefined(ValueConst v)
{
    return Tag(v) == 3;
}
int IsException(ValueConst v)
{
    return Tag(v) == 6;
}
int IsObject(ValueConst v)
{
    return Tag(v) == ObjectTag;
}
int IsString(ValueConst v)
{
    return Tag(v) == StringTag;
}
void FreeRuntime(Runtime *r)
{
    r->disposing = true;
    while (!r->contexts.empty())
        FreeContext(r->contexts.back());
    {
        v8::Isolate::Scope is(r->isolate);
        v8::HandleScope hs(r->isolate);
        for (auto *s : r->slots)
        {
            Finalize(s);
            s->value.Reset();
            delete s;
        }
        r->slots.clear();
        r->identities.clear();
    }
    v8::platform::NotifyIsolateShutdown(r->platform, r->isolate);
    r->isolate->Dispose();
    delete r->allocator;
    --liveRuntimeCount;
    delete r;
}
int ExecutePendingJob(Runtime *r, Context **out)
{
    Context *context = r->contexts.empty() ? nullptr : r->contexts.front();
    if (out)
        *out = context;
    if (r->draining)
        return 0;
    struct Guard
    {
        Runtime *runtime;
        ~Guard()
        {
            runtime->draining = false;
        }
    } guard{r};
    r->draining = true;
    r->jobs = false;
    v8::Isolate::Scope isolate(r->isolate);
    v8::HandleScope handles(r->isolate);
    v8::TryCatch errors(r->isolate);
    // Foreground tasks include FinalizationRegistry cleanup and V8 compiler/GC work.
    // Never wait for future delayed tasks or block the Unity main thread.
    while (v8::platform::PumpMessageLoop(r->platform, r->isolate, v8::platform::MessageLoopBehavior::kDoNotWait))
    {
        if (errors.HasCaught())
            break;
    }
    r->isolate->PerformMicrotaskCheckpoint();
    if (errors.HasCaught() && context)
    {
        Capture(context, errors);
        return -1;
    }
    return 0;
}

Value Call(Context *c, ValueConst fn, ValueConst receiver, int argc, ValueConst *argv)
{
    Scope s(c);
    v8::TryCatch tc(c->runtime->isolate);
    std::vector<v8::Local<v8::Value>> args(argc);
    for (int n = 0; n < argc; ++n)
        args[n] = Local(c, argv[n]);
    v8::Local<v8::Value> result;
    auto f = Local(c, fn);
    if (!f->IsFunction())
        return ThrowTypeError(c, "value is not a function");
    if (!f.As<v8::Function>()->Call(s.local, Local(c, receiver), argc, args.data()).ToLocal(&result))
        return Capture(c, tc);
    c->runtime->jobs = true;
    return Wrap(c, result);
}
Value NewCFunction(Context *c, CFunction *fn, const char *name, int length)
{
    auto *cb = new Callback();
    cb->simple = fn;
    return MakeCallback(c, cb, name, length, nullptr);
}
Value NewCFunctionData(Context *c, CFunctionData *fn, int length, int magic, int count, ValueConst *data)
{
    auto *cb = new Callback();
    cb->data = fn;
    cb->magic = magic;
    cb->count = count;
    return MakeCallback(c, cb, nullptr, length, data);
}
int NewClass(Runtime *r, ClassID id, const ClassDef *def)
{
    r->classes[id] = def->finalizer;
    return 0;
}
void SetModuleLoaderFunc(Runtime *r, ModuleNormalize *n, ModuleLoader *l, void *p)
{
    r->normalize = n;
    r->loader = l;
    r->loaderData = p;
}
namespace
{
Value Error(Context *c, int kind, const char *format, va_list args)
{
    char buf[4096];
    vsnprintf(buf, sizeof(buf), format, args);
    Scope s(c);
    auto *i = c->runtime->isolate;
    auto text = Text(i, buf);
    v8::Local<v8::Value> e = kind == 1   ? v8::Exception::TypeError(text)
                             : kind == 2 ? v8::Exception::RangeError(text)
                             : kind == 3 ? v8::Exception::ReferenceError(text)
                                         : v8::Exception::Error(text);
    return Throw(c, Wrap(c, e));
}
} // namespace
#define ERROR_FN(name, kind)                                                                                           \
    Value name(Context *c, const char *format, ...)                                                                    \
    {                                                                                                                  \
        va_list args;                                                                                                  \
        va_start(args, format);                                                                                        \
        auto r = Error(c, kind, format, args);                                                                         \
        va_end(args);                                                                                                  \
        return r;                                                                                                      \
    }
ERROR_FN(ThrowTypeError, 1)
ERROR_FN(ThrowRangeError, 2) ERROR_FN(ThrowReferenceError, 3) ERROR_FN(ThrowInternalError, 0)
} // namespace engine
} // namespace zents
#endif
