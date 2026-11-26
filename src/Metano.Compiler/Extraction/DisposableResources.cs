using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <c>Dispose</c> method a <c>using</c> calls on a resource of a static type (FR-111),
/// and whether the target emits it:
/// <list type="bullet">
///   <item>an interface or type parameter: <c>IDisposable.Dispose</c>, emitted when the
///   interface is <c>IDisposable</c> or transpiled (a constraint that is, for a type
///   parameter) — a BCL interface such as <c>IEnumerator&lt;T&gt;</c> is not;</item>
///   <item>a class or struct: its implementation of <c>IDisposable.Dispose</c> (the
///   interface member itself for an explicit implementation, which the target declares
///   under the interface name), or the pattern-based <c>Dispose()</c> of a
///   <c>ref struct</c>; emitted when the implementing type is transpiled.</item>
/// </list>
/// </summary>
internal static class DisposableResources
{
    /// <summary>The disposal of a resource of static type <paramref name="type"/>.</summary>
    public static ResourceDisposal DisposalOf(
        ITypeSymbol? type,
        Compilation compilation,
        TargetLanguage? target
    )
    {
        var interfaceDispose = InterfaceDisposeOf(compilation);

        return type?.UnwrapNullableValueType() switch
        {
            null => new(interfaceDispose, IsEmitted: true),
            ITypeParameterSymbol parameter => new(
                interfaceDispose,
                IsEmittedConstraint(parameter, interfaceDispose, target)
            )
            {
                IsContractTakenOnByThisCompilation =
                    ClassConstraintOf(parameter) is not { } constraint
                    || (
                        IsContractTakenOnBy(constraint, compilation.Assembly, target)
                        && !IsInlinedDispose(constraint, interfaceDispose)
                    ),
            },
            INamedTypeSymbol { TypeKind: TypeKind.Interface } iface => new(
                interfaceDispose,
                IsEmittedInterface(iface, target)
            ),
            var resource => ImplementationDisposal(resource, interfaceDispose, target) with
            {
                IsContractTakenOnByThisCompilation =
                    IsContractTakenOnBy(resource, compilation.Assembly, target)
                    && !IsInlinedDispose(resource, interfaceDispose),
            },
        };
    }

    private static IMethodSymbol InterfaceDisposeOf(Compilation compilation) =>
        compilation
            .GetSpecialType(SpecialType.System_IDisposable)
            .GetMembers(nameof(IDisposable.Dispose))
            .OfType<IMethodSymbol>()
            .First();

    private static ResourceDisposal ImplementationDisposal(
        ITypeSymbol resource,
        IMethodSymbol interfaceDispose,
        TargetLanguage? target
    )
    {
        var interfaceImplementation =
            resource.FindImplementationForInterfaceMember(interfaceDispose) as IMethodSymbol;

        var implementation = interfaceImplementation ?? PatternDisposeOf(resource);

        if (implementation is null)
            return new(interfaceDispose, IsEmitted: false);

        var method =
            implementation.MethodKind is MethodKind.ExplicitInterfaceImplementation
                ? interfaceDispose
                : implementation;

        return new(method, IsTranspiled(implementation.ContainingType, target))
        {
            Shadowing = ShadowingDisposeOf(resource, implementation, target),
            IsPatternBased = interfaceImplementation is null,
        };
    }

    /// <summary>
    /// Whether the class or struct that takes on <c>IDisposable</c> for
    /// <paramref name="resource"/> (itself, or the first base class that implements it
    /// without inheriting it) is declared in <paramref name="assembly"/> and emitted there:
    /// transpiled, and not bound to a hand-written <c>[Import]</c> declaration (which an
    /// <c>[assembly: TranspileAssembly]</c> project still counts as transpiled).
    /// <c>[External]</c> applies only to static classes, which implement no interface.
    /// </summary>
    private static bool IsContractTakenOnBy(
        ITypeSymbol resource,
        IAssemblySymbol assembly,
        TargetLanguage? target
    )
    {
        var taker = ContractTakerOf(resource as INamedTypeSymbol);

        return taker is not null
            && SymbolEqualityComparer.Default.Equals(taker.ContainingAssembly, assembly)
            && !taker.HasImport()
            && IsTranspiled(taker, target);
    }

    /// <summary>
    /// The class a type parameter is constrained to (directly or through another type
    /// parameter): its values are instances of that class or of a class derived from it, so
    /// they carry a disposal-protocol member only when that class's contract does.
    /// </summary>
    private static INamedTypeSymbol? ClassConstraintOf(ITypeParameterSymbol parameter) =>
        parameter
            .ConstraintTypes.Select(constraint =>
                constraint switch
                {
                    ITypeParameterSymbol nested => ClassConstraintOf(nested),
                    INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named => named,
                    _ => null,
                }
            )
            .FirstOrDefault(constraint => constraint is not null);

    /// <summary>
    /// The resource's <c>Dispose</c> is <c>[Emit]</c>: its calls are inlined and the type
    /// declares no disposal member, so only a lowering that calls it can dispose it.
    /// </summary>
    private static bool IsInlinedDispose(ITypeSymbol resource, IMethodSymbol interfaceDispose) =>
        resource.FindImplementationForInterfaceMember(interfaceDispose)?.HasEmit() == true;

