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
using System.Reflection;

namespace ZenTS
{
    public static class JsMarshalAsValidation
    {
        public static string Error(JsMarshalAsAttribute attr, Type type, bool isReturn=false, bool typeLevel=false)
        {
            if(attr==null||attr.JsMarshalType==JsMarshalType.Default)return null;
            if(type==null)return "missing CLR type";
            if(type.IsByRef)type=type.GetElementType();
            if(typeLevel && type.IsGenericType)return "type-level rules cannot target generic types";
            if(type.ContainsGenericParameters)return null; // validate after closing the member
            Type underlying=Nullable.GetUnderlyingType(type);Type t=underlying??type;
            bool primitive=t.IsPrimitive||t==typeof(decimal)||t==typeof(IntPtr)||t==typeof(UIntPtr);
            bool structure=t.IsValueType&&!primitive&&!t.IsEnum;
            switch(attr.JsMarshalType)
            {
                case JsMarshalType.BigInt:return !typeLevel&&(t==typeof(long)||t==typeof(ulong))&&underlying==null?null:"BigInt requires an Int64/UInt64 member slot";
                case JsMarshalType.ActualTypeUserData:return isReturn&&!typeLevel&&!t.IsValueType&&t!=typeof(string)&&!typeof(Delegate).IsAssignableFrom(t)?null:"ActualTypeUserData is return-only for default ByObj reference types";
                case JsMarshalType.Bytes:return t==typeof(byte[])?null:"Bytes requires byte[]";
                case JsMarshalType.Object:return !primitive&&!t.IsEnum&&!t.IsPointer?null:"Object requires a reference type or struct";
                case JsMarshalType.OpaqueValue:return !primitive||t==typeof(decimal)||t==typeof(TypedReference)?null:"OpaqueValue is not valid for a by-value primitive";
                case JsMarshalType.Table:
                case JsMarshalType.UnpackedValues:
                    if(!structure)return "Table/UnpackedValues requires a struct";
                    if(underlying!=null&&attr.JsMarshalType==JsMarshalType.UnpackedValues)return "Nullable cannot use UnpackedValues";
                    if(attr.Members==null||attr.Members.Length==0)return "Members is required";
                    foreach(string spec in attr.Members)
                    {
                        if(string.IsNullOrEmpty(spec))return "empty Members entry";
                        bool optional=spec.EndsWith("?",StringComparison.Ordinal);
                        if(optional&&attr.JsMarshalType!=JsMarshalType.Table)return "optional members require Table";
                        string name=optional?spec.Substring(0,spec.Length-1):spec;
                        const BindingFlags flags=BindingFlags.Public|BindingFlags.Instance;
                        var field=t.GetField(name,flags);var property=t.GetProperty(name,flags);
                        if(field==null&&property==null)return "public member not found: "+name;
                        if(property!=null&&(property.GetIndexParameters().Length!=0||property.GetGetMethod()==null||(!isReturn&&property.GetSetMethod()==null)))return "Table member requires public getter and setter: "+name;
                    }
                    return null;
                default:return "unknown MarshalType";
            }
        }
        internal static JsMarshalAsAttribute Resolve(JsMarshalAsAttribute attr,Type type,string member,bool isReturn=false,bool typeLevel=false,bool xml=false)
        {
            string error=Error(attr,type,isReturn,typeLevel);if(error==null)return attr;
            string message="[ZenTS] Invalid MarshalAs on "+member+" ("+type+"): "+error;
            if(xml)throw new JsMarshalAsConfigurationException(message);
            UnityEngine.Debug.LogWarning(message+"; falling back to Default.");
            return new JsMarshalAsAttribute();
        }
        public static JsMarshalType Normalize(JsMarshalType kind,Type type,string member) => Resolve(new JsMarshalAsAttribute(kind),type,member).JsMarshalType;
    }
}
