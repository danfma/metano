using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// <c>[ObjectArgs]</c> object-literal call-surface helpers — a method
/// whose single parameter is the synthesized object literal type, plus
/// the static <c>create({...})</c> factory paired with a positional
/// runtime constructor.
/// </summary>
internal static partial class IrToTsClassBridge
{
    /// <summary>
    /// Lowers an <c>[ObjectArgs]</c>-annotated method into a TS method whose
    /// single parameter is the synthesized object literal type produced by
    /// <see cref="IrToTsObjectArgsBridge.BuildArgsParam"/>; the destructure
    /// header restores the original parameter names so the lowered body
    /// keeps working unchanged. Abstract members are not routed through
    /// this path — the caller filters them out.
    /// </summary>
    public static TsMethodMember BuildObjectArgsMethod(
        IrMethodDeclaration method,
        TsType returnType,
        IReadOnlyList<TsTypeParameter>? typeParameters,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var name = IrToTsNamingPolicy.ToMethodName(method.Name, method.Attributes);
        var (argsParam, destructureHeader) = IrToTsObjectArgsBridge.BuildArgsParam(
            method.Parameters,
            bclRegistry
        );

        var body = new List<TsStatement> { destructureHeader };
        body.AddRange(
            IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, bclRegistry)
        );

        return new TsMethodMember(
            name,
            [argsParam],
            returnType,
            body,
            Static: method.IsStatic,
            Async: method.Semantics.IsAsync,
            Generator: method.Semantics.IsGenerator,
            Accessibility: MapAccessibility(method.Visibility),
            TypeParameters: typeParameters,
            Abstract: false
        );
    }

    /// <summary>
    /// Builds the static <c>create({...})</c> factory paired with a class
    /// whose constructor carries <c>[ObjectArgs]</c>. The factory exposes the
    /// object-literal call surface while the runtime constructor stays
    /// positional, so generated <c>super(...)</c> calls and inheritance
    /// continue to work unchanged. Returns <c>null</c> when the IR carries no
    /// constructor.
    /// </summary>
    public static TsMethodMember? BuildObjectArgsCreateFactory(
        IrConstructorDeclaration? ctor,
        string typeName,
        IReadOnlyList<TsTypeParameter>? typeParameters,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (ctor is null)
            return null;

        var parameters = ctor.Parameters.Select(p => p.Parameter).ToList();
        var (argsParam, destructureHeader) = IrToTsObjectArgsBridge.BuildArgsParam(
            parameters,
            bclRegistry
        );

        var ctorArgs = parameters
            .Select(p => (TsExpression)new TsIdentifier(TypeScriptNaming.ToCamelCase(p.Name)))
            .ToList();

        TsType returnType = typeParameters is { Count: > 0 } tps
            ? new TsNamedType(typeName, tps.Select(tp => (TsType)new TsNamedType(tp.Name)).ToList())
            : new TsNamedType(typeName);

        IReadOnlyList<TsStatement> body =
        [
            destructureHeader,
            new TsReturnStatement(new TsNewExpression(new TsIdentifier(typeName), ctorArgs)),
        ];

        return new TsMethodMember(
            "create",
            [argsParam],
            returnType,
            body,
            Static: true,
            Accessibility: TsAccessibility.Public,
            TypeParameters: typeParameters
        );
    }
}
