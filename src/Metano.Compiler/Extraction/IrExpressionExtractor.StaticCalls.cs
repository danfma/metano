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

        return new IrBoundStaticCall(
            TypeReferenceTo(owner),
            type,
            SymbolHelper.IsContainerless(owner),
            owner.HasExtensionMembers(),
            isLifted
        );
    }

    private IrExpression ExtractInvocationCore(InvocationExpressionSyntax invocation)
    {
        var method = _semantic.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        var expression = WithSelfType(
            ExtractInvocationUnannotatedCore(invocation),
            method,
            invocation
        );

        var boundCall = BoundStaticCallOf(method);

        return boundCall is null || expression.BoundStaticCall is not null
            ? expression
            : expression with
            {
                BoundStaticCall = boundCall,
            };
    }

    /// <summary>
    /// A call to a self-typed static, or to a referenced package's CRTP static, carries
    /// the class it is emitted on (spec 001 FR-113 item 3): the type it binds to the
    /// callee class's self parameter, or the caller's receiver when a self-typed static
    /// forwards its own. A call that binds an open type parameter otherwise (MS0054) is
    /// left as written.
    /// </summary>
    private IrExpression WithSelfType(
        IrExpression expression,
        IMethodSymbol? method,
        InvocationExpressionSyntax invocation
    )
    {
        if (
            expression is not IrCallExpression call
            || method is null
            || _context.ReifiedTypeAnalysis.SelfTyped.ArgumentOf(method) is not { } argument
        )
            return expression;

        var mapped = MapTypeWithReifiedScope(argument, invocation);

        return argument switch
        {
            INamedTypeSymbol named => call with
            {
                SelfType = SelfTypeBinding(
                    method,
                    mapped,
                    TypeReferenceTo(named.OriginalDefinition),
                    invocation
                ),
            },
            ITypeParameterSymbol
                when mapped is IrTypeParameterRef { Scope: IrTypeParameterScope.Receiver } =>
                call with
                {
                    SelfType = SelfTypeBinding(method, mapped, null, invocation),
                },
            _ => expression,
        };
    }

    private IrSelfTypeBinding SelfTypeBinding(
        IMethodSymbol method,
        IrTypeRef argument,
        IrTypeReference? selfClass,
        InvocationExpressionSyntax invocation
    )
    {
        var definition = method.OriginalDefinition;
        var classDefinition = definition.ContainingType;

        var classTypeParameters = IrTypeParameterExtractor.ExtractAll(
            classDefinition.TypeParameters,
            _originResolver,
            _target
        ) ?? [];

        var classTypeArguments = classTypeParameters
            .Select(
                (parameter, index) =>
                    new IrClassTypeArgument(
                        parameter,
                        MapTypeWithReifiedScope(
                            method.ContainingType.TypeArguments[index],
                            invocation
                        )
                    )
            )
            .ToList();

        var parameters = definition
            .Parameters.Select(parameter =>
                IrTypeRefMapper.Map(parameter.Type, _originResolver, _target)
            )
            .ToList();

        return new IrSelfTypeBinding(
            argument,
            selfClass,
            Analysis.CrtpSelfParameter.Of(classDefinition)!.Name,
            classTypeArguments,
            parameters,
            IrTypeRefMapper.Map(definition.ReturnType, _originResolver, _target)
        );
    }
}
