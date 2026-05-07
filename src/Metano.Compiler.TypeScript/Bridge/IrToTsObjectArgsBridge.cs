using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers <c>[ObjectArgs]</c>-annotated parameter lists into the synthetic
/// <c>args: { p1: T1; p2?: T2 }</c> shape plus the destructuring header
/// statement. Shared by module-function and class-method emission.
/// </summary>
/// <remarks>
/// Every C# parameter becomes a field on the synthesized object type;
/// optional ones (<see cref="IrParameter.IsOptional"/> or carrying a
/// default value) render with the <c>?:</c> suffix so callers may omit
/// them. The destructure header restores the original parameter names so
/// the lowered body keeps working unchanged.
/// </remarks>
internal static class IrToTsObjectArgsBridge
{
    public static bool HasObjectArgs(IReadOnlyList<IrAttribute>? attributes) =>
        attributes is { Count: > 0 } list
        && list.Any(a => a.Name is "ObjectArgsAttribute" or "ObjectArgs");

    public static (TsParameter ArgsParam, TsRawStatement DestructureHeader) BuildArgsParam(
        IReadOnlyList<IrParameter> parameters,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // Structured TsObjectType (vs. a raw-text TsNamedType) keeps nested
        // type references visible to the import collector — e.g. an
        // [ObjectArgs] factory whose field type lives in another file
        // ("children: Widget[]") only emits the Widget import when the
        // collector can recurse into the field's TsType node.
        var fields = new List<TsObjectTypeField>();
        var destructuringFragments = new List<string>();
        foreach (var p in parameters)
        {
            var (field, destructuringFragment) = BuildArgsObjectField(p, bclRegistry);
            fields.Add(field);
            destructuringFragments.Add(destructuringFragment);
        }

        var argsParam = new TsParameter("args", new TsObjectType(fields));
        // TODO: replace with a structured TsDestructuringDeclaration once the
        // AST gains one — rendering the header as raw text is the same defect
        // we just fixed on the parameter-type side.
        var destructure = new TsRawStatement(
            "const { " + string.Join(", ", destructuringFragments) + " } = args;"
        );
        return (argsParam, destructure);
    }

    private static (TsObjectTypeField Field, string DestructuringFragment) BuildArgsObjectField(
        IrParameter parameter,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var name = TypeScriptNaming.ToCamelCase(parameter.Name);
        var type = IrToTsTypeMapper.Map(parameter.Type);
        var isOptional = parameter.IsOptional || parameter.HasDefaultValue;
        var field = new TsObjectTypeField(name, type, isOptional);

        var destructuringFragment =
            parameter.HasDefaultValue && parameter.DefaultValue is not null
                ? $"{name} = {RenderDefaultValue(parameter.DefaultValue, bclRegistry)}"
                : name;

        return (field, destructuringFragment);
    }

    private static string RenderDefaultValue(
        IrExpression defaultValue,
        DeclarativeMappingRegistry? bclRegistry
    ) => Printer.RenderExpression(IrToTsExpressionBridge.Map(defaultValue, bclRegistry)).Trim();
}
