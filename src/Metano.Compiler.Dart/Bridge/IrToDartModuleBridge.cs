using Metano.Compiler.IR;
using Metano.Dart.AST;

namespace Metano.Dart.Bridge;

/// <summary>
/// Lowers <see cref="IrModuleFunction"/>s into top-level <see cref="DartFunction"/>
/// declarations. Called for C# static classes decorated with
/// <c>[ExportedAsModule]</c>: rather than a Dart class of static methods, the
/// consumer gets a flat file of importable functions, which is the idiomatic
/// Dart shape for a utility module.
/// </summary>
public static class IrToDartModuleBridge
{
    public static void Convert(
        IReadOnlyList<IrModuleFunction> functions,
        List<DartTopLevel> statements
    )
    {
        foreach (var fn in functions)
            statements.Add(ConvertFunction(fn));
    }

    private static DartFunction ConvertFunction(IrModuleFunction fn) =>
        new(
            Name: IrToDartNamingPolicy.ToParameterName(fn.Name),
            Parameters: fn.Parameters.Select(p => new DartParameter(
                    IrToDartNamingPolicy.ToParameterName(p.Name),
                    IrToDartTypeMapper.Map(p.Type),
                    // Forward the parsed default expression so Dart keeps the
                    // parameter optional and renders the default inside the
                    // optional positional block.
                    DefaultValue: p.DefaultValue
                ))
                .ToList(),
            ReturnType: IrToDartTypeMapper.Map(fn.ReturnType),
            Body: fn.Body,
            IsAsync: fn.Semantics.IsAsync
        );
}
