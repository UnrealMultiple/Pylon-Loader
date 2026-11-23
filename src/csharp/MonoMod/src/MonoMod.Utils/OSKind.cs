namespace MonoMod.Utils
{
    /// <summary>
    /// An operating system kind.
    /// </summary>
    public enum OSKind
    {
        /// <summary>
        /// An unknown operating system.
        /// </summary>
        Unknown = 0,

        // low 5 bits are flags for the base OS
        // bit 0 is Posix, 1 is Windows, 2 is OSX, 3 is Linux, 4 is BSD
        // remaining bits are a subtype

        /// <summary>
        /// A POSIX-compatible operating system.
        /// </summary>
        Posix = 1 << 0,

        /// <summary>
        /// A Linux kernel. 
        /// </summary>
        Linux = 1 << 3 | Posix,
        /// <summary>
        /// An Android operating system.
        /// </summary>
        /// <remarks>
        /// <see cref="OSKindExtensions.GetKernel(OSKind)"/> will return <see cref="Linux"/>, and
        /// <see cref="OSKindExtensions.GetSubtypeId(OSKind)"/> will return <c>1</c>.
        /// </remarks>
        Android = 0x01 << 5 | Linux, // Android is a subset of Linux
        /// <summary>
        /// A MacOSX kernel.
        /// </summary>
        OSX = 1 << 2 | Posix,
        /// <summary>
        /// An iOS operating system.
        /// </summary>
        /// <remarks>
        /// <see cref="OSKindExtensions.GetKernel(OSKind)"/> will return <see cref="IOS"/>, and
        /// <see cref="OSKindExtensions.GetSubtypeId(OSKind)"/> will return <c>1</c>.
        /// </remarks>
        IOS = 0x01 << 5 | OSX, // iOS is a subset of OSX
        /// <summary>
        /// A BSD kernel.
        /// </summary>
        BSD = 1 << 4 | Posix,

        /// <summary>
        /// A Windows kernel.
        /// </summary>
        Windows = 1 << 1,
        /// <summary>
        /// A Windows operating system, running on the Wine emulation layer.
        /// </summary>
        /// <remarks>
        /// <see cref="OSKindExtensions.GetKernel(OSKind)"/> will return <see cref="Windows"/>, and
        /// <see cref="OSKindExtensions.GetSubtypeId(OSKind)"/> will return <c>1</c>.
        /// </remarks>
        Wine = 0x01 << 5 | Windows,
    }

    /// <summary>
    /// A collection of extensions for the <see cref="OSKind"/> enum.
    /// </summary>
    public static class OSKindExtensions
    {
        /// <summary>
        /// Tests whether <paramref name="operatingSystem"/> is an operating system with the specified <paramref name="test"/> flag.
        /// </summary>
        /// <example>
        /// <list type="bullet">
        /// <item><see cref="OSKind.Wine"/> is <see cref="OSKind.Windows"/>.</item>
        /// <item><see cref="OSKind.Linux"/> is <see cref="OSKind.Posix"/>.</item>
        /// <item><see cref="OSKind.IOS"/> is <see cref="OSKind.OSX"/> is <see cref="OSKind.Posix"/>.</item>
        /// </list>
        /// </example>
        /// <param name="operatingSystem">The <see cref="OSKind"/> to test.</param>
        /// <param name="test">The test value.</param>
        /// <returns><see langword="true"/> if <paramref name="operatingSystem"/> matches <paramref name="test"/>; <see langword="false"/> otherwise.</returns>
        public static bool Is(this OSKind operatingSystem, OSKind test) => operatingSystem.Has(test);
        /// <summary>
        /// Gets the kernel <see cref="OSKind"/> for <paramref name="operatingSystem"/>.
        /// </summary>
        /// <remarks>
        /// <para>This method returns <paramref name="operatingSystem"/> except when noted in the remarks of the <see cref="OSKind"/> members.</para>
        /// <para>This is usually what you want to use, if you don't care about the differences between the more precise subtypes.</para>
        /// </remarks>
        /// <param name="operatingSystem">The <see cref="OSKind"/> to get the kernel of.</param>
        /// <returns>The <see cref="OSKind"/> representing <paramref name="operatingSystem"/>'s kernel.</returns>
        public static OSKind GetKernel(this OSKind operatingSystem) => (OSKind)((int)operatingSystem & 0b11111);
        /// <summary>
        /// Gets the subtype ID for <paramref name="operatingSystem"/>.
        /// </summary>
        /// <remarks>
        /// The subtype ID means basically nothing on its own. Its value is <c>0</c> unless otherwise noted in the remarks of
        /// <see cref="OSKind"/>'s members.
        /// </remarks>
        /// <param name="operatingSystem">The <see cref="OSKind"/> to get the subtype ID of.</param>
        /// <returns>The subtype ID of <paramref name="operatingSystem"/>.</returns>
        public static int GetSubtypeId(this OSKind operatingSystem) => (int)operatingSystem >> 5;
    }
}
