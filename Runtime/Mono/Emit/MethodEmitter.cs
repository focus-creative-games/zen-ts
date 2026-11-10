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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using ZenTS.Jvm;
using ZenTS.Marshaling;
using ZenTS.Mt;
using ZenTS.Utils;

namespace ZenTS.Emit
{
    internal static class MethodOverloadDispatcher
    {
        internal static bool IsExtensionMethod(MethodInfo method) => method != null && method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
        internal static ParameterInfo[] GetJsFacingParameters(MethodInfo method)
        {
            var parameters=method.GetParameters();
            return IsExtensionMethod(method)?parameters.Skip(1).ToArray():parameters;
        }
    }

    /// <summary>Expression-compiled method bridge (no Method.Invoke on hot path).</summary>
    internal static class MethodEmitter
    {
        private static readonly ConcurrentDictionary<MethodInfo, Func<object, object[], object>> Compiled =
            new ConcurrentDictionary<MethodInfo, Func<object, object[], object>>();

        internal static void ResetCaches()
        {
            JsCallPlan.Reset();
            Compiled.Clear();
        }

        public static JsValue EmitMethod(JsEnv env, MethodInfo method) =>
            EmitMethod(env, method, ownerType: method.DeclaringType, isByVal: false);

        public static JsValue EmitMethod(JsEnv env, MethodInfo method, Type ownerType, bool isByVal)
        {
            IntPtr ctx = env.Context;
            JsValue fn;
            if (method.IsGenericMethodDefinition)
            {
                var openCb = new JsOpenGenericMethodCallback(method, ownerType ?? method.DeclaringType, isByVal);
                fn = JsCallbackGate.NewCFunction(ctx, openCb.Invoke, method.Name, method.GetParameters().Length);
            }
            else
            {
                JsCallPlan.For(method);
                EnsureCompiled(method);
                var callback = new JsMethodCallback(method, ownerType, isByVal);
                fn = JsCallbackGate.NewCFunction(ctx, callback.Invoke, method.Name, method.GetParameters().Length);
            }

            AttachTag(ctx, fn, new MethodClosureTag
            {
                Method = method,
                OwnerType = ownerType ?? method.DeclaringType,
                IsStatic = method.IsStatic,
                IsByVal = isByVal,
                IsDirect = true,
            });
            return fn;
        }

        public static JsValue EmitOverloadGroup(JsEnv env, string name, List<MethodInfo> methods) =>
            EmitOverloadGroup(env, name, methods, ownerType: methods.Count > 0 ? methods[0].DeclaringType : null, isByVal: false);

        public static JsValue EmitOverloadGroup(JsEnv env, string name, List<MethodInfo> methods, Type ownerType, bool isByVal)
        {
            foreach (MethodInfo m in methods)
            {
                if (!m.IsGenericMethodDefinition)
                {
                    JsCallPlan.For(m);
                    EnsureCompiled(m);
                }
            }

            var callback = new JsOverloadCallback(methods, ownerType, isByVal);
            JsValue fn = JsCallbackGate.NewCFunction(env.Context, callback.Invoke, name, 0);
            AttachTag(env.Context, fn, new MethodClosureTag
            {
                Method = null,
                OwnerType = ownerType ?? (methods.Count > 0 ? methods[0].DeclaringType : null),
                IsStatic = methods.Count > 0 && methods.All(m => m.IsStatic),
                IsByVal = isByVal,
                IsDirect = false,
            });
            return fn;
        }

        public static bool TryGetDirectTag(IntPtr ctx, JsValue fn, out MethodClosureTag tag)
        {
            tag = null;
            if (EngineApi.IsFunction(ctx, fn) == 0)
            {
                return false;
            }

            JsValue idVal = EngineApi.GetPropertyStr(ctx, fn, "__zents_method_id");
            try
            {
                if (JsValueUtil.GetNormTag(idVal) != JsValueUtil.TagInt)
                {
                    return false;
                }

                int id = unchecked((int)JsValueUtil.GetUInt64(idVal));
                if (!MethodTagRegistry.TryGet(id, out tag) || tag == null || !tag.IsDirect || tag.Method == null)
                {
                    tag = null;
                    return false;
                }

                return true;
            }
            finally
            {
                JsValueUtil.Free(ctx, idVal);
            }
        }

