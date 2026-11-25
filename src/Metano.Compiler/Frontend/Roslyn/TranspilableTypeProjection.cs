using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Projects a top-level transpilable type to the <see cref="IrTranspilableTypeRef"/>
/// emit metadata a target resolves a bare identifier to: the one rule the frontend's
/// name-keyed map and a target's symbol-resolved lookups (#743) both apply.
/// </summary>
public static class TranspilableTypeProjection
{
    /// <summary>
    /// The emit metadata of <paramref name="type"/> printed under
    /// <paramref name="emittedName"/>: its file is its <c>[EmitInFile]</c> name, or
    /// else the emitted name, kebab-cased.
    /// </summary>
    public static IrTranspilableTypeRef Of(INamedTypeSymbol type, string emittedName)
    {
        var emitInFile = SymbolHelper.GetEmitInFile(type);
        var fileBase = emitInFile is { Length: > 0 } ? emitInFile : emittedName;

        return new IrTranspilableTypeRef(
            Key: type.GetCrossAssemblyOriginKey(),
            TsName: emittedName,
            Namespace: type.ContainingNamespace.IsGlobalNamespace
                ? ""
                : type.ContainingNamespace.ToDisplayString(),
            FileName: SymbolHelper.ToKebabCase(fileBase),
            IsStringEnum: SymbolHelper.HasStringEnum(type)
        );
    }
}
