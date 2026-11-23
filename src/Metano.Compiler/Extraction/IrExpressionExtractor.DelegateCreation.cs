using Metano.Compiler.IR;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Explicit delegate creation, <c>new Func&lt;int, string&gt;(Format)</c>
/// (#634): C# converts its one argument — a method group, a lambda or another
/// delegate — to the delegate type, exactly as the implicit conversion
/// <c>Func&lt;int, string&gt; f = Format</c> does, so it lowers as that
/// argument. A delegate has no constructor a target could call.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrExpression? TryExtractDelegateCreation(BaseObjectCreationExpressionSyntax creation)
    {
        if (_semantic.GetOperation(creation) is not IDelegateCreationOperation)
            return null;

        return creation.ArgumentList?.Arguments is [var argument]
            ? Extract(argument.Expression)
            : null;
    }
}
