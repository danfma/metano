using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports MS0001 for the catch clauses of a <c>try</c> statement that a
/// target cannot lower faithfully, each at the clause it concerns:
/// <list type="bullet">
///   <item>an exception filter that declares a variable (a pattern
///   designator), whose scope spans the filter and the clause body, or passes
///   an <c>out</c> argument — every target; on Dart, any exception filter,
///   since the Dart printer keeps its native <c>on T catch</c> clauses, which
///   have no filter;</item>
///   <item>a clause whose type has no runtime type test in a target that
///   erases static types (a type parameter);</item>
///   <item>a clause whose type shares its runtime class with other exception
///   types in such a target, followed by a clause for an exception type that
///   is neither the same nor derived from it: the earlier clause's test would
///   also take the exceptions the later clause handles.</item>
/// </list>
/// Dart reifies types and tests every exception type natively, so the two
/// type rules apply to the TypeScript target, and to an extraction that
/// names no target.
/// </summary>
internal sealed class UnsupportedCatchClauseReporter(
    SemanticModel semantic,
    IrExtractionContext context
)
{
    private readonly SemanticModel _semantic = semantic;

    // TODO(#549): these raw target checks become target capabilities; #651
    // (real runtime classes for the BCL exceptions) replaces
    // SharesItsRuntimeClass by a target port.
    private readonly bool _erasesStaticTypes = context.Target is null or TargetLanguage.TypeScript;
    private readonly bool _lowersFilters = context.Target is not TargetLanguage.Dart;

    /// <summary>
    /// Reports the clauses of <paramref name="statement"/> whose type test the
    /// target cannot express; <paramref name="clauses"/> are its extracted
    /// clauses, in the same order.
    /// </summary>
    public void ReportUnsupportedClauses(
        TryStatementSyntax statement,
        IReadOnlyList<IrCatchClause> clauses
    )
    {
        if (!_erasesStaticTypes)
            return;

        foreach (var (index, (declared, clause)) in statement.Catches.Zip(clauses).Index())
        {
            if (!HasRuntimeTypeTest(clause))
                ReportUntestableType(declared);
            else if (TakesLaterExceptions(declared, clause, statement.Catches.Skip(index + 1)))
                ReportSharedRuntimeClass(declared);
        }
    }

    /// <summary>
    /// Reports <paramref name="filter"/> when the target cannot lower it:
    /// any filter on a target that keeps none, otherwise one that
    /// <paramref name="bindsVariables"/> (declares a variable or passes an
    /// <c>out</c> argument).
    /// </summary>
    public void ReportUnsupportedFilter(CatchFilterClauseSyntax filter, bool bindsVariables)
    {
        if (!_lowersFilters)
            ReportDroppedFilter(filter);
        else if (bindsVariables)
            ReportFilterVariables(filter);
    }

    /// <summary>
    /// Whether <paramref name="clause"/> shares its runtime class and one of
    /// the clauses after it (<paramref name="later"/>) is for an exception
    /// type that is neither its own nor derived from it.
    /// </summary>
    private bool TakesLaterExceptions(
        CatchClauseSyntax declared,
        IrCatchClause clause,
        IEnumerable<CatchClauseSyntax> later
    )
    {
        if (!SharesItsRuntimeClass(clause))
            return false;

        var own = CaughtSymbolOf(declared);

        return later.Any(other => !IsSameOrDerived(CaughtSymbolOf(other), own));
    }

    private ITypeSymbol? CaughtSymbolOf(CatchClauseSyntax clause) =>
        clause.Declaration is { } declaration ? _semantic.GetTypeInfo(declaration.Type).Type : null;

    private static void ReportDroppedFilter(CatchFilterClauseSyntax filter) =>
        Report(
            filter,
            $"Exception filter '{filter}' cannot be lowered: exception filters are not lowered "
                + "for Dart; the clause would run unconditionally."
        );

    private static void ReportFilterVariables(CatchFilterClauseSyntax filter) =>
        Report(
            filter,
            $"Exception filter '{filter}' cannot be lowered: a filter that declares a variable "
                + "(a pattern designator) or passes an 'out' argument is not supported. Call a "
                + "method that does it from the filter instead."
        );

    private static void ReportUntestableType(CatchClauseSyntax clause) =>
        Report(
            ClauseHead(clause),
            $"Catch clause '{ClauseHead(clause)}' cannot be lowered: its exception type has no "
                + "runtime type test in the target (type parameters are erased), so the clause "
                + "takes every exception."
        );

    private static void ReportSharedRuntimeClass(CatchClauseSyntax clause) =>
        Report(
            ClauseHead(clause),
            $"Catch clause '{ClauseHead(clause)}' cannot be lowered ahead of a clause for "
                + "another exception type: its exception type is not transpiled, so the target "
                + "has no class of its own to test (generated code constructs every BCL "
                + "exception as a plain 'Error') and the clause tests 'instanceof Error', which "
                + "also takes the exceptions the later clauses handle. Telling such types apart "
                + "is tracked in #651."
        );

    private static void Report(SyntaxNode node, string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                node.GetLocation()
            )
        );

    /// <summary>The <c>(Type name)</c> declaration of a clause, or the clause itself.</summary>
    private static SyntaxNode ClauseHead(CatchClauseSyntax clause) =>
        clause.Declaration is { } declaration ? declaration : clause;

    private static bool HasRuntimeTypeTest(IrCatchClause clause) =>
        clause.ExceptionType is not { } type || IrRuntimeTypeTest.ExistsFor(type);

    /// <summary>
    /// Whether the clause's type is an exception without a class of its own in
    /// a type-erasing target: an exception type that is not transpiled (a BCL
    /// one, mostly) other than <c>System.Exception</c>, whose values the
    /// target can only test against the one error class.
    /// </summary>
    private static bool SharesItsRuntimeClass(IrCatchClause clause) =>
        clause
            is {
                CatchesEveryException: false,
                ExceptionType: IrNamedTypeRef
                {
                    Semantics: { Kind: IrNamedTypeKind.Exception, IsTranspilable: false }
                }
            };

    private static bool IsSameOrDerived(ITypeSymbol? type, ITypeSymbol? baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }
}
