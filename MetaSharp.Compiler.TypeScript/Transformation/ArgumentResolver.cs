using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Resolves a C# <see cref="ArgumentListSyntax"/> into a positional list of
/// <see cref="TsExpression"/>s, handling named-argument reordering and skipped
/// parameters with explicit default values.
///
/// Algorithm:
/// <list type="number">
///   <item>If no argument is named, return the transformed arguments in source order.</item>
///   <item>Otherwise resolve the call site's <see cref="IMethodSymbol"/> to get the
///   parameter list, fill the result slots with each parameter's
///   <see cref="IParameterSymbol.ExplicitDefaultValue"/> (or <c>undefined</c>), then
///   place positional arguments in source order and named arguments by parameter name.</item>
///   <item>Trim trailing slots that match the default values so the call site doesn't emit
///   unnecessary <c>undefined</c> arguments past the last explicitly provided one.</item>
/// </list>
///
/// Used by <see cref="ObjectCreationHandler"/> for <c>new Type(named: arg, …)</c>
/// expressions where C# allows skipping intermediate parameters that the equivalent
/// JavaScript constructor signature does not.
/// </summary>
public sealed class ArgumentResolver(ExpressionTransformer parent)
{
    private readonly ExpressionTransformer _parent = parent;

    public List<TsExpression> Resolve(ArgumentListSyntax? argumentList, ExpressionSyntax callSite)
    {
        if (argumentList is null || argumentList.Arguments.Count == 0)
            return [];

        // Check if any argument is named
        var hasNamedArgs = argumentList.Arguments.Any(a => a.NameColon is not null);
        if (!hasNamedArgs)
        {
            // All positional — simple case
            return argumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression)).ToList();
        }

        // Resolve the constructor/method symbol to get parameter order
        var symbolInfo = _parent.Model.GetSymbolInfo(callSite);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            // Fallback: just transform as-is
            return argumentList.Arguments.Select(a => _parent.TransformExpression(a.Expression)).ToList();
        }

        var parameters = methodSymbol.Parameters;
        var result = new TsExpression[parameters.Length];

        // Fill defaults
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].HasExplicitDefaultValue)
            {
                result[i] = parameters[i].ExplicitDefaultValue switch
                {
                    null => new TsLiteral("null"),
                    bool b => new TsLiteral(b ? "true" : "false"),
                    string s => new TsStringLiteral(s),
                    int n => new TsLiteral(n.ToString()),
                    _ => new TsLiteral(parameters[i].ExplicitDefaultValue?.ToString() ?? "undefined")
                };
            }
            else
            {
                result[i] = new TsIdentifier("undefined");
            }
        }

        // Place positional arguments first
        var positionalIndex = 0;
        foreach (var arg in argumentList.Arguments)
        {
            if (arg.NameColon is not null)
            {
                // Named argument — find the parameter index
                var paramName = arg.NameColon.Name.Identifier.Text;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].Name == paramName)
                    {
                        result[i] = _parent.TransformExpression(arg.Expression);
                        break;
                    }
                }
            }
            else
            {
                // Positional
                result[positionalIndex] = _parent.TransformExpression(arg.Expression);
                positionalIndex++;
            }
        }

        // Find the last index that was explicitly provided so we can trim trailing defaults
        var lastProvided = -1;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (argumentList.Arguments.Any(a =>
                (a.NameColon is not null && a.NameColon.Name.Identifier.Text == parameters[i].Name)
                || (a.NameColon is null && argumentList.Arguments.IndexOf(a) == i)))
            {
                lastProvided = i;
            }
        }

        return result.Take(lastProvided + 1).ToList();
    }
}
