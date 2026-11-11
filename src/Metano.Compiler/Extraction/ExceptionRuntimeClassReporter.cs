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
///   <item>A construction reports <c>MS0001</c> (a warning): the target throws
///   its <c>Exception</c> with the type name in the message.</item>
///   <item>A type test — <c>is</c>, <c>as</c>, a type pattern, a <c>catch</c>
///   clause — reports <c>MS0045</c> (an error): widened to <c>Exception</c>,
///   it would take every exception.</item>
/// </list>
/// </summary>
internal sealed class ExceptionRuntimeClassReporter(IrExtractionContext context)
{
    private readonly IrExtractionContext _context = context;

    /// <summary>
    /// Whether <paramref name="type"/> is an exception with no declaration on
    /// the target side: a BCL exception, or one from another non-transpilable
    /// assembly. Its constructions are classified by argument role
    /// (<see cref="IrExceptionConstructionExtractor"/>) and lowered by the
    /// target from them.
    /// </summary>
    public static bool LacksTargetDeclaration(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) => IrTypeRefMapper.IsExceptionType(type) && !IrTypeRefMapper.IsTranspilableType(type, target);

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
                + "class in the target: it is emitted as 'Exception' with the type name in its "
                + "message, so catch clauses and type tests cannot tell it apart (spec 011 FR-014).",
            construction
        );
    }

    /// <summary>
    /// Reports <c>MS0045</c> at <paramref name="test"/> when the target has no
    /// runtime class for <paramref name="testedType"/>, and returns whether it
    /// did, so the caller lowers no test.
    /// </summary>
    public bool ReportsUnanswerableTest(ITypeSymbol? testedType, SyntaxNode test)
    {
        if (!LacksRuntimeClass(testedType))
            return false;

        Report(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnanswerableRuntimeTypeTest,
            $"'{test}' cannot be lowered: '{testedType!.ToDisplayString()}' has no runtime class in "
                + "the target, so no type test can tell it apart from other exceptions, and testing "
                + "for 'Exception' instead would take them all (spec 011 FR-014). Catch or test a "
                + "base type the runtime defines, or a transpiled exception of your own.",
            test
        );

        return true;
    }

    private bool LacksRuntimeClass(ITypeSymbol? type) =>
        _context.HasExceptionRuntimeClass is { } hasRuntimeClass
        && type is INamedTypeSymbol named
        && LacksTargetDeclaration(named, _context.Target)
        && !hasRuntimeClass(named);

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
