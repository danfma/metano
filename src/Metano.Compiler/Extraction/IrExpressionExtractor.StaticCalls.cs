using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

public sealed partial class IrExpressionExtractor
{
    private IrBoundStaticCall? BoundStaticCallOf(IMethodSymbol? method, bool isLifted = false)
    {
        if (
            method is not { IsStatic: true, ContainingType: { } owner }
            || method.ReducedFrom is not null
            || IrStaticConstructorExtractor.DeclaredBy(owner) is null
            || IrTypeRefMapper.Map(owner, _originResolver, _target) is not IrNamedTypeRef type
        )
            return null;

        var hasExtensions =
            owner.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod)
            || owner.DeclaringSyntaxReferences.Any(reference =>
                reference
                    .GetSyntax()
                    .DescendantNodes()
                    .Any(node => node.Kind().ToString() == "ExtensionBlockDeclaration")
            );

        return new IrBoundStaticCall(
            TypeReferenceTo(owner),
            type,
            SymbolHelper.IsContainerless(owner),
            hasExtensions,
            isLifted
        );
    }

    private IrExpression ExtractInvocationCore(InvocationExpressionSyntax invocation)
    {
        var expression = ExtractInvocationUnannotatedCore(invocation);
        var method = _semantic.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var boundCall = BoundStaticCallOf(method);

        return boundCall is null || expression.BoundStaticCall is not null
            ? expression
            : expression with
            {
                BoundStaticCall = boundCall,
            };
    }
}
