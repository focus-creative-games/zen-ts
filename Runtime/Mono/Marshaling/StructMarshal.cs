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

using System;
using ZTS.Jvm;
using ZTS.Mt;

namespace ZTS.Marshaling
{
    internal static class StructMarshal
    {
        public static JSValue PushByVal(IntPtr ctx, object structValue)
        {
            Type type = structValue.GetType();
            // Box as CLR object handle with value-type instance proto (copy semantics via slot).
            JSValue proto = default;
            if (TypeRegistry.TryGetBinding(type, out TypeBinding binding))
            {
                proto = JsValueUtil.GetNormTag(binding.ByValInstanceProto) == JsValueUtil.TagObject
                    ? binding.ByValInstanceProto
                    : binding.InstanceProto;
            }
            else
            {
                try
                {
                    JsEnv env = JsEnv.FromContext(ctx);
                    TypeBinding ensured = TypeRegistry.EnsureBinding(env, type);
                    proto = JsValueUtil.GetNormTag(ensured.ByValInstanceProto) == JsValueUtil.TagObject
                        ? ensured.ByValInstanceProto
                        : ensured.InstanceProto;
                }
                catch
                {
                    // fall through — handle without proto
                }
            }

            // Store a boxed copy so mutations don't affect caller's stack copy unexpectedly
            // unless they go through the same handle.
            object copy = structValue;
            JSValue handle = ObjectRegistry.PushObject(ctx, copy, type, proto, udKind: "byval");
            try
            {
                JsEnv env2 = JsEnv.FromContext(ctx);
                if (Emit.MetaBinding.TryWrapMissPublic(env2, handle, out JSValue wrapped))
                {
                    JsValueUtil.Free(ctx, handle);
                    return wrapped;
                }
            }
            catch
            {
                // ignore wrap failures
            }

            return handle;
        }
    }
}
