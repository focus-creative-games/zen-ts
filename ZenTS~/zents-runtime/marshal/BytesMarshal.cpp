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

#include "BytesMarshal.h"

#include "vm/Array.h"
#include "vm/Class.h"

#include <cstring>

namespace zents
{
void BytesMarshal::Js2CsBytes(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo*)
{
    if (js::IsNull(value)) { *reinterpret_cast<Il2CppArray**>(address)=nullptr; return; }
    size_t length=0; const uint8_t* data=js::GetByteArrayData(ctx,value,&length);
    if(!data){js::ThrowTypeError(ctx,"zents: Bytes requires an attached Uint8Array");return;}
    Il2CppArray* array=il2cpp::vm::Array::New(il2cpp_defaults.byte_class,(il2cpp_array_size_t)length);
    if(length)std::memcpy(il2cpp::vm::Array::GetFirstElementAddress(array),data,length);
    *reinterpret_cast<Il2CppArray**>(address)=array;
}
js::Value BytesMarshal::Cs2JsBytes(js::Context* ctx, void* address, const MarshalMetaInfo*)
{
    Il2CppArray* array=*reinterpret_cast<Il2CppArray**>(address);
    if(!array)return js::Null();
    return js::NewByteArrayCopy(ctx,reinterpret_cast<uint8_t*>(il2cpp::vm::Array::GetFirstElementAddress(array)),il2cpp::vm::Array::GetLength(array));
}
void BytesMarshal::Js2CsBytesTypeMismatch(js::Context* ctx, js::ValueConst, void*, const MarshalMetaInfo*) {js::ThrowTypeError(ctx,"zents: Bytes requires byte[]");}
js::Value BytesMarshal::Cs2JsBytesTypeMismatch(js::Context* ctx, void*, const MarshalMetaInfo*) {return js::ThrowTypeError(ctx,"zents: Bytes requires byte[]");}
}
