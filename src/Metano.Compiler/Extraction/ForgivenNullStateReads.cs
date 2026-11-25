using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The reads C# flow analysis proves non-null by a construct a static type
/// checker does not follow (#775): after <c>stack!.Value</c>, C# treats
/// <c>stack</c> as not-null for the rest of the flow, and so it does after a
/// call whose nullable attributes say so (<c>ArgumentNullException.ThrowIfNull(x)</c>,
/// <c>string.IsNullOrEmpty(s)</c> false, a <c>[MemberNotNull]</c> method). The
/// variable is still declared nullable, so a target that checks nullability
/// statically needs the read asserted non-null where the value is used as
/// non-null: as the receiver of a member access, an invocation or an indexer,
/// and as an argument to a parameter declared non-nullable.
/// <para>
/// A read whose not-null state comes from an assignment or a null test of the
/// same variable is left alone: every static checker narrows on those. The
/// state's origin is the last such event before the read in the member's
/// source, a syntactic approximation of the flow.
/// </para>
/// </summary>
internal static class ForgivenNullStateReads
{
    /// <summary>Whether <paramref name="read"/> needs the non-null assertion.</summary>
    public static bool NeedsAssertion(SemanticModel model, ExpressionSyntax read) =>
        read is IdentifierNameSyntax or MemberAccessExpressionSyntax
        && UsedAsNonNull(model, read)
        && IsNullableReferenceProvedNonNull(model, read)
        && !IsNarrowedByAssignmentOrTest(model, read);

    private static bool UsedAsNonNull(SemanticModel model, ExpressionSyntax read) =>
        read.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression == read,
            ElementAccessExpressionSyntax element => element.Expression == read,
            ArgumentSyntax argument => IsNonNullableParameterOf(model, argument),
            _ => false,
        };

    private static bool IsNonNullableParameterOf(SemanticModel model, ArgumentSyntax argument) =>
        model.GetOperation(argument)
            is Microsoft.CodeAnalysis.Operations.IArgumentOperation
            {
                Parameter.Type:
                { IsReferenceType: true, NullableAnnotation: NullableAnnotation.NotAnnotated },
                Parameter.RefKind: RefKind.None,
            };

    private static bool IsNullableReferenceProvedNonNull(SemanticModel model, ExpressionSyntax read)
    {
        var info = model.GetTypeInfo(read);

        return info.Type is { IsReferenceType: true }
            && info.Nullability.FlowState == NullableFlowState.NotNull
            && DeclaredType(model.GetSymbolInfo(read).Symbol)?.NullableAnnotation
                == NullableAnnotation.Annotated;
    }

    private static ITypeSymbol? DeclaredType(ISymbol? symbol) =>
        symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol { IsIndexer: false } property => property.Type,
            _ => null,
        };

    /// <summary>
    /// Whether the last event of the read's variable before it — an
    /// assignment, a null test, a null-forgiving <c>!</c> or a pass to a
    /// method — is an assignment or a null test outside an argument list. A
    /// local with no such event got its state from its initializer, which
    /// narrows too. An assignment whose value holds the read
    /// (<c>stack = stack.Prev</c>) happens after it.
    /// </summary>
    private static bool IsNarrowedByAssignmentOrTest(SemanticModel model, ExpressionSyntax read)
    {
        var symbol = model.GetSymbolInfo(read).Symbol;

        var lastEvent = BodyOf(read)
            ?.DescendantNodes()
            .OfType<ExpressionSyntax>()
            .Where(other =>
                other.Span.End <= read.SpanStart
                && SyntaxFactory.AreEquivalent(other, read)
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(other).Symbol, symbol)
            )
            .Select(other => EventOf(model, other, read))
            .LastOrDefault(kind => kind != ReadEvent.PlainRead);

        return lastEvent switch
        {
            ReadEvent.Narrowing => true,
            ReadEvent.Forgiving => false,
            _ => symbol is ILocalSymbol,
        };
    }

    private static SyntaxNode? BodyOf(SyntaxNode read) =>
        read.Ancestors()
            .FirstOrDefault(a =>
                a
                    is BaseMethodDeclarationSyntax
                        or AccessorDeclarationSyntax
                        or LocalFunctionStatementSyntax
                        or PropertyDeclarationSyntax
            );

    private static ReadEvent EventOf(
        SemanticModel model,
        ExpressionSyntax occurrence,
        ExpressionSyntax read
    ) =>
        occurrence.Parent switch
        {
            PostfixUnaryExpressionSyntax forgiven
                when forgiven.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                ReadEvent.Forgiving,
            AssignmentExpressionSyntax assign
                when assign.Left == occurrence && !assign.Span.Contains(read.Span) =>
                ReadEvent.Narrowing,
            BinaryExpressionSyntax test
                when test.IsKind(SyntaxKind.EqualsExpression)
                    || test.IsKind(SyntaxKind.NotEqualsExpression) => TestEvent(test),
            IsPatternExpressionSyntax test => TestEvent(test),
            ArgumentSyntax argument when ProvesNonNull(model, argument) => ReadEvent.Forgiving,
            _ => ReadEvent.PlainRead,
        };

    /// <summary>
    /// Whether passing the variable proves it non-null afterwards: the
    /// parameter carries <c>[NotNull]</c> (<c>ThrowIfNull</c>) or
    /// <c>[NotNullWhen]</c> (<c>string.IsNullOrEmpty</c>). Any other pass
    /// leaves the state as it was.
    /// </summary>
    private static bool ProvesNonNull(SemanticModel model, ArgumentSyntax argument) =>
        model.GetOperation(argument)
            is Microsoft.CodeAnalysis.Operations.IArgumentOperation { Parameter: { } parameter }
        && parameter
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.ToDisplayString()
                    is "System.Diagnostics.CodeAnalysis.NotNullAttribute"
                        or "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute"
            );

    /// <summary>
    /// A null test narrows where a checker sees it as a condition; inside an
    /// argument list (<c>Debug.Assert(x != null)</c>) only the callee's
    /// nullable attributes make it a proof.
    /// </summary>
    private static ReadEvent TestEvent(ExpressionSyntax test) =>
        test.Ancestors().TakeWhile(a => a is not StatementSyntax).Any(a => a is ArgumentSyntax)
            ? ReadEvent.Forgiving
            : ReadEvent.Narrowing;

    private enum ReadEvent
    {
        PlainRead,
        Narrowing,
        Forgiving,
    }
}
