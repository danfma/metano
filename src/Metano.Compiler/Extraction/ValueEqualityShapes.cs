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
            ReplacesEquals: ReplacingEqualsOf(type, target).Count > 0,
            ReplacesHashCode: ReplacingHashCodeOf(type, target) is not null
        );
    }

    /// <summary>
    /// The declarations C# uses instead of the synthesized <c>Equals(object)</c>:
    /// a record's <c>Equals(R)</c>; a struct's <c>override Equals(object)</c>, and
    /// its <c>Equals(S)</c> when it implements an interface member — the
    /// <c>IEquatable&lt;S&gt;.Equals</c> that <c>EqualityComparer&lt;S&gt;.Default</c>
    /// (sets, dictionaries, record fields) calls, implicitly or explicitly, or a
    /// transpiled interface's. A struct's <c>Equals(S)</c> that implements nothing
    /// replaces nothing: <c>ValueType.Equals</c> never calls it.
    /// </summary>
    public static IReadOnlyList<IMethodSymbol> ReplacingEqualsOf(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        EqualsMethodsOf(type, target)
            .Where(method =>
                method.Parameters is [{ Type: var parameter }]
                && (
                    type.IsRecord
                        ? method.MethodKind is MethodKind.Ordinary && IsTypeItself(parameter, type)
                        : IsObjectOverride(method, parameter)
                            || IsTypeItself(parameter, type)
                                && ImplementsInterfaceMember(type, method)
                )
            )
            .ToList();

    /// <summary>
    /// The replacing declaration the synthesized <c>Equals(object)</c> takes the
    /// body of: a struct's <c>override Equals(object)</c>, the same signature.
    /// </summary>
    public static IMethodSymbol? FoldedEqualsOf(INamedTypeSymbol type, TargetLanguage? target) =>
        type.IsRecord
            ? null
            : ReplacingEqualsOf(type, target)
                .FirstOrDefault(method => IsObjectOverride(method, method.Parameters[0].Type));

    /// <summary>
    /// The typed replacing <c>Equals</c> the synthesized <c>Equals(object)</c> routes
    /// to, as C# does: a record's <c>Equals(R)</c>, or a struct's interface-implementing
    /// <c>Equals(S)</c> when it declares no <c>override Equals(object)</c>.
    /// <c>null</c> when there is none.
    /// </summary>
    public static IMethodSymbol? RoutedEqualsOf(INamedTypeSymbol type, TargetLanguage? target) =>
        FoldedEqualsOf(type, target) is null
            ? ReplacingEqualsOf(type, target)
                .FirstOrDefault(method => IsTypeItself(method.Parameters[0].Type, type))
            : null;

    /// <summary>The type's <c>override GetHashCode()</c>; <c>null</c> when it declares none.</summary>
    public static IMethodSymbol? ReplacingHashCodeOf(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        UserMethodsOf(type, target)
            .FirstOrDefault(method =>
                method is { Name: nameof(GetHashCode), IsOverride: true, Parameters.Length: 0 }
            );

    private static IEnumerable<IMethodSymbol> EqualsMethodsOf(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        UserMethodsOf(type, target)
            .Where(method => MethodOverloadGroups.GroupNameOf(method) == nameof(Equals));

    private static bool IsObjectOverride(IMethodSymbol method, ITypeSymbol parameter) =>
        method.IsOverride && parameter.SpecialType is SpecialType.System_Object;

    private static bool ImplementsInterfaceMember(INamedTypeSymbol type, IMethodSymbol method) =>
        !method.ExplicitInterfaceImplementations.IsEmpty
        || type.AllInterfaces.SelectMany(implemented =>
                implemented.GetMembers(nameof(Equals)).OfType<IMethodSymbol>()
            )
            .Any(member =>
                SymbolEqualityComparer.Default.Equals(
                    type.FindImplementationForInterfaceMember(member),
                    method
                )
            );

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
        TargetLanguage? target
    ) =>
        type
            .OriginalDefinition.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method =>
                method
                    is {
                        IsStatic: false,
                        IsImplicitlyDeclared: false,
                        MethodKind: MethodKind.Ordinary
                            or MethodKind.ExplicitInterfaceImplementation,
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
