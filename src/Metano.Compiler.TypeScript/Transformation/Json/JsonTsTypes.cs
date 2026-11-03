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
}
