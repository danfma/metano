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
    public static IrValueEqualityShape Of(INamedTypeSymbol type, OverloadGroupingPolicy policy)
    {
        var kind = KindOf(type);

        if (kind is IrValueEqualityKind.None)
            return IrValueEqualityShape.None;

        return new IrValueEqualityShape(
            kind,
            ReplacesEquals: ReplacingEqualsOf(type, policy).Count > 0,
            ReplacesHashCode: ReplacingHashCodeOf(type, policy.Target) is not null
        );
    }

    /// <summary>
    /// The declarations C# uses instead of the synthesized <c>Equals(object)</c>:
    /// a record's <c>Equals(R)</c> (<see cref="ReplacesRecordEquals"/>); a struct's
    /// <c>override Equals(object)</c>, and its <c>Equals(S)</c> when it implements
    /// <c>IEquatable&lt;S&gt;.Equals</c> — which <c>EqualityComparer&lt;S&gt;.Default</c>
    /// (sets, dictionaries, record fields) calls — or a transpiled interface's
    /// <c>Equals(S)</c> (<see cref="ReplacesStructEquals"/>). A struct's
    /// <c>Equals(S)</c> that implements nothing replaces nothing:
    /// <c>ValueType.Equals</c> never calls it.
    /// </summary>
    public static IReadOnlyList<IMethodSymbol> ReplacingEqualsOf(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    ) =>
        EqualsMethodsOf(type, policy)
            .Where(method =>
                method.Parameters is [{ Type: var parameter }]
                && (
                    type.IsRecord
                        ? ReplacesRecordEquals(type, method, parameter)
                        : ReplacesStructEquals(type, method, parameter, policy.Target)
                )
            )
            .ToList();

    /// <summary>
    /// The replacing declaration the synthesized <c>Equals(object)</c> takes the
    /// body of: a struct's <c>override Equals(object)</c>, the same signature.
    /// </summary>
    public static IMethodSymbol? FoldedEqualsOf(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    ) =>
        type.IsRecord
            ? null
            : ReplacingEqualsOf(type, policy)
                .FirstOrDefault(method => IsObjectOverride(method, method.Parameters[0].Type));

    /// <summary>
    /// The typed replacing <c>Equals</c> the synthesized <c>Equals(object)</c> routes
    /// to, as C# does: a record's <c>Equals(R)</c>, or a struct's interface-implementing
    /// <c>Equals(S)</c> when it declares no <c>override Equals(object)</c>.
    /// <c>null</c> when there is none.
    /// </summary>
    public static IMethodSymbol? RoutedEqualsOf(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    ) =>
        FoldedEqualsOf(type, policy) is null
            ? ReplacingEqualsOf(type, policy)
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

    private static bool ReplacesRecordEquals(
        INamedTypeSymbol type,
        IMethodSymbol method,
        ITypeSymbol parameter
    ) => method.MethodKind is MethodKind.Ordinary && IsTypeItself(parameter, type);

    private static bool ReplacesStructEquals(
        INamedTypeSymbol type,
        IMethodSymbol method,
        ITypeSymbol parameter,
        TargetLanguage? target
    ) =>
        IsObjectOverride(method, parameter)
        || IsTypeItself(parameter, type) && ImplementsEqualityInterfaceMember(type, method, target);

    /// <summary>
    /// The methods grouped under <c>Equals</c> the target declares: ordinary ones
    /// and the explicit implementations the overload groups keep.
    /// </summary>
    private static IEnumerable<IMethodSymbol> EqualsMethodsOf(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    ) =>
        UserMethodsOf(type, policy.Target)
            .Where(method => OverloadGroupName.Of(method) == nameof(Equals))
            .Where(method => !ExplicitImplementationFates.IsDropped(method, policy));

    private static bool IsObjectOverride(IMethodSymbol method, ITypeSymbol parameter) =>
        method.IsOverride && parameter.SpecialType is SpecialType.System_Object;

    /// <summary>
    /// Whether <paramref name="method"/> implements <c>IEquatable&lt;S&gt;.Equals</c>
    /// or an <c>Equals</c> of a transpiled interface, implicitly or explicitly.
    /// </summary>
    private static bool ImplementsEqualityInterfaceMember(
        INamedTypeSymbol type,
        IMethodSymbol method,
        TargetLanguage? target
    ) =>
        ImplementsEqualsOf(type, method, implemented =>
            EquatableInterfaces.IsEquatableOf(implemented, type)
            || EmittedTypes.IsTranspiledInterface(implemented, target)
        );

    /// <summary>
    /// Whether <paramref name="method"/> implements an <c>Equals</c> of an interface
    /// of <paramref name="type"/> that <paramref name="counts"/> accepts.
    /// </summary>
    private static bool ImplementsEqualsOf(
        INamedTypeSymbol type,
        IMethodSymbol method,
        Func<INamedTypeSymbol, bool> counts
    ) =>
        type
            .AllInterfaces.Where(counts)
            .SelectMany(implemented =>
                implemented.GetMembers(nameof(Equals)).OfType<IMethodSymbol>()
            )
            .Any(member =>
                SymbolEqualityComparer.Default.Equals(
                    type.FindImplementationForInterfaceMember(member)?.OriginalDefinition,
                    method.OriginalDefinition
                )
            );

    /// <summary>
    /// A struct's replacing <c>Equals(S)</c> that implements a transpiled
    /// interface's <c>Equals</c> but not <c>System.IEquatable&lt;S&gt;</c>: .NET's
    /// <c>EqualityComparer&lt;S&gt;.Default</c> compares such a struct field by field,
    /// while the target's sets, dictionaries and record fields reach the user's
    /// equality. <c>null</c> when there is none, and when the struct overrides
    /// <c>Equals(object)</c>, which .NET's comparer then calls too.
    /// </summary>
    public static IMethodSymbol? TranspiledOnlyEqualsOf(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    ) =>
        KindOf(type) is IrValueEqualityKind.Struct && FoldedEqualsOf(type, policy) is null
            ? ReplacingEqualsOf(type, policy)
                .FirstOrDefault(method =>
                    IsTypeItself(method.Parameters[0].Type, type)
                    && !ImplementsEqualsOf(type, method, implemented =>
                        EquatableInterfaces.IsEquatableOf(implemented, type)
                    )
                )
            : null;

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
