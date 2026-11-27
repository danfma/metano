using Metano.Compiler.Diagnostics;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// An explicit implementation of a public interface member that only forwards to a
/// less accessible member inherited from a base class
/// (<c>void IDisposable.Dispose() =&gt; Dispose();</c> beside an <c>internal</c>
/// <c>Dispose</c> of the base class, <c>bool IChangeTracking.IsChanged =&gt; IsChanged;</c>
/// beside an <c>internal</c> property). Dropping it would force the claiming member
/// public, and with it the public API of every other subclass of its base (#821, #822).
/// The implementing class declares its own public member under the interface member's
/// name instead, which reaches the claiming member through <c>base</c>, and the claiming
/// member keeps its own visibility. A method forwarder calls it
/// (<see cref="CallThroughBase"/>); a property or event forwarder re-publishes it
/// (<see cref="RepublishInherited"/>, <see cref="RepublishesInherited"/>).
/// <para>
/// The 4.0.0 path (the claiming member public, the forwarder dropped) stays for a
/// claiming member declared on the implementing class itself (it cannot coexist with a
/// second member of the same name), one that is already public (FR-111), and a target
/// that hides no internal member (<see cref="OverloadGroupingPolicy.HidesInternalMembers"/>,
/// Dart). Any other shape the class cannot publish keeps it too, with a warning
/// (<see cref="ReportUnpublished"/>).
/// </para>
/// </summary>
public static partial class NarrowedClaimForwarders
{
    /// <summary>
    /// Whether the target declares the explicit implementation <paramref name="member"/>
    /// as a member of the implementing class.
    /// </summary>
    public static bool IsPublishedAsOwnMember(ISymbol member, OverloadGroupingPolicy policy) =>
        ExplicitImplementationLowering.Resolve(member, policy.Target) is { } lowering
        && IsPublishedAsOwnMember(lowering, policy);

    /// <summary>
    /// Whether the target declares the forwarder of <paramref name="lowering"/> as a
    /// member of the implementing class.
    /// </summary>
    internal static bool IsPublishedAsOwnMember(
        ExplicitImplementation lowering,
        OverloadGroupingPolicy policy
    ) =>
        policy.HidesInternalMembers
        && NarrowedClaimOf(lowering) is { } claim
        && ObstacleOf(claim, policy) is null;

    /// <summary>
    /// <paramref name="declaration"/> of a published forwarder with its call rebound
    /// from <c>this</c> to <c>base</c>: the member is declared under the claiming
    /// member's own name, so a call on <c>this</c> would reach itself. A body that is
    /// not the single call on <c>this</c> is reported as an error. A signature-only
    /// extraction (no compilation) has no body to rebind, and any other declaration is
    /// returned unchanged.
    /// </summary>
    internal static IrMethodDeclaration CallThroughBase(
        IrMethodDeclaration declaration,
        IMethodSymbol method,
        IrExtractionContext context
    )
    {
        if (context.Compilation is null || !IsPublishedAsOwnMember(method, context.GroupingPolicy))
            return declaration;

        if (declaration.Body is [var statement] && TryRebind(statement) is { } rebound)
            return declaration with { Body = [rebound] };

        ReportUnrebindable(method);

        return declaration;
    }

    /// <summary>
    /// A superseded explicit implementation of a public interface member whose claiming
    /// member, of the same kind, is less accessible and inherited from a base class.
    /// An indexer claims nothing here: no target emits one.
    /// </summary>
    private static NarrowedClaim? NarrowedClaimOf(ExplicitImplementation lowering) =>
        lowering
            is {
                Fate: ExplicitImplementationFate.Superseded,
                Member: { ContainingType: var owner } member,
                ClaimingMember: { } claiming
            }
        && member.Kind == claiming.Kind
        && member is not IPropertySymbol { IsIndexer: true }
        && claiming is not IPropertySymbol { IsIndexer: true }
        && IsNarrowerThanInterface(lowering.InterfaceMember, claiming)
        && IsInheritedClaimer(claiming, owner)
            ? new NarrowedClaim(member, claiming, owner)
            : null;

