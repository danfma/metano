using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Whether <paramref name="symbol"/> is a static method of a static class that
    /// declares extension members (<see cref="SymbolHelper.HasExtensionMembers"/>): the
    /// fact a target that emits such a class as a module reads to flatten the call
    /// (#554). Only methods qualify, so the syntax scan runs for static calls alone.
    /// </summary>
    private static bool IsStaticMethodOfExtensionContainer(ISymbol symbol) =>
        symbol is IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary }
        && symbol.ContainingType is { IsStatic: true } owner
        && owner.HasExtensionMembers();
}
