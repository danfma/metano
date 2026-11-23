using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Whether <paramref name="symbol"/> is a static method of an extension container
    /// the toolchain emits (<see cref="SymbolHelper.IsEmittedExtensionContainer"/>):
    /// the fact a target that emits such a class as a module reads to flatten the call
    /// (#554).
    /// </summary>
    private bool IsStaticMethodOfExtensionContainer(ISymbol symbol) =>
        symbol is IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary }
        && symbol.ContainingType is { } owner
        && owner.IsEmittedExtensionContainer(_target);
}
