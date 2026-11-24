using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <c>params</c> shape of a <c>: this(...)</c> constructor chain, whose
/// written arguments the chain keeps one by one (#379).
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The indices of the written arguments of <paramref name="thisCall"/> that
    /// C# packs into the target's <c>params</c> array in expanded form, in
    /// element order (empty for <c>: this()</c>); <c>null</c> when the chain
    /// passes the array itself, the target has no <c>params</c> array, or an
    /// element maps to no written argument.
    /// </summary>
    internal IReadOnlyList<int>? ExpandedParamsArgumentsOf(ConstructorInitializerSyntax thisCall)
    {
        var paramArray = BoundArgumentsOf(_semantic.GetOperation(thisCall))
            ?.FirstOrDefault(argument => argument.ArgumentKind is ArgumentKind.ParamArray);

        if (paramArray?.Value is not IArrayCreationOperation creation)
            return null;

        var written = thisCall.ArgumentList.Arguments;
        var elements = creation.Initializer?.ElementValues ?? [];

        var indices = elements
            .Select(element =>
                written.IndexOf(argument => argument.Expression == ElementSyntaxOf(element))
            )
            .ToList();

        // An element that maps to no written argument leaves the chain as it
        // is: the target binds the arguments one by one, as before.
        return indices.Contains(-1) ? null : indices;
    }
}
