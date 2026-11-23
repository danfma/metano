using Metano.Compiler.Analysis;
using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Synthesizes <c>operator ==</c>, <c>hashCode</c>, and (for records with a
/// primary constructor) <c>copyWith</c> on Dart record classes (#243, #574).
/// </summary>
internal static class DartRecordMembersSynthesizer
{
    private const int HashCodeMaxCombineArity = 4;

    public static bool ShouldSynthesize(IrClassDeclaration ir) =>
        ir.Semantics.IsRecord && !ir.Semantics.IsPlainObject;

    public static void Synthesize(
        IrClassDeclaration ir,
        string className,
        string? primaryConstructorName,
        List<DartClassMember> members
    )
    {
        var equalityFields = (ir.EqualityMembers ?? [])
            .Select(m =>
                (
                    Name: m.EmittedName ?? IrToDartNamingPolicy.ToParameterName(m.Name),
                    Type: IrToDartTypeMapper.Map(m.Type)
                )
            )
            .ToList();

        var hasRecordBase =
            ir.Semantics.IsRecord
            && ir.BaseType is IrNamedTypeRef { Semantics.IsTranspilable: true };

        members.Add(
            IrValueEqualitySynthesis.DeclaresEquals(ir)
                ? BuildEqualsOperatorOverUserEquals(className)
                : BuildEqualsOperator(className, equalityFields, hasRecordBase)
        );

        if (!IrValueEqualitySynthesis.DeclaresHashCode(ir))
            members.Add(BuildHashCodeGetter(equalityFields, hasRecordBase));

        if (ir.Semantics.IsRecord && ir.Constructor is { } ctor)
        {
            var copyFields = ctor
                .Parameters.Select(p =>
                    (
                        Name: IrToDartNamingPolicy.ToParameterName(p.Parameter.Name),
                        Type: IrToDartTypeMapper.Map(p.Parameter.Type)
                    )
                )
                .ToList();

            members.Add(BuildCopyWithMethod(className, primaryConstructorName, copyFields));
        }
    }

    /// <summary>
    /// <c>bool operator ==(Object other) =&gt; equals(other)</c> next to a
    /// user <c>Equals(object)</c> override.
    /// </summary>
    public static DartMethodSignature BuildEqualsOperatorDelegatingTo(string equalsName) =>
        BuildEqualsOperatorReturning(CallUserEquals(equalsName));

    private static DartMethodSignature BuildEqualsOperatorOverUserEquals(string className) =>
        BuildEqualsOperatorReturning(
            new IrBinaryExpression(
                new IrIsPatternExpression(
                    new IrIdentifier("other"),
                    new IrTypePattern(new IrNamedTypeRef(className))
                ),
                IrBinaryOp.LogicalAnd,
                CallUserEquals(IrToDartNamingPolicy.ToMemberName(nameof(object.Equals), null))
            )
        );

    private static IrCallExpression CallUserEquals(string emittedName) =>
        new(
            new IrMemberAccess(new IrThisExpression(), emittedName),
            [new IrArgument(new IrIdentifier("other"))]
        );

    private static DartMethodSignature BuildEqualsOperatorReturning(IrExpression result) =>
        new(
            Name: "==",
            Parameters: [new DartParameter("other", new DartNamedType("Object"))],
            ReturnType: new DartNamedType("bool"),
            Body: [new IrReturnStatement(result)],
            OperatorSymbol: "==",
            IsOverride: true
        );

    private static DartMethodSignature BuildEqualsOperator(
        string className,
        IReadOnlyList<(string Name, DartType Type)> fields,
        bool hasRecordBase
    )
    {
        IrExpression body = new IrIsPatternExpression(
            new IrIdentifier("other"),
            new IrTypePattern(new IrNamedTypeRef(className))
        );

        IrExpression baseCheck = hasRecordBase
            ? new IrBinaryExpression(
                new IrBaseExpression(),
                IrBinaryOp.Equal,
                new IrIdentifier("other")
            )
            : new IrBinaryExpression(
                new IrMemberAccess(new IrIdentifier("other"), "runtimeType"),
                IrBinaryOp.Equal,
                new IrMemberAccess(new IrThisExpression(), "runtimeType")
            );

        body = new IrBinaryExpression(body, IrBinaryOp.LogicalAnd, baseCheck);

        foreach (var (name, _) in fields)
        {
            body = new IrBinaryExpression(
                body,
                IrBinaryOp.LogicalAnd,
                new IrBinaryExpression(
                    new IrMemberAccess(new IrIdentifier("other"), name),
                    IrBinaryOp.Equal,
                    new IrMemberAccess(new IrThisExpression(), name)
                )
            );
        }

        return BuildEqualsOperatorReturning(body);
    }

    private static DartClassMember BuildHashCodeGetter(
        IReadOnlyList<(string Name, DartType Type)> fields,
        bool hasRecordBase
    )
    {
        var items = new List<IrExpression>(fields.Count + (hasRecordBase ? 1 : 0));

        if (hasRecordBase)
            items.Add(new IrMemberAccess(new IrBaseExpression(), "hashCode"));

        foreach (var (name, _) in fields)
            items.Add(new IrMemberAccess(new IrThisExpression(), name));

        IReadOnlyList<IrStatement> body =
            items.Count == 0 ? [new IrReturnStatement(new IrLiteral(0, IrLiteralKind.Int32))]
            : items.Count == 1 && hasRecordBase ? [new IrReturnStatement(items[0])]
            : items.Count <= HashCodeMaxCombineArity
                ? [new IrReturnStatement(BuildCombineExpression(items))]
            : BuildHashCodeBuilderBody(items);

        return new DartGetter(
            Name: "hashCode",
            ReturnType: new DartNamedType("int"),
            Body: body,
            IsOverride: true
        );
    }

    private static IrCallExpression BuildCombineExpression(IReadOnlyList<IrExpression> items)
    {
        var combineMethod = items.Count == 1 ? "combine" : $"combine{items.Count}";
        var args = items.Select(item => new IrArgument(item)).ToList();

        return new IrCallExpression(
            new IrMemberAccess(new IrTypeReference("HashCode"), combineMethod),
            args
        );
    }

    private static IReadOnlyList<IrStatement> BuildHashCodeBuilderBody(
        IReadOnlyList<IrExpression> items
    )
    {
        var statements = new List<IrStatement>(items.Count + 2)
        {
            new IrVariableDeclaration(
                Name: "hc",
                Type: null,
                Initializer: new IrNewExpression(new IrNamedTypeRef("HashCode"), [])
            ),
        };

        foreach (var item in items)
        {
            statements.Add(
                new IrExpressionStatement(
                    new IrCallExpression(
                        new IrMemberAccess(new IrIdentifier("hc"), "add"),
                        [new IrArgument(item)]
                    )
                )
            );
        }

        statements.Add(
            new IrReturnStatement(
                new IrCallExpression(new IrMemberAccess(new IrIdentifier("hc"), "toHashCode"), [])
            )
        );

        return statements;
    }

    private static DartMethodSignature BuildCopyWithMethod(
        string className,
        string? primaryConstructorName,
        IReadOnlyList<(string Name, DartType Type)> fields
    )
    {
        var parameters = fields
            .Select(f => new DartParameter(
                f.Name,
                f.Type is DartNullableType ? f.Type : new DartNullableType(f.Type),
                IsNamed: true
            ))
            .ToList();

        var callArgs = fields
            .Select(f => new IrArgument(
                new IrBinaryExpression(
                    new IrIdentifier(f.Name),
                    IrBinaryOp.NullCoalescing,
                    new IrMemberAccess(new IrThisExpression(), f.Name)
                )
            ))
            .ToList();

        IrExpression body = primaryConstructorName is null
            ? new IrNewExpression(new IrNamedTypeRef(className), callArgs)
            : new IrCallExpression(
                new IrMemberAccess(new IrTypeReference(className), primaryConstructorName),
                callArgs
            );

        return new DartMethodSignature(
            Name: "copyWith",
            Parameters: parameters,
            ReturnType: new DartNamedType(className),
            Body: [new IrReturnStatement(body)]
        );
    }
}
