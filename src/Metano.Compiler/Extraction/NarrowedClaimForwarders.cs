using Metano.Compiler.Diagnostics;
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
/// The 4.0.0 path (the claiming member public, the forwarder dropped) stays for every
/// other shape: a claiming member declared on the implementing class itself (it cannot
/// coexist with a second member of the same name), one that is already public (FR-111),
/// an abstract one (<c>super.m()</c> of an abstract method is TS2513), one that belongs
/// to an overload group (the forwarder would join the group and its dispatcher), a
/// forwarder whose return type is not the claiming member's
/// (<c>object ICloneable.Clone() =&gt; Clone();</c> over <c>internal Node Clone()</c>), and
/// a target that hides no internal member
/// (<see cref="OverloadGroupingPolicy.HidesInternalMembers"/>, Dart).
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
        OverloadGroupingPolicy policy
    ) =>
        policy.HidesInternalMembers
        && lowering
            is {
                Fate: ExplicitImplementationFate.Superseded,
                Member: IMethodSymbol { ContainingType: var owner } member,
                ClaimingMember: IMethodSymbol claiming
            }
        && IsNarrowerThanInterface(lowering.InterfaceMember, claiming)
        && IsInheritedClaimer(claiming, owner)
        && IsCallableThroughBase(claiming, owner)
        && SymbolEqualityComparer.Default.Equals(member.ReturnType, claiming.ReturnType)
        && ForwardingImplementations.IsPureForwarder(member, claiming);

    /// <summary>
    /// <paramref name="declaration"/> of a published forwarder with its call rebound
    /// from <c>this</c> to <c>base</c>: the member is declared under the claiming
    /// member's own name, so a call on <c>this</c> would reach itself. A body that is
    /// not the single call on <c>this</c> is reported as an error. A signature-only
    /// extraction (no compilation) has no body to rebind, and any other declaration is
    /// returned unchanged.
    /// </summary>
    public static IrMethodDeclaration CallThroughBase(
        IrMethodDeclaration declaration,
        IMethodSymbol method,
        IrExtractionContext context
    )
    {
        if (
            context.Compilation is null
            || ExplicitImplementationLowering.Resolve(method, context.Target) is not { } lowering
            || !IsPublishedAsOwnMember(lowering, context.GroupingPolicy)
        )
            return declaration;

        if (declaration.Body is [var statement] && TryRebind(statement) is { } rebound)
            return declaration with { Body = [rebound] };

        ReportUnrebindable(method);

        return declaration;
    }

    private static bool IsNarrowerThanInterface(ISymbol interfaceMember, IMethodSymbol claiming) =>
        SymbolHelper.IsEffectivelyPublic(interfaceMember.ContainingType)
        && claiming.DeclaredAccessibility
            is Accessibility.Internal
                or Accessibility.ProtectedAndInternal;

    private static bool IsInheritedClaimer(IMethodSymbol claiming, INamedTypeSymbol owner) =>
        !SymbolEqualityComparer.Default.Equals(
            claiming.ContainingType.OriginalDefinition,
            owner.OriginalDefinition
        );

    /// <summary>
    /// A concrete claiming member with no overload beside it: <c>super.m()</c> of an
    /// abstract member does not compile, and an overload makes the forwarder join a
    /// dispatched group.
    /// </summary>
    private static bool IsCallableThroughBase(IMethodSymbol claiming, INamedTypeSymbol owner) =>
        !claiming.IsAbstract && !HasOverloadBeside(claiming, owner);

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
}
