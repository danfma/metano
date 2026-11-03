using System.Collections.Immutable;
using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Maps a C# type to the TS type the generated context names it with, through
/// the same IR mapping (and BCL overrides) every other emitted signature uses.
/// </summary>
internal static class JsonTsTypes
{
    public static TsType Map(TypeScriptTransformContext context, ITypeSymbol symbol) =>
        IrToTsTypeMapper.Map(
            IrTypeRefMapper.Map(symbol, context.OriginResolver, TargetLanguage.TypeScript),
            context.BclOverrides
        );

    /// <summary>
    /// The TS type of an object contract's spec. One contract serves every
    /// nullable-annotation variant of its type arguments (<c>Page&lt;Leaf?&gt;</c>
    /// and <c>Page&lt;Leaf&gt;</c> are one symbol), so the arguments are mapped
    /// without their annotations, keeping <c>TypeSpec&lt;Page&lt;Leaf&gt;&gt;</c>
    /// independent of which variant the closure reached first.
    /// </summary>
    public static TsType MapContract(TypeScriptTransformContext context, INamedTypeSymbol type) =>
        Map(context, WithoutArgumentAnnotations(type));

    private static ITypeSymbol WithoutArgumentAnnotations(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeArguments.Length: > 0 } generic)
            return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        if (generic.ContainingType is { IsGenericType: true })
            return generic;

        var arguments = generic.TypeArguments.Select(WithoutArgumentAnnotations).ToImmutableArray();

        return generic
            .OriginalDefinition.Construct(
                arguments,
                arguments.Select(_ => NullableAnnotation.NotAnnotated).ToImmutableArray()
            )
            .WithNullableAnnotation(generic.NullableAnnotation);
    }
}
