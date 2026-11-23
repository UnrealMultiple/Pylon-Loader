namespace MonoMod.Utils
{
    /// <summary>
    /// A runtime implementation kind.
    /// </summary>
    public enum RuntimeKind
    {
        /// <summary>
        /// Some unknown runtime implementation.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// The Windows .NET Framework CLR implementation.
        /// </summary>
        Framework,
        /// <summary>
        /// The CoreCLR implementation, used by .NET Core and .NET 5+. derived from the Silverlight runtime.
        /// </summary>
        CoreCLR,
        /// <summary>
        /// The Mono CLR implementation.
        /// </summary>
        Mono,
    }
}
