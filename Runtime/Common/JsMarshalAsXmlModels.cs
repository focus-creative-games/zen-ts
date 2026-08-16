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

namespace ZTS
{
    public enum JsMarshalAsXmlTargetKind : byte
    {
        Type = 0,
        Field = 1,
        Property = 2,
        Param = 3,
        Return = 4,
    }

    /// <summary>
    /// One MarshalAs rule from external XML (spec marshal/02-MARSHAL-AS §9).
    /// </summary>
    public sealed class JsMarshalAsXmlRule
    {
        public string SourcePath { get; set; }

        public string AssemblyName { get; set; }

        public string TypeFullName { get; set; }

        public JsMarshalAsXmlTargetKind Kind { get; set; }

        public string MemberName { get; set; }

        public string MethodName { get; set; }

        public string Signature { get; set; }

        public int ParamIndex { get; set; } = -1;

        public JsMarshalType MarshalType { get; set; }

        public string[] Members { get; set; }

        public string DuplicateKey
        {
            get
            {
                switch (Kind)
                {
                    case JsMarshalAsXmlTargetKind.Type:
                        return AssemblyName + "|" + TypeFullName + "|Type";
                    case JsMarshalAsXmlTargetKind.Field:
                        return AssemblyName + "|" + TypeFullName + "|Field|" + MemberName;
                    case JsMarshalAsXmlTargetKind.Property:
                        return AssemblyName + "|" + TypeFullName + "|Property|" + MemberName;
                    case JsMarshalAsXmlTargetKind.Param:
                        return AssemblyName + "|" + TypeFullName + "|" + MethodName + "|" + Signature + "|Param|" + ParamIndex;
                    case JsMarshalAsXmlTargetKind.Return:
                        return AssemblyName + "|" + TypeFullName + "|" + MethodName + "|" + Signature + "|Return";
                    default:
                        throw new InvalidOperationException("Unknown MarshalAs XML target kind: " + Kind);
                }
            }
        }

        public JsMarshalAsAttribute ToAttribute()
        {
            return new JsMarshalAsAttribute(MarshalType)
            {
                Members = Members,
            };
        }
    }
}