        private static void AttachTag(IntPtr ctx, JsValue fn, MethodClosureTag tag)
        {
            int id = MethodTagRegistry.Register(tag);
            EngineApi.SetPropertyStr(ctx, fn, "__zents_method_id", JsValueUtil.NewInt32(id));
            EngineApi.SetPropertyStr(ctx, fn, "__zents_direct", JsValueUtil.NewBool(tag.IsDirect));
        }

        internal static Func<object, object[], object> EnsureCompiled(MethodInfo method)
        {
            if (Compiled.TryGetValue(method, out Func<object, object[], object> existing))
            {
                return existing;
            }

            Func<object, object[], object> fn;
            try
            {
                fn = Compile(method);
            }
            catch
            {
                MethodInfo m = method;
                fn = (target, args) => m.Invoke(target, args);
            }

            return Compiled.GetOrAdd(method, fn);
        }

        private static Func<object, object[], object> Compile(MethodInfo method)
        {
            ParameterExpression target = Expression.Parameter(typeof(object), "target");
            ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
            ParameterInfo[] ps = method.GetParameters();
            var callArgs = new Expression[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                Expression raw = Expression.ArrayIndex(args, Expression.Constant(i));
                if (pt.IsByRef)
                {
                    Type elem = pt.GetElementType();
                    callArgs[i] = Expression.Convert(raw, elem);
                }
                else
                {
                    callArgs[i] = Expression.Convert(ConvertArg(raw, pt), pt);
                }
            }

            Expression instance = method.IsStatic
                ? null
                : Expression.Convert(target, method.DeclaringType);

            Expression call = method.IsStatic
                ? Expression.Call(method, callArgs)
                : Expression.Call(instance, method, callArgs);

            Expression body;
            if (method.ReturnType == typeof(void))
            {
                body = Expression.Block(call, Expression.Constant(null, typeof(object)));
            }
            else
            {
                body = Expression.Convert(call, typeof(object));
            }

            return Expression.Lambda<Func<object, object[], object>>(body, target, args).Compile();
        }

        private static Expression ConvertArg(Expression raw, Type pt)
        {
            return Expression.Convert(raw, pt);
        }

        private static bool HasByRefParameters(MethodInfo method)
        {
            foreach (ParameterInfo p in method.GetParameters())
            {
                if (p.ParameterType.IsByRef)
                {
                    return true;
                }
            }

            return false;
        }

        private static object InvokeMethod(MethodInfo method, object target, object[] args)
        {
            if (HasPointerSignature(method))
            {
                return PointerAwareInvoker.Invoke(method, target, args);
            }

            if (HasByRefParameters(method))
            {
                return method.Invoke(target, args);
            }

            return EnsureCompiled(method)(target, args);
        }

