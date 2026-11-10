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
using ZenTS.Jvm;
using ZenTS.Mt;

namespace ZenTS.Marshaling
{
    /// <summary>
    /// OpaqueValue handles for ref/in/out default marshal.
    /// Frame generation: Enter/Leave invalidate handles after the C#↔JS call returns.
    /// </summary>
    internal static class OpaqueParameterScope
    {
        private static int _generation = 1;
        private static readonly System.Collections.Generic.Stack<int> Scopes = new System.Collections.Generic.Stack<int>();
        private static readonly System.Collections.Generic.HashSet<int> Active = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<OpaqueValue>> Handles = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<OpaqueValue>>();
        public static void Enter()
        {
            unchecked
            {
                if (++_generation <= 0)
                    _generation = 1;
            }
            Scopes.Push(_generation);
            Active.Add(_generation);
            Handles[_generation] = new System.Collections.Generic.List<OpaqueValue>();
        }
        public static void Leave(IntPtr ctx)
        {
            if (Scopes.Count == 0)
                return;
            int id = Scopes.Pop();
            Active.Remove(id);
            foreach (var handle in Handles[id])
                handle.Target = null;
            Handles.Remove(id);
        }
        public static void Reset()
        {
            foreach (var handles in Handles.Values)
                foreach (var handle in handles)
                    handle.Target = null;
            Handles.Clear();
            Active.Clear();
            Scopes.Clear();
        }

        public static JsValue Push(IntPtr ctx, object target, Type declaredType = null, JsMarshalAsAttribute marshal = null)
        {
            if (Scopes.Count == 0)
                throw new JsScriptException("zents: OpaqueValue requires an active call scope");
            var opaque = new OpaqueValue(target) { Generation = Scopes.Peek(), DeclaredType = declaredType ?? target?.GetType(), Marshal = marshal?.JsMarshalType == JsMarshalType.OpaqueValue ? null : marshal };
            Handles[Scopes.Peek()].Add(opaque);
            JsValue handle = EngineApi.NewObject(ctx);
            int slot = ObjectRegistry.Register(opaque);
            EngineApi.SetPropertyStr(ctx, handle, "__zents_id", JsValueUtil.NewInt32(slot));
            EngineApi.SetPropertyStr(ctx, handle, "__zents_opaque", JsValueUtil.NewBool(true));
            opaque.JsValue = handle;
            return handle;
        }

        public static JsValue PushExisting(IntPtr ctx, OpaqueValue opaque)
        {
            EnsureAlive(opaque);
            if (JsValueUtil.GetNormTag(opaque.JsValue) == JsValueUtil.TagObject)
            {
                return JsValueUtil.Dup(opaque.JsValue);
            }

            return Push(ctx, opaque.Target, opaque.DeclaredType, opaque.Marshal);
        }

        public static bool TryPop(IntPtr ctx, JsValue jsValue, out object target)
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

            JsValue flag = EngineApi.GetPropertyStr(ctx, jsValue, "__zents_opaque");
            bool isOpaque = JsValueUtil.GetNormTag(flag) == JsValueUtil.TagBool && JsValueUtil.GetUInt64(flag) != 0;
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
                throw new JsScriptException("zents: invalid opaque parameter handle.");
            }

            if (!Active.Contains(opaque.Generation))
            {
                throw new JsScriptException("zents: invalid opaque parameter handle (expired).");
            }
        }
    }
}
