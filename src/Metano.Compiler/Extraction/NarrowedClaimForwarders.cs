using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// An explicit implementation of a public interface method that only forwards to a
/// less accessible member inherited from a base class
/// (<c>void IDisposable.Dispose() =&gt; Dispose();</c> beside an <c>internal</c>
/// <c>Dispose</c> of the base class). Dropping it would force the claiming member
/// public, and with it the public API of every other subclass of its base (#821). The
/// implementing class declares its own public member under the interface member's name
/// instead, which calls the claiming member through <c>base</c>, and the claiming member
/// keeps its own visibility.
/// <para>
/// A claiming member declared on the implementing class itself cannot coexist with a
/// second member of the same name, so it stays the one member and is public
/// (<see cref="InterfaceContractClaims"/>), as is any other claimer a target cannot
/// forward to. A claiming member that is already public needs no forwarder (FR-111).
/// A target that ignores <c>internal</c> (Dart) keeps dropping the forwarder.
/// </para>
/// </summary>
internal static class NarrowedClaimForwarders
{
    /// <summary>
    /// Whether the target declares the forwarder of <paramref name="lowering"/> as a
    /// member of the implementing class.
    /// </summary>
    public static bool IsPublishedAsOwnMember(
        ExplicitImplementation lowering,
        TargetLanguage? target
    ) =>
        target is not TargetLanguage.Dart
        && lowering
            is {
                Fate: ExplicitImplementationFate.Superseded,
                Member: IMethodSymbol { ContainingType: var owner } member,
                ClaimingMember: IMethodSymbol claiming
            }
        && IsPublic(lowering.InterfaceMember.ContainingType)
        && claiming.DeclaredAccessibility
            is Accessibility.Internal
                or Accessibility.ProtectedAndInternal
        && !SymbolEqualityComparer.Default.Equals(
            claiming.ContainingType.OriginalDefinition,
            owner.OriginalDefinition
        )
        && ForwardingImplementations.IsPureForwarder(member, claiming);

    /// <summary>
    /// <paramref name="declaration"/> of a published forwarder with its call rebound
    /// from <c>this</c> to <c>base</c>: the member is declared under the claiming
    /// member's own name, so a call on <c>this</c> would reach itself. Any other
    /// declaration is returned unchanged.
    /// </summary>
    public static IrMethodDeclaration CallThroughBase(
        IrMethodDeclaration declaration,
        IMethodSymbol method,
        TargetLanguage? target
    ) =>
        ExplicitImplementationLowering.Resolve(method, target) is { } lowering
        && IsPublishedAsOwnMember(lowering, target)
            ? declaration with
            {
                Body = declaration.Body?.Select(RebindStatement).ToList(),
            }
            : declaration;

    /// <summary>Whether the type and all its containers are public.</summary>
    public static bool IsPublic(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not Accessibility.Public)
                return false;
        }

        return true;
    }

    private static IrStatement RebindStatement(IrStatement statement) =>
        statement switch
        {
            IrExpressionStatement { Expression: var expression } => new IrExpressionStatement(
                Rebind(expression)
            ),
            IrReturnStatement { Value: { } value } => new IrReturnStatement(Rebind(value)),
            _ => statement,
        };

    private static IrExpression Rebind(IrExpression expression) =>
        expression
            is IrCallExpression { Target: IrMemberAccess { Target: IrThisExpression } access } call
            ? call with
            {
                Target = access with { Target = new IrBaseExpression() },
            }
            : expression;
}
