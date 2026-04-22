using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrClassDeclaration"/> marked with <c>[PlainObject]</c>
/// into a TypeScript interface plus standalone helper functions for any
/// user-declared instance methods. The interface mirrors the primary
/// constructor's parameters as readonly fields (with optional fields for
/// parameters that have a default value, matching the wire-shape convention).
/// Each instance method becomes an exported function whose first parameter is
/// <c>self: T</c>; <c>this</c> references inside the body are rewritten to
/// <c>self</c> at the IR level so the bridge stays self-rename-agnostic.
/// </summary>
public static class IrToTsPlainObjectBridge
{
    public static bool Convert(
        IrClassDeclaration ir,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry = null
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
                var propName = IrToTsNamingPolicy.ToInterfaceMemberName(
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

        sink.Add(new TsInterface(tsName, properties));

        if (ir.Members is { Count: > 0 } members)
        {
            foreach (var member in members)
            {
                if (member is not IrMethodDeclaration method)
                    continue;
                if (method.IsStatic)
                    continue;
                EmitInstanceMethod(method, tsName, sink, bclRegistry);
            }
        }

        return true;
    }

    private static void EmitInstanceMethod(
        IrMethodDeclaration method,
        string ownerTsName,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // Inside a [PlainObject] method body `this` references rewrite to the
        // explicit `self` parameter — the type has no class wrapper at runtime,
        // so `this` would resolve to the wrong object.
        var rewrittenBody = method.Body is null
            ? null
            : (IReadOnlyList<IrStatement>)method.Body.Select(s => RewriteThis(s, "self")).ToList();
        var loweredBody = IrToTsBodyHelpers.LowerOrNotImplemented(
            rewrittenBody,
            method.Name,
            bclRegistry
        );

        // First parameter is always `self: T`; the rest are the C# parameters.
        var parameters = new List<TsParameter> { new("self", new TsNamedType(ownerTsName)) };
        parameters.AddRange(
            method.Parameters.Select(p => new TsParameter(
                TypeScriptNaming.ToCamelCase(p.Name),
                IrToTsTypeMapper.Map(p.Type),
                Optional: p.IsOptional
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

    private static IrStatement RewriteThis(IrStatement stmt, string identifierName) =>
        stmt switch
        {
            IrExpressionStatement es => new IrExpressionStatement(
                RewriteThis(es.Expression, identifierName)
            ),
            IrReturnStatement ret => new IrReturnStatement(
                ret.Value is null ? null : RewriteThis(ret.Value, identifierName)
            ),
            IrVariableDeclaration vd => vd with
            {
                Initializer = vd.Initializer is null
                    ? null
                    : RewriteThis(vd.Initializer, identifierName),
            },
            IrIfStatement ifs => new IrIfStatement(
                RewriteThis(ifs.Condition, identifierName),
                RewriteList(ifs.Then, identifierName),
                ifs.Else is null ? null : RewriteList(ifs.Else, identifierName)
            ),
            IrThrowStatement th => new IrThrowStatement(RewriteThis(th.Expression, identifierName)),
            IrBlockStatement block => new IrBlockStatement(
                RewriteList(block.Statements, identifierName)
            ),
            _ => stmt,
        };

    private static IReadOnlyList<IrStatement> RewriteList(
        IReadOnlyList<IrStatement> body,
        string identifierName
    ) => body.Select(s => RewriteThis(s, identifierName)).ToList();

    private static IrExpression RewriteThis(IrExpression expr, string identifierName) =>
        expr switch
        {
            IrThisExpression => new IrIdentifier(identifierName),
            IrMemberAccess ma => ma with { Target = RewriteThis(ma.Target, identifierName) },
            IrElementAccess ea => ea with
            {
                Target = RewriteThis(ea.Target, identifierName),
                Index = RewriteThis(ea.Index, identifierName),
            },
            IrCallExpression call => call with
            {
                Target = RewriteThis(call.Target, identifierName),
                Arguments = call
                    .Arguments.Select(a => a with { Value = RewriteThis(a.Value, identifierName) })
                    .ToList(),
            },
            IrBinaryExpression bin => bin with
            {
                Left = RewriteThis(bin.Left, identifierName),
                Right = RewriteThis(bin.Right, identifierName),
            },
            IrUnaryExpression un => un with { Operand = RewriteThis(un.Operand, identifierName) },
            IrConditionalExpression cond => cond with
            {
                Condition = RewriteThis(cond.Condition, identifierName),
                WhenTrue = RewriteThis(cond.WhenTrue, identifierName),
                WhenFalse = RewriteThis(cond.WhenFalse, identifierName),
            },
            IrWithExpression w => w with
            {
                Source = RewriteThis(w.Source, identifierName),
                Assignments = w
                    .Assignments.Select(a =>
                        a with
                        {
                            Value = RewriteThis(a.Value, identifierName),
                        }
                    )
                    .ToList(),
            },
            IrNewExpression ne => ne with
            {
                Arguments = ne
                    .Arguments.Select(a => a with { Value = RewriteThis(a.Value, identifierName) })
                    .ToList(),
            },
            IrCastExpression cast => cast with
            {
                Expression = RewriteThis(cast.Expression, identifierName),
            },
            IrAwaitExpression aw => aw with
            {
                Expression = RewriteThis(aw.Expression, identifierName),
            },
            IrThrowExpression th => th with
            {
                Expression = RewriteThis(th.Expression, identifierName),
            },
            IrArrayLiteral arr => arr with
            {
                Elements = arr.Elements.Select(e => RewriteThis(e, identifierName)).ToList(),
            },
            _ => expr,
        };
}
