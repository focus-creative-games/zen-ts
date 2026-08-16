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
