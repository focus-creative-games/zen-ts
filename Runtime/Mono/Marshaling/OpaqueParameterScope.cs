using System;
using ZTS.Jvm;
using ZTS.Mt;

namespace ZTS.Marshaling
{
    /// <summary>
    /// OpaqueValue handles for ref/in/out default marshal.
    /// Frame generation: Enter/Leave invalidate handles after the C#↔JS call returns.
    /// </summary>
    internal static class OpaqueParameterScope
    {
        private static int _generation = 1;

        public static void Enter()
        {
            // Nested Enter keeps the same generation so nested callbacks share the frame.
        }

        public static void Leave(IntPtr ctx)
        {
            unchecked
            {
                _generation++;
                if (_generation <= 0)
                {
                    _generation = 1;
                }
            }
        }

        public static void Reset()
        {
            unchecked
            {
                _generation++;
                if (_generation <= 0)
                {
                    _generation = 1;
                }
            }
        }

        public static JSValue Push(IntPtr ctx, object target)
        {
            var opaque = new OpaqueValue(target) { Generation = _generation };
            JSValue handle = QuickJsDll.JS_NewObject(ctx);
            int slot = ObjectRegistry.Register(opaque);
            QuickJsDll.JS_SetPropertyStr(ctx, handle, "__zts_id", JsValueUtil.NewInt32(slot));
            QuickJsDll.JS_SetPropertyStr(ctx, handle, "__zts_opaque", JsValueUtil.NewBool(true));
            opaque.JsValue = handle;
            return handle;
        }

        public static JSValue PushExisting(IntPtr ctx, OpaqueValue opaque)
        {
            EnsureAlive(opaque);
            if (JsValueUtil.GetNormTag(opaque.JsValue) == JsValueUtil.TagObject)
            {
                return JsValueUtil.Dup(opaque.JsValue);
            }

            return Push(ctx, opaque.Target);
        }

        public static bool TryPop(IntPtr ctx, JSValue jsValue, out object target)
        {
            target = null;
            if (!ObjectRegistry.TryGetObject(ctx, jsValue, out object obj))
            {
                return false;
            }

            if (obj is OpaqueValue opaque)
            {
                EnsureAlive(opaque);
                target = opaque.Target;
                return true;
            }

            JSValue flag = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, "__zts_opaque");
            bool isOpaque = JsValueUtil.GetNormTag(flag) == JsValueUtil.TagBool && flag.UInt64 != 0;
            JsValueUtil.Free(ctx, flag);
            if (!isOpaque)
            {
                return false;
            }

            target = obj;
            return true;
        }

        public static void SetTarget(OpaqueValue opaque, object value)
        {
            EnsureAlive(opaque);
            opaque.Target = value;
        }

        private static void EnsureAlive(OpaqueValue opaque)
        {
            if (opaque == null)
            {
                throw new TsScriptException("zts: invalid opaque parameter handle.");
            }

            if (opaque.Generation != _generation)
            {
                throw new TsScriptException("zts: invalid opaque parameter handle (expired).");
            }
        }
    }
}
