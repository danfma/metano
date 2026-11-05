using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The IR of a parameter's explicit default value, shared by the method and
/// constructor extractors so backends that render defaults (the Dart target,
/// TypeScript optional parameters) can reproduce them.
/// </summary>
internal static class ParameterDefaultValues
{
    /// <summary>
    /// The default of <paramref name="parameter"/>; <c>null</c> when it has
    /// none, when no compilation is supplied (shape-only mode), or when its
    /// declaring syntax isn't reachable (referenced-assembly methods).
    /// </summary>
    public static IrExpression? Extract(IParameterSymbol parameter, IrExtractionContext context)
    {
        var compilation = context.Compilation;

        if (!parameter.HasExplicitDefaultValue || compilation is null)
            return null;

        var syntax =
            parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ParameterSyntax;

        if (syntax?.Default?.Value is not { } defaultExpression)
            return null;

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);

        return new IrExpressionExtractor(model, context.ForConstantDefault()).Extract(
            defaultExpression
        );
    }
}
