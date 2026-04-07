using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Transforms a C# exception type (anything that ultimately inherits from
/// <c>System.Exception</c>) into a TypeScript class that extends the JavaScript
/// <c>Error</c> built-in (or another transpilable exception base).
///
/// The constructor signature mirrors the C# constructor with the most parameters and
/// the body emits a single <c>super(...)</c> call. Base-call arguments are resolved
/// from the primary constructor base initializer when present, falling back to passing
/// the first ctor parameter through to <c>super</c>.
/// </summary>
public sealed class ExceptionTransformer(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    public void Transform(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        // Find the primary constructor or the constructor that calls base(message)
        var ctor = type.Constructors
            .Where(c => !c.IsImplicitlyDeclared && c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        var ctorParams = new List<TsConstructorParam>();
        var superArgs = new List<TsExpression>();

        if (ctor is not null)
        {
            foreach (var p in ctor.Parameters)
            {
                ctorParams.Add(new TsConstructorParam(
                    TypeScriptNaming.ToCamelCase(p.Name),
                    TypeMapper.Map(p.Type),
                    Accessibility: TsAccessibility.None
                ));
            }

            // Try to find the base constructor argument (the message)
            var syntax = ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

            // For primary constructors with base initializer: class Foo(args) : Exception(expr)
            if (syntax is ClassDeclarationSyntax classDecl && classDecl.BaseList is not null)
            {
                foreach (var baseType in classDecl.BaseList.Types)
                {
                    if (baseType is PrimaryConstructorBaseTypeSyntax primaryBase)
                    {
                        var semanticModel = _context.Compilation.GetSemanticModel(primaryBase.SyntaxTree);
                        var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
                        foreach (var arg in primaryBase.ArgumentList.Arguments)
                        {
                            superArgs.Add(exprTransformer.TransformExpression(arg.Expression));
                        }
                    }
                }
            }
        }

        // If we couldn't resolve the super args, just pass all ctor params
        if (superArgs.Count == 0 && ctorParams.Count > 0)
        {
            superArgs.Add(new TsIdentifier(ctorParams[0].Name));
        }

        // Build constructor body: super(message)
        var ctorBody = new List<TsStatement>
        {
            new TsExpressionStatement(
                new TsCallExpression(new TsIdentifier("super"), superArgs)
            )
        };

        var constructor = new TsConstructor(ctorParams, ctorBody);

        // Determine the base class in TS
        TsType extendsType = new TsNamedType("Error");
        if (type.BaseType is not null && TypeTransformer.IsExceptionType(type.BaseType)
            && type.BaseType.ToDisplayString() != "System.Exception"
            && SymbolHelper.IsTranspilable(type.BaseType, _context.AssemblyWideTranspile, _context.CurrentAssembly))
        {
            extendsType = TypeMapper.Map(type.BaseType);
        }

        statements.Add(new TsClass(type.Name, constructor, [], Extends: extendsType));
    }
}
