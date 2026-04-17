using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Lowers <see cref="IrModuleFunction"/>s into top-level TypeScript
/// <see cref="TsFunctionDeclaration"/>s — the shape a
/// <c>[ExportedAsModule]</c> static class emits (plain functions instead of
/// a class full of statics) — and also renders classic C# extension
/// methods, which Roslyn already exposes as ordinary methods whose first
/// parameter is the receiver. C# 14 <c>extension(R r)</c> blocks and
/// classic-style extension properties also flow through this bridge: the
/// extractor folds them into the same <see cref="IrModuleFunction"/> shape
/// (with the receiver as the first parameter). <c>[ModuleEntryPoint]</c>
/// bodies are emitted separately as <see cref="TsTopLevelStatement"/>s by
/// the caller in <c>TypeTransformer.TryEmitModuleViaIr</c>.
/// </summary>
public static class IrToTsModuleBridge
{
    /// <summary>
    /// Appends a <see cref="TsFunctionDeclaration"/> per
    /// <see cref="IrModuleFunction"/> to <paramref name="statements"/>.
    /// Bodies are routed through <see cref="IrToTsStatementBridge"/> so BCL
    /// mappings and the implicit-<c>this</c> synthesis the extractor already
    /// applied for instance members carry through.
    /// </summary>
    public static void Convert(
        IReadOnlyList<IrModuleFunction> functions,
        List<TsTopLevel> statements,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        foreach (var fn in functions)
            statements.Add(ConvertFunction(fn, bclRegistry));
    }

    private static TsFunction ConvertFunction(
        IrModuleFunction fn,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var parameters = fn
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                IrToTsTypeMapper.Map(p.Type)
            ))
            .ToList();
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(fn.Body, fn.Name, bclRegistry);

        // [Name] overrides (including the TypeScript-specific form) decide the
        // emitted function name; IrToTsNamingPolicy encapsulates the
        // target-aware lookup.
        var name = IrToTsNamingPolicy.ToFunctionName(fn.Name, fn.Attributes);

        return new TsFunction(
            name,
            parameters,
            IrToTsTypeMapper.Map(fn.ReturnType),
            body,
            Exported: true,
            // Generators can't also be async under this backend; the extractor
            // already forced Async off when IsGenerator is on.
            Async: fn.Semantics.IsAsync,
            Generator: fn.Semantics.IsGenerator,
            TypeParameters: MapTypeParameters(fn.TypeParameters)
        );
    }

    private static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;
        return typeParameters
            .Select(tp => new TsTypeParameter(
                tp.Name,
                tp.Constraints is { Count: > 0 } c ? IrToTsTypeMapper.Map(c[0]) : null
            ))
            .ToList();
    }
}
