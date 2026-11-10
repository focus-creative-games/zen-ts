// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
namespace ZenTS
{
    public static class HostJsPlatform
    {
        public static bool IsWeChat(BuildTarget target) => target.ToString()=="WeixinMiniGame";
        public static bool IsTarget(BuildTarget target) => target==BuildTarget.WebGL||IsWeChat(target);
        internal static NamedBuildTarget NamedTarget(BuildTarget target) => NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
        internal static object ExceptionSupport(BuildTarget target)
        {
            if(target==BuildTarget.WebGL)return PlayerSettings.WebGL.exceptionSupport;
            var settings=typeof(PlayerSettings).GetNestedType("WeixinMiniGame",BindingFlags.Public);
            var property=settings?.GetProperty("exceptionSupport",BindingFlags.Public|BindingFlags.Static);
            if(property==null)throw new BuildFailedException("[ZenTS] Cannot read WeixinMiniGame exception support in this Editor");
            return property.GetValue(null);
        }
        internal static void EnableFullExceptions(BuildTarget target)
        {
            if(target==BuildTarget.WebGL){PlayerSettings.WebGL.exceptionSupport=WebGLExceptionSupport.FullWithStacktrace;return;}
            var settings=typeof(PlayerSettings).GetNestedType("WeixinMiniGame",BindingFlags.Public);
            var property=settings?.GetProperty("exceptionSupport",BindingFlags.Public|BindingFlags.Static);
            if(property==null)throw new BuildFailedException("[ZenTS] Cannot configure WeixinMiniGame exception support");
            property.SetValue(null,Enum.Parse(property.PropertyType,"FullWithStacktrace"));
        }
    }
}