        private static bool HasPointerSignature(MethodInfo method)
        {
            if (method.ReturnType.IsPointer)
            {
                return true;
            }

            foreach (ParameterInfo p in method.GetParameters())
            {
                Type pt = p.ParameterType;
                if (pt.IsByRef)
                {
                    pt = pt.GetElementType();
                }

                if (pt != null && pt.IsPointer)
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteBackByRefParameters(
            IntPtr ctx, MethodInfo method, int argc, IntPtr argvPtr, object[] args, bool isExtension)
        {
            ParameterInfo[] ps = method.GetParameters();
            int jsIndex = 0;
            int start = isExtension ? 1 : 0;
            for (int i = start; i < ps.Length; i++)
            {
                if (JsCallPlan.IsOut(ps[i])) continue;
                int slots = TableMarshal.GetJsArgSlotCount(ps[i]);
                if (ps[i].ParameterType.IsByRef)
                {
                    if (jsIndex >= 0 && jsIndex < argc)
                    {
                        JsValue jsArg = ArgReader.Read(argvPtr, jsIndex);
                        object updated = args[i];
                        if (ObjectRegistry.TryGetObject(ctx, jsArg, out object holder) && holder is OpaqueValue opaque)
                        {
                            OpaqueParameterScope.SetTarget(opaque, updated);
                        }
                        else
                        {
                            Type elemType = ps[i].ParameterType.GetElementType();
                            if (elemType != null && elemType.IsValueType &&
                                ObjectRegistry.TryGetObject(ctx, jsArg, out _))
                            {
                                ObjectRegistry.TryReplaceObject(ctx, jsArg, updated);
                            }
                        }
                    }
                }

                if (ps[i].IsDefined(typeof(ParamArrayAttribute), false))
                {
                    break;
                }

                jsIndex += slots;
            }
        }

        private static bool TryCloseGtap(IntPtr ctx, MethodInfo open, int argc, IntPtr argv, out MethodInfo closed, out int prefix)
        {
            closed=null;prefix=open.GetGenericArguments().Length;if(argc<prefix)return false;
            Type[] types=new Type[prefix];
            for(int i=0;i<prefix;++i)
            {
                JsValue value=ArgReader.Read(argv,i);
                if(EngineApi.IsFunction(ctx,value)!=0 || !ObjectRegistry.TryGetObject(ctx,value,out object held) || !(held is Type type))return false;
                JsValue kind=EngineApi.GetPropertyStr(ctx,value,"__zents_ud_kind");
                try { if(JsValueUtil.GetNormTag(kind)!=JsValueUtil.TagString || JsCallPlan.ReadText(ctx,kind)!="byobj")return false; }
                finally {JsValueUtil.Free(ctx,kind);}
                types[i]=type;
            }
            try { closed=open.MakeGenericMethod(types); }
            catch(ArgumentException) { return false; }
            return JsCallPlan.For(closed).Match(ctx,argc-prefix,IntPtr.Add(argv,prefix*16),out _,out _,out _);
        }
        private sealed class JsOpenGenericMethodCallback
        {
            private readonly MethodInfo _method;
            private readonly Type _owner;
            private readonly bool _byVal;
            public JsOpenGenericMethodCallback(MethodInfo method,Type owner,bool byVal){_method=method;_owner=owner;_byVal=byVal;}
            public JsValue Invoke(IntPtr ctx, JsValue thisVal,int argc,IntPtr argv)
            {
                try
                {
                    if(!TryCloseGtap(ctx,_method,argc,argv,out MethodInfo closed,out int prefix))throw new JsScriptException("zents: open generic method requires System.Type GTAP prefix or zents.inflate");
                    JsValue fn=ZentsLib.GetClosedGenericFunction(ctx,closed,_owner,_byVal);
                    try{return EngineApi.Call(ctx,fn,thisVal,argc-prefix,IntPtr.Add(argv,prefix*16));}finally{JsValueUtil.Free(ctx,fn);}
                }
                catch(Exception ex){JsCallbackBoundary.ThrowManaged(ctx,ex);return JsValueUtil.MakeErrorSentinel();}
            }
        }

        private sealed class JsMethodCallback
        {
            private readonly MethodInfo _method;
            private readonly Func<object, object[], object> _compiled;
            private readonly Type _owner;
            private readonly bool _byVal;

            public JsMethodCallback(MethodInfo method, Type owner=null, bool byVal=false)
            {
                _method = method;
                _owner=owner??method.DeclaringType;_byVal=byVal;
                _compiled = HasPointerSignature(method) ? null : EnsureCompiled(method);
            }

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argvPtr)
            {
                try
                {
                    bool extension=MethodOverloadDispatcher.IsExtensionMethod(_method);object receiver=null;
                    if((!_method.IsStatic||extension) && !ObjectRegistry.TryGetObject(ctx,thisVal,out receiver))throw new JsScriptException("zents: invalid method target");
                    if((!_method.IsStatic||extension)&&_owner.IsValueType&&!_owner.IsEnum)
                    {
                        JsValue kind=EngineApi.GetPropertyStr(ctx,thisVal,"__zents_ud_kind");
                        try{if((JsCallPlan.ReadText(ctx,kind)=="byval")!=_byVal)throw new JsScriptException("zents: incompatible ByVal/ByObj receiver");}
                        finally{JsValueUtil.Free(ctx,kind);}
                    }
                    var plan=JsCallPlan.For(_method);object[] args=plan.Pop(ctx,receiver,argc,argvPtr);
                    object result=InvokeMethod(_method,extension?null:receiver,args);
                    WriteBackByRefParameters(ctx,_method,argc,argvPtr,args,extension);
                    return plan.PushResult(ctx,result,args);
                }
                catch(Exception ex){JsCallbackBoundary.ThrowManaged(ctx,ex);return JsValueUtil.MakeErrorSentinel();}
            }
        }

        private sealed class JsOverloadCallback
        {
            private readonly List<MethodInfo> _methods;

            private readonly Type _owner;private readonly bool _byVal;
            public JsOverloadCallback(List<MethodInfo> methods,Type owner,bool byVal){_methods=methods;_owner=owner;_byVal=byVal;}

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argvPtr)
            {
                try
                {
                    // Open generic candidates get the explicit System.Type-prefix phase first.
                    var gtap=new List<(MethodInfo method,int prefix)>();
                    foreach(var open in _methods) if(open.IsGenericMethodDefinition && TryCloseGtap(ctx,open,argc,argvPtr,out MethodInfo closed,out int count))gtap.Add((closed,count));
                    if(gtap.Count>0)
                    {
                        int prefix=gtap[0].prefix;
                        if(gtap.Any(x=>x.prefix!=prefix))throw new JsScriptException("zents: ambiguous GTAP overload");
                        MethodInfo best=(MethodInfo)JsCallPlan.Select(ctx,gtap.Select(x=>(MethodBase)x.method).ToArray(),argc-prefix,IntPtr.Add(argvPtr,prefix*16));
                        if(best==null)throw new JsScriptException("zents: no applicable GTAP overload");
                        return new JsMethodCallback(best,_owner,_byVal).Invoke(ctx,thisVal,argc-prefix,IntPtr.Add(argvPtr,prefix*16));
                    }
                    MethodInfo method=(MethodInfo)JsCallPlan.Select(ctx,_methods.Cast<MethodBase>().ToArray(),argc,argvPtr);
                    if(method==null)throw new JsScriptException("zents: no matching overload");
                    return new JsMethodCallback(method,_owner,_byVal).Invoke(ctx,thisVal,argc,argvPtr);
                }
                catch(Exception ex){JsCallbackBoundary.ThrowManaged(ctx,ex);return JsValueUtil.MakeErrorSentinel();}
            }
        }

    }

