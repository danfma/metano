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
///   an <c>out</c> argument — every target;</item>
///   <item>a clause whose type has no runtime type test in a target that
///   erases static types (a type parameter);</item>
///   <item>a clause whose type shares its runtime class with other exception
///   types in such a target, followed by a clause for an exception type that
///   is neither the same nor derived from it: the earlier clause's test would
///   also take the exceptions the later clause handles.</item>
/// </list>
/// Dart reifies types and tests every exception type natively, so the two
/// type rules apply to the TypeScript target only.
/// </summary>
internal sealed class UnsupportedCatchClauseReporter(
    SemanticModel semantic,
    IrExtractionContext context
)
{
    private readonly SemanticModel _semantic = semantic;
    private readonly bool _erasesStaticTypes = context.Target is null or TargetLanguage.TypeScript;

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

        for (var index = 0; index < clauses.Count; index++)
        {
            var declared = statement.Catches[index];

            if (!HasRuntimeTypeTest(clauses[index]))
                ReportUntestableType(declared);
            else if (TakesLaterExceptions(clauses[index], statement.Catches.Skip(index)))
                ReportSharedRuntimeClass(declared);
        }
    }

    /// <summary>
    /// Whether a clause that shares its runtime class is followed by a clause
    /// for another exception type; <paramref name="fromClause"/> starts at the
    /// clause's own syntax.
    /// </summary>
    private bool TakesLaterExceptions(
        IrCatchClause clause,
        IEnumerable<CatchClauseSyntax> fromClause
    )
    {
        if (!SharesItsRuntimeClass(clause))
            return false;

        var caught = fromClause.Select(CaughtTypeOf).ToList();

        return caught.Skip(1).Any(later => !IsSameOrDerived(later, caught[0]));
    }

    private ITypeSymbol? CaughtTypeOf(CatchClauseSyntax clause) =>
        clause.Declaration is { } declaration ? _semantic.GetTypeInfo(declaration.Type).Type : null;

    /// <summary>
    /// Reports a filter that declares a variable or passes an <c>out</c>
    /// argument: the variable would have to be declared where both the filter
    /// and the clause body see it, and an <c>out</c> result bound ahead of the
    /// filter, neither of which a target's catch lowering provides.
    /// </summary>
    public static void ReportFilterVariables(CatchFilterClauseSyntax filter) =>
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
                + "runtime type test in the target (type parameters are erased)."
        );

    private static void ReportSharedRuntimeClass(CatchClauseSyntax clause) =>
        Report(
            ClauseHead(clause),
            $"Catch clause '{ClauseHead(clause)}' cannot be lowered ahead of a clause for "
                + "another exception type: a BCL exception type has no class of its own in the "
                + "target (every BCL exception is a JavaScript 'Error'), so its type test would "
                + "also take the exceptions the later clauses handle. Catch a transpiled "
                + "exception type, or make it the last clause."
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
        clause.ExceptionType is not { } type
        || IrRuntimeTypeTest.Of(type) is not IrRuntimeTypeTest.None;

    /// <summary>
    /// Whether the clause's type is an exception without a class of its own in
    /// a type-erasing target: a BCL (or otherwise non-transpiled) exception
    /// other than <c>System.Exception</c>, whose values are all instances of
    /// the one runtime error class.
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
