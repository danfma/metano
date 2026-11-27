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
internal static partial class ForwardingImplementations
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
    /// Whether <paramref name="member"/> only forwards to <paramref name="claiming"/> on
    /// <c>this</c>, whatever the accessibility of <paramref name="claiming"/>: a method
    /// whose body is one call of it passing its own parameters in order, or a property or
    /// event whose every accessor only reads, writes, adds to or removes from it.
    /// </summary>
    public static bool IsPureForwarder(ISymbol member, ISymbol claiming) =>
        (member, claiming) switch
        {
            (IMethodSymbol method, IMethodSymbol target) => ForwardedCallOf(method) is { } call
                && IsForwardingCall(call, method, target),
            (IPropertySymbol property, IPropertySymbol target) => IsPurePropertyForwarder(
                property,
                target
            ),
            (IEventSymbol evt, IEventSymbol target) => IsPureEventForwarder(evt, target),
            _ => false,
        };

    /// <summary>
    /// The member a forwarder reaches on <c>this</c>: the method its body calls, or the
    /// property its getter reads, or the event its <c>add</c> accessor adds to;
    /// <c>null</c> when its body is no such single access.
    /// </summary>
    public static ISymbol? ForwardedMemberOf(ISymbol member) =>
        member switch
        {
            IMethodSymbol method => ForwardedCallOf(method)?.TargetMethod,
            IPropertySymbol property => ReadPropertyOf(property),
            IEventSymbol evt => AddedEventOf(evt),
            _ => null,
        };

    private static IInvocationOperation? ForwardedCallOf(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences is [var reference]
        && reference.GetSyntax() is MethodDeclarationSyntax declaration
        && SoleExpressionOf(declaration.ExpressionBody, declaration.Body) is { } expression
            ? OperationOf(method, expression) as IInvocationOperation
            : null;

    /// <summary>
    /// The one expression a body evaluates: an expression body, or a block holding a
    /// single expression statement or <c>return</c>.
    /// </summary>
    private static ExpressionSyntax? SoleExpressionOf(
        ArrowExpressionClauseSyntax? arrow,
        BlockSyntax? block
    ) =>
        (arrow, block) switch
        {
            ({ Expression: var expression }, _) => expression,
            (_, { Statements: [ExpressionStatementSyntax statement] }) => statement.Expression,
            (_, { Statements: [ReturnStatementSyntax { Expression: { } returned }] }) => returned,
            _ => null,
        };

    /// <summary>
    /// The operation of <paramref name="expression"/>, a part of the source of
    /// <paramref name="member"/>; <c>null</c> for a member of a referenced assembly.
    /// </summary>
    private static IOperation? OperationOf(ISymbol member, ExpressionSyntax expression) =>
        member.ContainingAssembly is ISourceAssemblySymbol { Compilation: var compilation }
            ? compilation.GetSemanticModel(expression.SyntaxTree).GetOperation(expression)
            : null;

    private static bool IsForwardingCall(
        IInvocationOperation call,
        IMethodSymbol method,
        IMethodSymbol target
    ) =>
        SymbolEqualityComparer.Default.Equals(
            call.TargetMethod.OriginalDefinition,
            target.OriginalDefinition
        )
        && IsOnThis(call.Instance)
        && PassesParametersInOrder(call, method);

    /// <summary>
    /// An implicit or explicit <c>this</c> receiver: <c>base.M()</c> skips the overrides
    /// of the derived classes, so it does not forward to the member that claims the name.
    /// </summary>
    private static bool IsOnThis(IOperation? instance) =>
        instance
            is IInstanceReferenceOperation
            {
                ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                Syntax: not BaseExpressionSyntax,
            };

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
