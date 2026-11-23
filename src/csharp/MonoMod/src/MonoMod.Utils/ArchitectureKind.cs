using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Utils
{
    /// <summary>
    /// A CPU architecture.
    /// </summary>
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
        Justification = "x86_64 is the name of the architecture, at least for Intel. AMD64 is another reasonable name.")]
    [SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute",
        Justification = "This isn't a set of flags. Some bit values are named to ")]
    public enum ArchitectureKind
    {
        /// <summary>
        /// An unknown architecture.
        /// </summary>
        Unknown,
        /// <summary>
        /// A flag which is set in architectures which are 64-bit.
        /// </summary>
        Bits64 = 1,
        /// <summary>
        /// The Intel x86 CPU architecture.
        /// </summary>
        x86 = 0x01 << 1,
        /// <summary>
        /// The <c>x86_64</c> 64-bit extensions to <see cref="x86"/>. Also known as <see cref="AMD64"/>.
        /// </summary>
        /// <seealso cref="AMD64"/>
        x86_64 = x86 | Bits64,
        /// <summary>
        /// The AMD 64-bit extension to <see cref="x86"/>. Also known as <see cref="x86_64"/>.
        /// </summary>
        /// <seealso cref="x86_64"/>
        AMD64 = x86_64,
        /// <summary>
        /// The ARM instruction set.
        /// </summary>
        Arm = 0x02 << 1,
        /// <summary>
        /// The 64-bit ARM instruction set.
        /// </summary>
        Arm64 = Arm | Bits64,
    }
}
