extern alias ilhelpers;

#pragma warning disable IDE0005 // unused using

global using ilhelpers::MonoMod;

#if !NET6_0_OR_GREATER && (NETSTANDARD2_1_OR_GREATER || NETCOREAPP || NET)
// Any time we want to use Unsafe, we want ours, not the BCL's. Note that we need these funky defs because the location of Unsafe moves between versions.
// I would actually rather move the BCL assembly defining it into an alias, but that doesn't seem to be particularly viable
global using Unsafe = ilhelpers::System.Runtime.CompilerServices.Unsafe;
#else
global using Unsafe = System.Runtime.CompilerServices.Unsafe;
#endif
