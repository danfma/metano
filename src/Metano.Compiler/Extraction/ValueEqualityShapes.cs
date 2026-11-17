using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads a type's value equality from its symbols (spec 013 FR-045): which
/// members C# synthesizes and which declarations replace them. Every reader —
/// the overload groups, the targets' bridges, the runtime-requirement scanner —
/// sees this one fact (<see cref="IrTypeSemantics.ValueEquality"/>), never a
/// guess from the members of the IR.
/// </summary>
internal static class ValueEqualityShapes
{
    /// <summary>The value equality of <paramref name="type"/>.</summary>
    public static IrValueEqualityShape Of(INamedTypeSymbol type, TargetLanguage? target)
    {
        var kind = KindOf(type);

        if (kind is IrValueEqualityKind.None)
            return IrValueEqualityShape.None;

        return new IrValueEqualityShape(
            kind,
            ReplacesEquals: ReplacingEqualsOf(type, target) is not null,
            ReplacesHashCode: ReplacingHashCodeOf(type, target) is not null
        );
    }

    /// <summary>
    /// The declaration C# uses instead of the synthesized <c>Equals(object)</c>:
    /// a record's <c>Equals(R)</c>, a struct's <c>override Equals(object)</c>.
    /// <c>null</c> when there is none, and for a struct's <c>Equals(S)</c>, which
    /// <c>ValueType.Equals</c> never calls.
    /// </summary>
    public static IMethodSymbol? ReplacingEqualsOf(INamedTypeSymbol type, TargetLanguage? target) =>
        UserMethodsOf(type, target, nameof(object.Equals))
            .FirstOrDefault(method =>
                method.Parameters is [{ Type: var parameter }]
                && (
                    type.IsRecord
                        ? IsTypeItself(parameter, type)
                        : method.IsOverride && parameter.SpecialType is SpecialType.System_Object
                )
            );

    /// <summary>The type's <c>override GetHashCode()</c>; <c>null</c> when it declares none.</summary>
    public static IMethodSymbol? ReplacingHashCodeOf(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        UserMethodsOf(type, target, nameof(object.GetHashCode))
            .FirstOrDefault(method => method is { IsOverride: true, Parameters.Length: 0 });

    private static IrValueEqualityKind KindOf(INamedTypeSymbol type)
    {
        if (!IsEmittedAsClass(type))
            return IrValueEqualityKind.None;

        if (type.IsRecord)
            return IrValueEqualityKind.Record;

        return type.TypeKind is TypeKind.Struct
            ? IrValueEqualityKind.Struct
            : IrValueEqualityKind.None;
    }

    private static bool IsEmittedAsClass(INamedTypeSymbol type) =>
        !type.HasPlainObject()
        && !type.HasJsTuple()
        && !type.HasBranded()
        && !type.IsJsxComponent();

    private static IEnumerable<IMethodSymbol> UserMethodsOf(
        INamedTypeSymbol type,
        TargetLanguage? target,
        string name
    ) =>
        type
            .OriginalDefinition.GetMembers(name)
            .OfType<IMethodSymbol>()
            .Where(method =>
                method
                    is {
                        IsStatic: false,
                        IsImplicitlyDeclared: false,
                        MethodKind: MethodKind.Ordinary
                    }
                && !method.IsCompilerGenerated()
                && !method.HasIgnore(target)
            );

    private static bool IsTypeItself(ITypeSymbol parameter, INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(
            parameter.OriginalDefinition,
            type.OriginalDefinition
        );
}
