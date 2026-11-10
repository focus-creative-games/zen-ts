// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.Collections.Generic;
using System.Reflection;
namespace ZenTS
{
    internal static class AotXmlPolicy
    {
        internal static HashSet<Assembly> Capture()
        {
            var assemblies = new HashSet<Assembly>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;
                try
                {
                    if (!string.IsNullOrEmpty(assembly.Location))
                        assemblies.Add(assembly);
                }
                catch (NotSupportedException) { }
            }
            return assemblies;
        }
    }
}
