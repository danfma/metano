// Polyfill for the C# 9 init-only setter feature on netstandard2.0.
// The compiler emits a reference to System.Runtime.CompilerServices.IsExternalInit
// for any property that uses the `init` accessor; this empty type satisfies that
// reference without forcing a higher target framework.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