    private static INamedTypeSymbol? ContractTakerOf(INamedTypeSymbol? type)
    {
        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (!ImplementsDisposable(candidate.BaseType))
                return ImplementsDisposable(candidate) ? candidate : null;
        }

        return null;
    }

    private static bool ImplementsDisposable(INamedTypeSymbol? type) =>
        type is not null
        && type.AllInterfaces.Any(contract =>
            contract.SpecialType is SpecialType.System_IDisposable
        );

    /// <summary>
    /// A <c>Dispose</c> method a type between <paramref name="resource"/> and the declarer of
    /// <paramref name="implementation"/> adds without overriding it (<c>Dispose(bool)</c>
    /// below a base <c>Dispose()</c>): the target emits it under the name the disposal
    /// calls, so the call would reach it instead of the implementation. A declarer that
    /// dispatches several <c>Dispose</c> overloads passes the added ones its dispatcher, so
    /// none shadows. <c>null</c> when nothing shadows the implementation.
    /// </summary>
    private static IMethodSymbol? ShadowingDisposeOf(
        ITypeSymbol resource,
        IMethodSymbol implementation,
        TargetLanguage? target
    )
    {
        var declarer = implementation.ContainingType;

        if (EmittedDisposeMethodsOf(declarer, target).Count() >= 2)
            return null;

        for (
            var type = resource as INamedTypeSymbol;
            type is not null && !SymbolEqualityComparer.Default.Equals(type, declarer);
            type = type.BaseType
        )
        {
            var added = EmittedDisposeMethodsOf(type, target)
                .FirstOrDefault(method => !Overrides(method, implementation));

            if (added is not null)
                return added;
        }

        return null;
    }

    private static IEnumerable<IMethodSymbol> EmittedDisposeMethodsOf(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        IsTranspiled(type, target)
            ? type.GetMembers(nameof(IDisposable.Dispose))
                .OfType<IMethodSymbol>()
                .Where(method =>
                    method is { MethodKind: MethodKind.Ordinary, IsStatic: false }
                    && !method.HasName()
                    && !SymbolHelper.HasIgnore(method, target)
                )
            : [];

    private static bool Overrides(IMethodSymbol method, IMethodSymbol implementation)
    {
        for (
            var overridden = method;
            overridden is not null;
            overridden = overridden.OverriddenMethod
        )
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    overridden.OriginalDefinition,
                    implementation.OriginalDefinition
                )
            )
                return true;
        }

        return false;
    }

    /// <summary>The parameterless instance <c>Dispose()</c> a <c>ref struct</c> resource binds.</summary>
    private static IMethodSymbol? PatternDisposeOf(ITypeSymbol resource) =>
        resource.IsRefLikeType
            ? resource
                .GetMembers(nameof(IDisposable.Dispose))
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method is { IsStatic: false, Parameters.Length: 0 })
            : null;

    private static bool IsEmittedConstraint(
        ITypeParameterSymbol parameter,
        IMethodSymbol interfaceDispose,
        TargetLanguage? target
    ) =>
        parameter.ConstraintTypes.Any(constraint =>
            constraint switch
            {
                ITypeParameterSymbol nested => IsEmittedConstraint(
                    nested,
                    interfaceDispose,
                    target
                ),
                INamedTypeSymbol { TypeKind: TypeKind.Interface } iface => IsEmittedInterface(
                    iface,
                    target
                ),
                INamedTypeSymbol named => ImplementationDisposal(
                    named,
                    interfaceDispose,
                    target
                ).IsEmitted,
                _ => false,
            }
        );

    private static bool IsEmittedInterface(INamedTypeSymbol iface, TargetLanguage? target) =>
        DisposableInterfaces.Is(iface) || IsTranspiled(iface, target);

    private static bool IsTranspiled(INamedTypeSymbol? type, TargetLanguage? target) =>
        type is not null && IrTypeRefMapper.IsTranspilableType(type, target);
}

/// <summary>The <c>Dispose</c> a <c>using</c> calls, and whether the target emits it.</summary>
internal sealed record ResourceDisposal(IMethodSymbol Method, bool IsEmitted)
{
    /// <summary>
    /// The method a call of <see cref="Method"/> by its name would reach instead, as a
    /// derived type adds a <c>Dispose</c> overload without overriding it; <c>null</c> when
    /// none does.
    /// </summary>
    public IMethodSymbol? Shadowing { get; init; }

    /// <summary>
    /// The resource does not implement <c>IDisposable</c>: <see cref="Method"/> is the
    /// pattern-based <c>Dispose()</c> of a <c>ref struct</c>, which no interface member
    /// reaches.
    /// </summary>
    public bool IsPatternBased { get; init; }

    /// <summary>
    /// The resource's <c>IDisposable</c> contract is taken on by a class or struct this
    /// compilation emits, so a target bridge that adds a protocol member to such types
    /// (TypeScript's <c>[Symbol.dispose]()</c>) gave it one. Always <c>true</c> for an
    /// interface or type-parameter resource, whose runtime class is not known statically.
    /// </summary>
    public bool IsContractTakenOnByThisCompilation { get; init; } = true;
}
