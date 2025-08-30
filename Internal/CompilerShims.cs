#if NETCOREAPP3_1
// Shim agar fitur record (C# 9) bisa dipakai di target netcoreapp3.1
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
