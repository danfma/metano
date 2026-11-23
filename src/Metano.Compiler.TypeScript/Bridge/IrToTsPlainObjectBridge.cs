using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> marked with <c>[PlainObject]</c>
/// into a TypeScript interface plus standalone helper functions for any
/// user-declared instance methods. The interface mirrors the primary
/// constructor's parameters as readonly fields (with optional fields for
/// parameters that have a default value, matching the wire-shape convention).
/// Each instance method becomes an exported function whose first parameter is
/// <c>self: T</c>; every <c>this</c> inside the body lowers to <c>self</c>
/// (<see cref="IrToTsBridgeContext.ThisReceiver"/>).
/// </summary>
public static class IrToTsPlainObjectBridge
{
    private const string SelfParameterName = "self";

    internal static bool Convert(
        IrClassDeclaration ir,
        List<TsTopLevel> sink,
        IrToTsBridgeContext context
    )
    {
        if (!ir.Semantics.IsPlainObject)
            return false;

        var tsName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);

        var properties = new List<TsProperty>();
        if (ir.Constructor is not null)
        {
            foreach (var ctorParam in ir.Constructor.Parameters)
            {
                var param = ctorParam.Parameter;
                // [PlainObject] interface members use the promoted property's public name
                // semantics. The IR carries the parameter name in its original C# casing;
                // camelCase (with reserved-word escaping) matches the legacy output.
                var propName = IrToTsNamingPolicy.ToDeclaredDataMemberName(
                    param.Name,
                    attributes: null
                );
                var propType = IrToTsTypeMapper.Map(param.Type);
                properties.Add(
                    new TsProperty(
                        propName,
                        propType,
                        Readonly: true,
                        Optional: param.HasDefaultValue || param.IsOptional
                    )
                );
            }
        }

        sink.Add(
            new TsInterface(tsName, properties, Extends: IrToTsClassBridge.BuildImplements(ir))
        );

        if (ir.Members is { Count: > 0 } members)
        {
            foreach (var member in members)
            {
                switch (member)
                {
                    case IrMethodDeclaration method when !method.IsStatic:
                        EmitInstanceMethod(method, tsName, sink, context);
                        break;
                    case IrFieldDeclaration field when field.IsStatic:
                        EmitStaticField(field, sink, context);
                        break;
                    // Static methods + instance fields stay deferred:
                    // static methods on a `[PlainObject]` record can land
                    // in a follow-up alongside any other "shared
                    // utilities" emission convention; instance fields on
                    // an interface-shaped record cannot carry a class
                    // body, so they would need either a separate
                    // top-level helper or a redesign of the wire shape.
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Emits a record's <c>static readonly</c> field as a top-level
    /// <c>export const</c> in the same module file as the
    /// <c>[PlainObject]</c> interface. Honors <c>[Name]</c> overrides
    /// the same way every other surface does. The field's initializer
    /// flows through the standard expression bridge, so a record
    /// constructor call reads back as the matching object literal.
    /// </summary>
    private static void EmitStaticField(
        IrFieldDeclaration field,
        List<TsTopLevel> sink,
        IrToTsBridgeContext context
    )
    {
        // Top-level `export const/let` declarations make the symbol
        // public to every consumer of the module. C# accessibility
        // beyond `public` (private / protected / internal /
        // protected-internal / private-protected) cannot project
        // onto an export — emitting them would leak implementation
        // details and contradict the C# surface. Restrict the
        // emission to `public` static fields; other visibilities
        // stay confined to whatever C#-side consumers exist.
        if (field.Visibility != IrVisibility.Public)
            return;
        if (field.Initializer is null)
            return;

        // Member naming policy for fields stays on
        // `ToDeclaredDataMemberName` (camelCase + reserved-word
        // escaping) so a static field on a `[PlainObject]` record
        // surfaces under the same identifier shape as the rest of
        // the module's members. Using `ToTypeName` here would
        // PascalCase the export and disagree with every other field
        // in the codebase. The override path (`[Name("delete")]`)
        // bypasses `ToCamelCase`'s reserved-word escape; reapply it
        // here because a top-level `const` / `let` cannot use a
        // reserved identifier (unlike class members, which can).
        var name = TypeScriptNaming.EscapeIfReserved(
            IrToTsNamingPolicy.ToDeclaredDataMemberName(field.Name, field.Attributes)
        );
        var initializer = IrToTsExpressionBridge.Map(field.Initializer, context);
        // `readonly` C# fields lower to a `const` declaration; the
        // rare mutable-static case (a top-level cache or counter)
        // emits as a `let` so call sites can still rebind it. Both
        // forms keep the `export` modifier so consumers in the same
        // module can import the symbol.
        sink.Add(
            new TsTopLevelStatement(
                new TsVariableDeclaration(
                    name,
                    initializer,
                    Const: field.IsReadonly,
                    Exported: true
                )
            )
        );
    }

    private static void EmitInstanceMethod(
        IrMethodDeclaration method,
        string ownerTsName,
        List<TsTopLevel> sink,
        IrToTsBridgeContext context
    )
    {
        // Inside a [PlainObject] method body every `this` lowers to the
        // explicit `self` parameter — the type has no class wrapper at runtime,
        // so `this` would resolve to the wrong object.
        var loweredBody = IrToTsBodyHelpers.LowerOrNotImplemented(
            method.Body,
            method.Name,
            context.WithThisReceiver(SelfParameterName)
        );

        // First parameter is always `self: T`; the rest are the C# parameters.
        var parameters = new List<TsParameter>
        {
            new(SelfParameterName, new TsNamedType(ownerTsName)),
        };

        parameters.AddRange(
            method.Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p),
                Optional: p.IsOptional,
                Rest: p.IsRestParams
            ))
        );

        // Function name escapes reserved words because top-level function
        // declarations can't use them (`function delete() {}` is illegal even
        // though `obj.delete` is fine). The IR call-site lowering routes
        // through the same ToCamelCase variant so the two halves stay in sync.
        var fnName = IrToTsNamingPolicy.ToFunctionName(method.Name, method.Attributes);

        sink.Add(
            new TsFunction(
                fnName,
                parameters,
                IrToTsTypeMapper.Map(method.ReturnType),
                loweredBody,
                Exported: true,
                Async: method.Semantics.IsAsync
            )
        );
    }
}
