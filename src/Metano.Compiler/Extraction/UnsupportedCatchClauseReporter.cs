using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports the catch clauses of a <c>try</c> statement that a target cannot
/// lower faithfully, each at the clause it concerns:
/// <list type="bullet">
///   <item>MS0001 for an exception filter that declares a variable (a pattern
///   designator), whose scope spans the filter and the clause body, or passes
///   an <c>out</c> argument — every target; on Dart, any exception filter,
///   since the Dart printer keeps its native <c>on T catch</c> clauses, which
///   have no filter;</item>
///   <item>MS0045 for a clause whose exception type the target gives no
///   runtime class (a BCL exception outside its mapping, spec 011 FR-014; see
///   <see cref="ExceptionRuntimeClassReporter"/>): its test would take every
///   exception;</item>
///   <item>MS0001 for a clause whose type has no runtime type test in a
///   target that erases static types (a type parameter).</item>
/// </list>
/// Dart reifies types and tests every exception type natively, so the two
/// type rules apply to the TypeScript target (the type-parameter rule also to
/// an extraction that names no target).
/// </summary>
internal sealed class UnsupportedCatchClauseReporter(
    SemanticModel semantic,
    IrExtractionContext context
)
{
    /// <summary>The <see cref="IrUnsupportedExpression.Kind"/> of a filter that binds variables.</summary>
    public const string FilterVariablesKind = "CatchFilterVariable";

    private readonly SemanticModel _semantic = semantic;
    private readonly ExceptionRuntimeClassReporter _exceptionClassReporter = new(context);

    // TODO(#549): these raw target checks become target capabilities.
    private readonly bool _erasesStaticTypes = context.Target is null or TargetLanguage.TypeScript;
    private readonly bool _lowersFilters = context.Target is not TargetLanguage.Dart;

    /// <summary>
    /// Reports the clauses of <paramref name="statement"/> whose filter or type
    /// test the target cannot express; <paramref name="clauses"/> are its
    /// extracted clauses, in the same order. The clauses after the first one
    /// that handles every exception are never tried, so they report nothing.
    /// </summary>
    public void ReportUnsupportedClauses(
        TryStatementSyntax statement,
        IReadOnlyList<IrCatchClause> clauses
    )
    {
        foreach (var (declared, clause) in ReachableClauses(statement.Catches.Zip(clauses)))
        {
            if (declared.Filter is { } filter)
                ReportUnsupportedFilter(filter, clause);

            ReportUnsupportedType(declared, clause);
        }
    }

    /// <summary>
    /// Reports <paramref name="filter"/> when the target cannot lower it:
    /// any filter on a target that keeps none, otherwise one extracted as the
    /// unsupported placeholder (it declares a variable or passes an
    /// <c>out</c> argument).
    /// </summary>
    private void ReportUnsupportedFilter(CatchFilterClauseSyntax filter, IrCatchClause clause)
    {
        if (!_lowersFilters)
            ReportDroppedFilter(filter);
        else if (clause.Filter is IrUnsupportedExpression { Kind: FilterVariablesKind })
            ReportFilterVariables(filter);
    }

    private void ReportUnsupportedType(CatchClauseSyntax declared, IrCatchClause clause)
    {
        var caught = CaughtSymbolOf(declared);

        if (caught is not null && _exceptionClassReporter.LacksRuntimeClass(caught))
            _exceptionClassReporter.ReportUnanswerableTest(caught, ClauseHead(declared));
        else if (_erasesStaticTypes && !HasRuntimeTypeTest(clause))
            ReportUntestableType(declared, clause);
    }

    /// <summary>
    /// The clauses C# can reach: the first unconditional one takes every
    /// exception, so the ones after it are never tried.
    /// </summary>
    private static IEnumerable<(CatchClauseSyntax Declared, IrCatchClause Clause)> ReachableClauses(
        IEnumerable<(CatchClauseSyntax Declared, IrCatchClause Clause)> clauses
    )
    {
        foreach (var reachable in clauses)
        {
            yield return reachable;

            if (reachable.Clause.IsUnconditional)
                yield break;
        }
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

    private static void ReportUntestableType(CatchClauseSyntax declared, IrCatchClause clause) =>
        Report(
            ClauseHead(declared),
            $"Catch clause '{ClauseHead(declared)}' cannot be lowered: its exception type has no "
                + "runtime type test in the target (type parameters are erased), so the clause "
                + (
                    clause.Filter is null
                        ? "takes every exception."
                        : "takes every exception its filter accepts."
                )
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
}
