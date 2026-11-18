using Metano.Compiler.Extraction.Invocation;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A method group of a static <c>[Import]</c> facade (<c>Inferno.CreateElement</c>
/// passed as a delegate, or spliced into a <c>Js.Code</c> hole; spec 011 FR-032):
/// the facade's local name, carrying its import the way a call of the facade does
/// (<see cref="EmitOrImportRewriter"/>), so the file that names it also imports it.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrTemplateExpression? ImportFacadeMethodGroupOf(ExpressionSyntax expression)
    {
        if (
            expression is not (SimpleNameSyntax or MemberAccessExpressionSyntax)
            || IsInvokedExpression(expression)
        )
            return null;

        if (
            _semantic.GetSymbolInfo(expression).Symbol
                is not IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary } method
            || method.HasEmit()
            || method.GetImport() is not { } import
            || _semantic.GetOperation(expression) is not IMethodReferenceOperation
        )
            return null;

        var localName = method.ImportFacadeLocalName(_target);

        return new IrTemplateExpression(
            localName,
            Receiver: null,
            Arguments: [],
            ExternalImports: [EmitOrImportRewriter.ToIrExternalImport(import, localName)]
        );
    }
}
