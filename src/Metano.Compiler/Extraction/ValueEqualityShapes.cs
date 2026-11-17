using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
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

    /// <summary>
    /// Whether the overload groups keep <paramref name="method"/>, an explicit
    /// <c>IEquatable&lt;S&gt;.Equals(S)</c> of struct <c>S</c>, that the lowering
    /// supersedes only because <c>System.ValueType.Equals</c> holds its name. For a
    /// target that names the synthesized <c>Equals</c> the struct's equality is that
    /// member's, which the implementation replaces (B2); every other target, and
    /// every other explicit implementation, keeps the lowering's fate.
    /// </summary>
    public static bool KeepsSupersededEquatable(
        IMethodSymbol method,
        ExplicitImplementation lowering,
        OverloadGroupingPolicy policy
    ) =>
        policy.NameOf(IrSynthesizedValueMember.Equals) is not null
        && lowering
            is {
                Fate: ExplicitImplementationFate.Superseded,
                ClaimingMember: IMethodSymbol
                {
                    ContainingType.SpecialType: SpecialType.System_ValueType
                },
            }
        && method.ContainingType is { TypeKind: TypeKind.Struct, IsRecord: false } type
        && method.Parameters is [{ Type: var parameter }]
        && IsTypeItself(parameter, type)
        && method.ExplicitInterfaceImplementations.Any(member =>
            IsEquatableOf(member.ContainingType, type)
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
            .Where(method => MethodOverloadGroups.GroupNameOf(method) == nameof(Equals))
            .Where(method => !MethodOverloadGroups.IsDroppedExplicitImplementation(method, policy));

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
        type
            .AllInterfaces.Where(implemented =>
                IsEquatableOf(implemented, type)
                || MethodOverloadGroups.IsTranspiledInterface(implemented, target)
            )
            .SelectMany(implemented =>
                implemented.GetMembers(nameof(Equals)).OfType<IMethodSymbol>()
            )
            .Any(member =>
                SymbolEqualityComparer.Default.Equals(
                    type.FindImplementationForInterfaceMember(member)?.OriginalDefinition,
                    method.OriginalDefinition
                )
            );

    private static bool IsEquatableOf(INamedTypeSymbol candidate, INamedTypeSymbol type) =>
        candidate
            is {
                Name: "IEquatable",
                ContainingNamespace.Name: "System",
                TypeArguments: [var argument],
            }
        && IsTypeItself(argument, type);

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
