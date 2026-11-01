using System.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

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

    /// <summary>
    /// Appends one top-level variable per module field or auto-property of a
    /// containerless static class (see <see cref="Metano.Compiler.Extraction.IrModuleVariableExtractor"/>):
    /// <c>export const max: number = 10;</c>. Call sites read these members as
    /// bare identifiers, so the declaration uses the same data-member name.
    /// <list type="bullet">
    ///   <item>A <c>readonly</c> / <c>const</c> field or a get-only / init-only
    ///     property is a <c>const</c>; anything writable is a <c>let</c>.</item>
    ///   <item>A member without an initializer starts at the type's C# default
    ///     (<c>0</c>, <c>false</c>, <c>null</c>, …); a reference type with no
    ///     literal default is a bare <c>let</c>.</item>
    ///   <item>A <c>private</c> member stays module-local; any other visibility
    ///     is exported, since another file of the assembly may read it.</item>
    /// </list>
    /// Computed properties are not variables; the caller reports them before
    /// calling this.
    /// </summary>
    public static void ConvertVariables(
        IReadOnlyList<IrMemberDeclaration> variables,
        List<TsTopLevel> statements,
        IrToTsTypeOverrides? typeOverrides = null,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        foreach (var variable in variables)
        {
            var declaration = variable switch
            {
                IrFieldDeclaration field => ConvertVariable(
                    field,
                    field.Type,
                    field.Initializer,
                    field.IsReadonly,
                    typeOverrides,
                    bclRegistry
                ),
                IrPropertyDeclaration property => ConvertVariable(
                    property,
                    property.Type,
                    property.Initializer,
                    property.Accessors
                        is IrPropertyAccessors.GetOnly
                            or IrPropertyAccessors.GetInit,
                    typeOverrides,
                    bclRegistry
                ),
                _ => throw new UnreachableException(
                    $"Unexpected module variable {variable.GetType().Name}."
                ),
            };

            statements.Add(new TsTopLevelStatement(declaration));
        }
    }

    private static TsVariableDeclaration ConvertVariable(
        IrMemberDeclaration member,
        IrTypeRef type,
        IrExpression? initializer,
        bool isReadonly,
        IrToTsTypeOverrides? typeOverrides,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var value = initializer is not null
            ? IrToTsExpressionBridge.Map(initializer, bclRegistry)
            : IrToTsClassBridge.ComputeDefaultInitializer(type);

        return new TsVariableDeclaration(
            IrToTsNamingPolicy.ToDeclaredDataMemberName(member.Name, member.Attributes),
            value,
            // A `const` needs an initializer; a member with no literal default is a `let`.
            Const: isReadonly && value is not null,
            Exported: member.Visibility is not IrVisibility.Private,
            Type: IrToTsTypeMapper.Map(type, typeOverrides)
        );
    }

    private static TsFunction ConvertFunction(
        IrModuleFunction fn,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var name = IrToTsNamingPolicy.ToFunctionName(fn.Name, fn.Attributes);

        if (IrToTsObjectArgsBridge.HasObjectArgs(fn.Attributes))
            return ConvertObjectArgsFunction(fn, name, bclRegistry);

        var parameters = fn
            .Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                IrToTsTypeMapper.Map(p.Type),
                Optional: p.IsOptional,
                DefaultValue: p.HasDefaultValue && p.DefaultValue is not null
                    ? IrToTsExpressionBridge.Map(p.DefaultValue, bclRegistry)
                    : null,
                Rest: p.IsParams
            ))
            .ToList();
        var body = IrToTsBodyHelpers.LowerOrNotImplemented(fn.Body, fn.Name, bclRegistry);

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

    private static TsFunction ConvertObjectArgsFunction(
        IrModuleFunction fn,
        string name,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var (argsParam, destructure) = IrToTsObjectArgsBridge.BuildArgsParam(
            fn.Parameters,
            bclRegistry
        );

        var body = new List<TsStatement> { destructure };
        body.AddRange(IrToTsBodyHelpers.LowerOrNotImplemented(fn.Body, fn.Name, bclRegistry));

        return new TsFunction(
            name,
            [argsParam],
            IrToTsTypeMapper.Map(fn.ReturnType),
            body,
            Exported: true,
            Async: fn.Semantics.IsAsync,
            Generator: fn.Semantics.IsGenerator,
            TypeParameters: MapTypeParameters(fn.TypeParameters)
        );
    }

    private static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    ) => IrToTsTypeParameterMapper.Convert(typeParameters);
}
