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
            ),
            INamedTypeSymbol { TypeKind: TypeKind.Interface } iface => new(
                interfaceDispose,
                IsEmittedInterface(iface, target)
            ),
            var resource => ImplementationDisposal(resource, interfaceDispose, target),
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
        var implementation =
            resource.FindImplementationForInterfaceMember(interfaceDispose) as IMethodSymbol
            ?? PatternDisposeOf(resource);

        if (implementation is null)
            return new(interfaceDispose, IsEmitted: false);

        var method =
            implementation.MethodKind is MethodKind.ExplicitInterfaceImplementation
                ? interfaceDispose
                : implementation;

        return new(method, IsTranspiled(implementation.ContainingType, target));
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
internal sealed record ResourceDisposal(IMethodSymbol Method, bool IsEmitted);
