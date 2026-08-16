using System;

namespace ZTS
{
    /// <summary>
    /// Backend contract for <see cref="TsAppDomain"/>. Implemented by nested
    /// <c>Runtime</c> types in Mono / Il2Cpp assemblies; Common creates them by reflection
    /// (no Common→backend compile-time reference).
    /// </summary>
    public interface ITsRuntime
    {
        void Initialize(Func<string, object> moduleLoader);

        /// <summary>
        /// Tear down the main JS runtime/context and re-initialize with <paramref name="moduleLoader"/>.
        /// </summary>
        void Reset(Func<string, object> moduleLoader);

        void ProcessPendingRefReleases();

        /// <summary>
        /// Bind JS module export to a closed <paramref name="delegateType"/> instance.
        /// </summary>
        Delegate GetFunction(Type delegateType, string jsModule, string jsExportName);
    }
}
