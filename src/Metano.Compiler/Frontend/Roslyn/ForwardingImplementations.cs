using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Whether an explicit interface implementation only forwards to the member that claims
/// its name (<c>void IDisposable.Dispose() =&gt; Dispose();</c>): its body is one call of
/// that member on <c>this</c>, passing its own parameters in order. Interface calls then
/// reach the claiming member in .NET and in a target without explicit implementations
/// alike, so dropping the forwarder loses nothing (FR-111). The claiming member must be
/// <c>public</c> or <c>internal</c>: a <c>protected</c> or <c>private</c> one cannot
/// satisfy the interface in the target.
/// </summary>
internal static class ForwardingImplementations
{
    /// <summary>
    /// Whether <paramref name="member"/> is satisfied through <paramref name="claiming"/>:
    /// a member with no source (a referenced assembly, whose own build validated it) is;
    /// a source member is when it is a pure forwarder to an accessible claiming method.
    /// </summary>
    public static bool IsSatisfiedThrough(ISymbol member, ISymbol claiming)
    {
        if (member.DeclaringSyntaxReferences.IsEmpty)
            return true;

        return claiming
                is IMethodSymbol
                {
                    DeclaredAccessibility: Accessibility.Public or Accessibility.Internal,
                }
            && IsPureForwarder(member, claiming);
    }

    /// <summary>
    /// Whether the body of <paramref name="member"/> is one call of
    /// <paramref name="claiming"/> on <c>this</c>, passing its own parameters in order,
    /// whatever the accessibility of <paramref name="claiming"/>.
    /// </summary>
    public static bool IsPureForwarder(ISymbol member, ISymbol claiming) =>
        member is IMethodSymbol method
        && claiming is IMethodSymbol target
        && ForwardedCallOf(method) is { } call
        && IsForwardingCall(call, method, target);

    private static IInvocationOperation? ForwardedCallOf(IMethodSymbol method)
    {
        if (
            method.ContainingAssembly is not ISourceAssemblySymbol { Compilation: var compilation }
            || method.DeclaringSyntaxReferences is not [var reference]
            || reference.GetSyntax() is not MethodDeclarationSyntax declaration
            || SoleExpressionOf(declaration) is not { } expression
        )
            return null;

        var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);

        return semantic.GetOperation(expression) as IInvocationOperation;
    }

    /// <summary>
    /// The one expression the body evaluates: an expression body, or a block holding a
    /// single expression statement or <c>return</c>.
    /// </summary>
    private static ExpressionSyntax? SoleExpressionOf(MethodDeclarationSyntax declaration) =>
        declaration switch
        {
            { ExpressionBody.Expression: var arrow } => arrow,
            { Body.Statements: [ExpressionStatementSyntax statement] } => statement.Expression,
            { Body.Statements: [ReturnStatementSyntax { Expression: { } returned }] } => returned,
            _ => null,
        };

    private static bool IsForwardingCall(
        IInvocationOperation call,
        IMethodSymbol method,
        IMethodSymbol target
    ) =>
        SymbolEqualityComparer.Default.Equals(
            call.TargetMethod.OriginalDefinition,
            target.OriginalDefinition
        )
        && call.Instance
            is IInstanceReferenceOperation
            {
                ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                Syntax: not BaseExpressionSyntax,
            }
        && PassesParametersInOrder(call, method);

    private static bool PassesParametersInOrder(IInvocationOperation call, IMethodSymbol method) =>
        call.Arguments.Length == method.Parameters.Length
        && call.Arguments.Select((argument, index) => (argument, index))
            .All(pair =>
                pair.argument
                    is {
                        ArgumentKind: ArgumentKind.Explicit,
                        Value: IParameterReferenceOperation { Parameter: var parameter },
                    }
                && SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[pair.index])
                && pair.argument.Parameter?.Ordinal == pair.index
            );
}
