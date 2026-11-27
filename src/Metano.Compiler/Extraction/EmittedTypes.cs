using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Which declaring types the target emits, as the overload groups, the value
/// equality shapes and the direct calls read them (spec 013 DECIDED-10): one
/// neutral home, so none of those readers depends on another for the rule.
/// </summary>
internal static class EmittedTypes
{
    /// <summary>
    /// Whether <paramref name="type"/> — the declaring type of a candidate
    /// direct call — emits the member the call site needs: a transpilable
    /// class, or a transpiled interface, none of the keep-today's-emission
    /// shapes of DECIDED-10: <c>[External]</c> and <c>[PlainObject]</c> are
    /// excluded here, and <c>[JsCallable]</c>, which only an interface
    /// carries, through <see cref="IsTranspiledInterface"/>.
    /// A <c>[Branded]</c> type is judged per method
    /// (<c>DispatchedMethodCalls.KeepsBrandedDispatcher</c>).
    /// </summary>
    public static bool IsEmittedContainingType(INamedTypeSymbol type, TargetLanguage? target)
    {
        if (type.TypeKind == TypeKind.Interface)
            return IsTranspiledInterface(type, target);

        return type.TypeKind is TypeKind.Class or TypeKind.Struct
            && IrTypeRefMapper.IsTranspilableType(type, target)
            && !type.IsAmbient()
            && !SymbolHelper.HasPlainObject(type);
    }

    /// <summary>
    /// A transpiled interface whose groups implementers anchor on: emitted
    /// (not <c>[External]</c>) and a real type (not <c>[JsCallable]</c>).
    /// </summary>
    public static bool IsTranspiledInterface(INamedTypeSymbol type, TargetLanguage? target) =>
        IrTypeRefMapper.IsTranspilableType(type.OriginalDefinition, target)
        && !type.OriginalDefinition.IsAmbient()
        && !SymbolHelper.HasJsCallable(type.OriginalDefinition);

    /// <summary>
    /// A class the target emits, whose dispatched groups a derived class and
    /// a <c>base.M(...)</c> call can reach by fast path: transpiled, not
    /// <c>[External]</c>, not a BCL or special type.
    /// </summary>
    public static bool IsEmittedBase(INamedTypeSymbol? type, TargetLanguage? target) =>
        type is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None }
        && IrTypeRefMapper.IsTranspilableType(type.OriginalDefinition, target)
        && !type.OriginalDefinition.IsAmbient();
}
