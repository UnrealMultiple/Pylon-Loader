using System.Runtime.CompilerServices;

// 本程序集只做"把托管值原样递给内核"，不做运行期编组：
// 关掉之后 Vector2 / 结构体 / 指针全是 blittable 直传，这正是 LibraryImport 源生成器
// 接受这些类型的前提（否则 Vector2 之类会被判成不支持）。字符串仍按 Utf8 由源生成器处理。
[assembly: DisableRuntimeMarshalling]
