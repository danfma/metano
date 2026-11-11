using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports the sites that need a runtime class for an exception type the
/// target gives none (spec 011 FR-014). Whether a type has one is the target's
/// answer, read through <see cref="IrExtractionContext.HasExceptionRuntimeClass"/>;
/// a target that sets no port tests every exception natively, and nothing is
/// reported.
/// <list type="bullet">
///   <item>A construction reports <c>MS0001</c> (a warning): the target
///   cannot keep the type's identity, so it is lost to catch clauses and type
///   tests.</item>
///   <item>A type test — <c>is</c>, <c>as</c>, a type pattern, a <c>catch</c>
///   clause — reports <c>MS0045</c> (an error): widened to <c>Exception</c>,
///   it would take every exception.</item>
/// </list>
/// </summary>
internal sealed class ExceptionRuntimeClassReporter(IrExtractionContext context)
{
    private readonly IrExtractionContext _context = context;

    /// <summary>
    /// Whether <paramref name="type"/> is an exception without a declaration
    /// on the target side for which the target has no runtime class either.
    /// </summary>
    public bool LacksRuntimeClass(ITypeSymbol? type) =>
        _context.HasExceptionRuntimeClass is { } hasRuntimeClass
        && type is INamedTypeSymbol named
        && IrTypeRefMapper.IsExceptionWithoutTargetDeclaration(named, _context.Target)
        && !hasRuntimeClass(named);

    /// <summary>
    /// Reports <c>MS0001</c> at <paramref name="construction"/> when the
    /// target has no runtime class for <paramref name="exceptionType"/>.
    /// </summary>
    public void ReportConstruction(INamedTypeSymbol exceptionType, SyntaxNode construction)
    {
        if (!LacksRuntimeClass(exceptionType))
            return;

        Report(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.UnsupportedFeature,
            $"'{construction}' constructs '{exceptionType.ToDisplayString()}', which has no runtime "
                + "class in the target: the exception loses its type, so catch clauses and type "
                + "tests cannot tell it apart from other exceptions (spec 011 FR-014).",
            construction
        );
    }

    /// <summary>
    /// Reports <c>MS0045</c> at <paramref name="test"/>, a type test against
    /// <paramref name="testedType"/>, for which
    /// <see cref="LacksRuntimeClass"/> holds.
    /// </summary>
    public void ReportUnanswerableTest(ITypeSymbol testedType, SyntaxNode test) =>
        Report(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnanswerableRuntimeTypeTest,
            $"'{test}' cannot be lowered: '{testedType.ToDisplayString()}' has no runtime class in "
                + "the target, so no type test can tell it apart from other exceptions, and testing "
                + "for 'Exception' instead would take them all (spec 011 FR-014). Catch or test a "
                + "base type the runtime defines, or a transpiled exception of your own.",
            test
        );

    private static void Report(
        MetanoDiagnosticSeverity severity,
        string code,
        string message,
        SyntaxNode site
    ) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(severity, code, message, site.GetLocation())
        );
}