    private static bool IsNarrowerThanInterface(ISymbol interfaceMember, ISymbol claiming) =>
        SymbolHelper.IsEffectivelyPublic(interfaceMember.ContainingType)
        && claiming.DeclaredAccessibility
            is Accessibility.Internal
                or Accessibility.ProtectedAndInternal;

    private static bool IsInheritedClaimer(ISymbol claiming, INamedTypeSymbol owner) =>
        !SymbolEqualityComparer.Default.Equals(
            claiming.ContainingType.OriginalDefinition,
            owner.OriginalDefinition
        );

    /// <summary>
    /// Why the implementing class cannot publish <paramref name="claim"/>, or <c>null</c>
    /// when it can.
    /// </summary>
    private static string? ObstacleOf(NarrowedClaim claim, OverloadGroupingPolicy policy) =>
        claim switch
        {
            { Claiming.IsAbstract: true } =>
                "the member it forwards to is abstract, and the generated class cannot reach "
                    + "an abstract member through 'super'",
            { Claiming.IsStatic: true } => "it is static",
            _ when !HasClaimersType(claim) => TypeMismatchOf(claim),
            { Claiming: IMethodSymbol method } when HasOverloadBeside(method, claim.Owner) =>
                "the member it forwards to has overloads, whose dispatched group the forwarder "
                    + "would join",
            { Member: IPropertySymbol { GetMethod: null } } => "it has no getter",
            { Claiming: IPropertySymbol property } when HasUnpublishableSetter(property, policy) =>
                "the property it forwards to has accessors and an 'init' or private setter, "
                    + "which the generated class cannot override",
            _ when !ForwardingImplementations.IsPureForwarder(claim.Member, claim.Claiming) =>
                "its body does more than forward to the member",
            _ => null,
        };

    private static bool HasClaimersType(NarrowedClaim claim) =>
        SymbolEqualityComparer.Default.Equals(TypeOf(claim.Member), TypeOf(claim.Claiming));

    private static string TypeMismatchOf(NarrowedClaim claim) =>
        claim.Member is IMethodSymbol { IsGenericMethod: true }
            ? "it is a generic method, whose return type is not the one of the method it "
                + "forwards to"
            : "its type is not the type of the member it forwards to";

    private static ITypeSymbol? TypeOf(ISymbol member) =>
        member switch
        {
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IEventSymbol evt => evt.Type,
            _ => null,
        };

    /// <summary>
    /// An overload beside the claiming method makes the forwarder join a dispatched
    /// group.
    /// </summary>
    private static bool HasOverloadBeside(IMethodSymbol claiming, INamedTypeSymbol owner)
    {
        for (var type = owner; type is not null; type = type.BaseType)
        {
            if (type.SpecialType is SpecialType.System_Object)
                break;

            if (type.GetMembers(claiming.Name).Any(other => IsOverloadOf(other, claiming)))
                return true;
        }

        return false;
    }

    private static bool IsOverloadOf(ISymbol other, IMethodSymbol claiming) =>
        other is IMethodSymbol { MethodKind: MethodKind.Ordinary } method
        && !SymbolHelper.HasSameParameters(method, claiming);

    private static IrStatement? TryRebind(IrStatement statement)
    {
        if (statement is IrExpressionStatement { Expression: var expression })
            return RebindCall(expression) is { } call ? new IrExpressionStatement(call) : null;

        if (statement is IrReturnStatement { Value: { } value })
            return RebindCall(value) is { } call ? new IrReturnStatement(call) : null;

        return null;
    }

    private static IrExpression? RebindCall(IrExpression expression) =>
        expression
            is IrCallExpression { Target: IrMemberAccess { Target: IrThisExpression } access } call
            ? call with
            {
                Target = access with { Target = new IrBaseExpression() },
            }
            : null;

    private static void ReportUnrebindable(IMethodSymbol method) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                $"Explicit implementation '{method.ToDisplayString()}' forwards to an inherited "
                    + "member, but its body is not a single call on 'this' that can be "
                    + "rebound to 'base'; call the member directly from a plain method.",
                method.Locations.FirstOrDefault()
            )
        );

    /// <summary>
    /// An explicit implementation and the less accessible inherited member it leans on.
    /// </summary>
    private sealed record NarrowedClaim(ISymbol Member, ISymbol Claiming, INamedTypeSymbol Owner);
}
