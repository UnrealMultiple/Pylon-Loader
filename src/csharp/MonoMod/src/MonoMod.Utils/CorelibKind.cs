namespace MonoMod.Utils
{
    /// <summary>
    /// The kind of corelib loaded by the current runtime.
    /// </summary>
    public enum CorelibKind
    {
        /// <summary>
        /// The .NET Framework corelib. The corelib's name is <c>mscorlib</c>, and it is used on standard Mono and .NET Framework.
        /// </summary>
        Framework,
        /// <summary>
        /// The .NET Core corelib. The corelib's name is <c>System.Private.CoreLib</c>, and it is used on .NET Mono (from 
        /// <see href="https://github.com/dotnet/runtime" />) and CoreCLR (.NET Core and .NET 5+) runtimes.
        /// </summary>
        Core,
    }
}
