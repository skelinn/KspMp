#if !NET5_0_OR_GREATER
// Lets C# 9+ 'init' accessors and records compile for net472 / netstandard2.0 (the runtime never needs the type).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