    internal static class FieldEmitter
    {
        public static JsValue EmitGetter(JsEnv env, FieldInfo field)
        {
            var cb = new FieldGetter(field);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "get_" + field.Name, 0);
        }

        public static JsValue EmitSetter(JsEnv env, FieldInfo field)
        {
            var cb = new FieldSetter(field);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "set_" + field.Name, 1);
        }

        private sealed class FieldGetter
        {
            private readonly FieldInfo _field;
            private readonly JsMarshalAsAttribute _marshal;
            public FieldGetter(FieldInfo field) { _field = field; _marshal=JsMarshalAsXmlRegistry.ResolveFieldMarshal(field); }

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    object target = null;
                    if (!_field.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: invalid field target.");
                        }
                    }

                    return TypedMarshal.Push(ctx, _field.GetValue(target), _field.FieldType,
                        _marshal);
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }

        private sealed class FieldSetter
        {
            private readonly FieldInfo _field;
            private readonly JsMarshalAsAttribute _marshal;
            public FieldSetter(FieldInfo field) { _field = field; _marshal=JsMarshalAsXmlRegistry.ResolveFieldMarshal(field); }

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    if(_field.IsInitOnly||_field.IsLiteral)throw new JsScriptException("zents: field is read-only");
                    object target = null;
                    if (!_field.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: invalid field target.");
                        }
                    }

                    JsValue jsArg = ArgReader.Read(argv, 0);
                    object value = TypedMarshal.Pop(ctx, jsArg, _field.FieldType,
                        _marshal);
                    _field.SetValue(target, value);
                    return JsValueUtil.Undefined;
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }
    }

    internal static class PropertyEmitter
    {
        public static JsValue EmitGetter(JsEnv env, PropertyInfo property)
        {
            var cb = new PropGetter(property);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "get_" + property.Name, 0);
        }

        public static JsValue EmitSetter(JsEnv env, PropertyInfo property)
        {
            var cb = new PropSetter(property);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "set_" + property.Name, 1);
        }

        private sealed class PropGetter
        {
            private readonly PropertyInfo _property;
            private readonly JsMarshalAsAttribute _marshal;
            private readonly Func<object, object> _readRef;
            public PropGetter(PropertyInfo property)
            {
                _property = property;
                _marshal = JsMarshalAsXmlRegistry.ResolvePropertyMarshal(property);
                if (property.PropertyType.IsByRef)
                {
                    // Reflection cannot invoke a ref return. Copy its current value
                    // while the managed reference is valid, never expose its address.
                    MethodInfo getter = property.GetMethod;
                    Type element = property.PropertyType.GetElementType();
                    var method = new DynamicMethod("ReadRef_" + property.Name,
                        typeof(object), new[] { typeof(object) }, typeof(PropertyEmitter).Module, true);
                    ILGenerator il = method.GetILGenerator();
                    if (!getter.IsStatic)
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(getter.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, getter.DeclaringType);
                    }
                    il.Emit(getter.IsStatic || getter.DeclaringType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, getter);
                    il.Emit(OpCodes.Ldobj, element);
                    if (element.IsValueType) il.Emit(OpCodes.Box, element);
                    il.Emit(OpCodes.Ret);
                    _readRef = (Func<object, object>)method.CreateDelegate(typeof(Func<object, object>));
                }
            }

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    if (!_property.CanRead)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(ctx,
                            $"zents: property has no getter: {_property.DeclaringType?.FullName}.{_property.Name}");
                    }

                    object target = null;
                    MethodInfo get = _property.GetMethod;
                    if (get != null && !get.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: invalid property target.");
                        }
                    }

                    return TypedMarshal.Push(ctx, _readRef != null ? _readRef(target) : _property.GetValue(target),
                        _property.PropertyType.IsByRef ? _property.PropertyType.GetElementType() : _property.PropertyType,
                        _marshal);
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }

        private sealed class PropSetter
        {
            private readonly PropertyInfo _property;
            private readonly JsMarshalAsAttribute _marshal;
            public PropSetter(PropertyInfo property) { _property = property; _marshal=JsMarshalAsXmlRegistry.ResolvePropertyMarshal(property); }

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    if (!_property.CanWrite)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(ctx,
                            $"zents: property has no setter: {_property.DeclaringType?.FullName}.{_property.Name}");
                    }

                    object target = null;
                    MethodInfo set = _property.SetMethod;
                    if (set != null && !set.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: invalid property target.");
                        }
                    }

                    object value = TypedMarshal.Pop(ctx, ArgReader.Read(argv, 0), _property.PropertyType,
                        _marshal);
                    _property.SetValue(target, value);
                    return JsValueUtil.Undefined;
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }
    }

    internal static class ConstructorEmitter
    {
        private static readonly Dictionary<ConstructorInfo, Func<object[], object>> CtorCache =
            new Dictionary<ConstructorInfo, Func<object[], object>>();

        public static JsValue Emit(JsEnv env, Type type, JsValue instanceProto)
        {
            var cb = new ConstructorCallback(type, instanceProto);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, type.Name, 0);
        }

        internal static Func<object[], object> CompileCtorPublic(ConstructorInfo ctor)
        {
            lock (CtorCache)
            {
                if (CtorCache.TryGetValue(ctor, out Func<object[], object> existing))
                {
                    return existing;
                }
            }

            ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
            ParameterInfo[] ps = ctor.GetParameters();
            var callArgs = new Expression[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                callArgs[i] = Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), ps[i].ParameterType);
            }

            NewExpression neo = Expression.New(ctor, callArgs);
            Func<object[], object> fn = Expression.Lambda<Func<object[], object>>(
                Expression.Convert(neo, typeof(object)), args).Compile();
            lock (CtorCache)
            {
                CtorCache[ctor] = fn;
            }

            return fn;
        }

        private sealed class ConstructorCallback
        {
            private readonly Type _type;
            private readonly ConstructorInfo[] _constructors;
            public JsValue Proto;

            public ConstructorCallback(Type type, JsValue instanceProto)
            {
                _type = type;
                _constructors=type.GetConstructors(BindingFlags.Public|BindingFlags.Instance);
                foreach(var ctor in _constructors)JsCallPlan.For(ctor);
                Proto = instanceProto;
            }

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    ConstructorInfo[] ctors=_constructors;
                    if(ctors.Length==0 && _type.IsValueType && argc==0)return TypedMarshal.Push(ctx,Activator.CreateInstance(_type),_type);
                    ConstructorInfo ctor=(ConstructorInfo)JsCallPlan.Select(ctx,ctors,argc,argv);
                    if(ctor==null)throw new JsScriptException("zents: no matching constructor for "+_type.FullName);
                    object[] args=JsCallPlan.For(ctor).Pop(ctx,null,argc,argv);
                    object instance=ctor.GetParameters().Any(p=>p.ParameterType.IsByRef)?ctor.Invoke(args):CompileCtorPublic(ctor)(args);
                    return TypedMarshal.Push(ctx,instance,_type);
                }
                catch(Exception ex){JsCallbackBoundary.ThrowManaged(ctx,ex);return JsValueUtil.MakeErrorSentinel();}
            }
        }
    }

    internal static class ExtensionMethodUtil
    {
        public static IEnumerable<MethodInfo> GetExtensionMethods(Type extendedType)
        {
            var seenMethods = new HashSet<MethodInfo>();
            var scannedExtTypes = new HashSet<Type>();

            for (Type t = extendedType; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (JsExtensionAttribute attr in t.GetCustomAttributes<JsExtensionAttribute>(inherit: false))
                {
                    if (attr.ExtensionTypes == null)
                    {
                        continue;
                    }

                    foreach (Type extType in attr.ExtensionTypes)
                    {
                        foreach (MethodInfo method in EnumerateExtensionTypeMethods(
                                     extType, extendedType, seenMethods, scannedExtTypes))
                        {
                            yield return method;
                        }
                    }
                }

                if (JsExtensionXmlRegistry.TryGetExtensionTypes(t, out Type[] xmlTypes))
                {
                    foreach (Type extType in xmlTypes)
                    {
                        foreach (MethodInfo method in EnumerateExtensionTypeMethods(
                                     extType, extendedType, seenMethods, scannedExtTypes))
                        {
                            yield return method;
                        }
                    }
                }
            }
        }

        private static IEnumerable<MethodInfo> EnumerateExtensionTypeMethods(
            Type extType,
            Type extendedType,
            HashSet<MethodInfo> seenMethods,
            HashSet<Type> scannedExtTypes)
        {
            if (extType == null || !scannedExtTypes.Add(extType))
            {
                yield break;
            }

            foreach (MethodInfo method in extType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                {
                    continue;
                }

                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length > 0 && ps[0].ParameterType.IsAssignableFrom(extendedType) &&
                    seenMethods.Add(method))
                {
                    yield return method;
                }
            }
        }
    }

    internal static class NullableConstructorEmitter
    {
        public static JsValue Emit(JsEnv env, Type nullableType, Type underlyingType)
        {
            var cb = new NullableConstructorCallback(nullableType, underlyingType);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, nullableType.Name, 0);
        }

        private sealed class NullableConstructorCallback
        {
            private readonly Type _underlyingType;

            public NullableConstructorCallback(Type nullableType, Type underlyingType)
            {
                _underlyingType = underlyingType;
            }

            public JsValue Invoke(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    if (_underlyingType.IsEnum)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(
                            ctx, $"zents: Nullable<{_underlyingType.Name}> construct is not supported for enums.");
                    }

                    if (_underlyingType.IsPrimitive || _underlyingType == typeof(string) ||
                        _underlyingType == typeof(IntPtr) || _underlyingType == typeof(UIntPtr))
                    {
                        if (argc != 1)
                        {
                            return JsCallbackGate.ReturnErrorSentinel(
                                ctx, $"zents: Nullable<{_underlyingType.Name}> requires one argument.");
                        }

                        object value = TypedMarshal.Pop(ctx, ArgReader.Read(argv, 0), _underlyingType);
                        return TypedMarshal.Push(ctx, value, _underlyingType);
                    }

                    ConstructorInfo[] ctors = _underlyingType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    if (ctors.Length == 0)
                    {
                        if (_underlyingType.IsValueType)
                        {
                            object def = Activator.CreateInstance(_underlyingType);
                            return TypedMarshal.Push(ctx, def, _underlyingType);
                        }

                        return JsCallbackGate.ReturnErrorSentinel(
                            ctx, $"zents: no public constructor for Nullable<{_underlyingType.FullName}>.");
                    }

                    ConstructorInfo ctor=(ConstructorInfo)JsCallPlan.Select(ctx,ctors,argc,argv);
                    if(ctor==null)throw new JsScriptException("zents: no matching nullable constructor");
                    object[] args=JsCallPlan.For(ctor).Pop(ctx,null,argc,argv);
                    object instance = ConstructorEmitter.CompileCtorPublic(ctor)(args);
                    return TypedMarshal.Push(ctx, instance, _underlyingType);
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }
    }
}
