using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Lowers <see cref="IrModuleFunction"/>s into top-level <see cref="DartFunction"/>
/// declarations. Called for C# static classes decorated with
/// <c>[ExportedAsModule]</c>: rather than a Dart class of static methods, the
/// consumer gets a flat file of importable functions, which is the idiomatic
/// Dart shape for a utility module. An overload of a dispatched group is declared under its
/// claimed fast path (<c>twiceText</c>, spec 013 FR-022, FR-032).
/// </summary>
public static class IrToDartModuleBridge
{
    public static void Convert(
        IReadOnlyList<IrModuleFunction> functions,
        DartOverloadClaims claims,
        List<DartTopLevel> statements,
        DartIrRewriter rewriter,
        IReadOnlyList<IrMemberDeclaration>? variables = null
    )
    {
        foreach (var variable in variables ?? [])
            AddVariable(variable, statements, rewriter);

        foreach (var fn in functions)
            statements.Add(ConvertFunction(fn, NameOf(fn, claims), rewriter));
    }

    internal static string VariableNameOf(IrMemberDeclaration member)
    {
        var name = IrToDartNamingPolicy.ToMemberName(member.Name, member.Attributes);

        if (
            member.Visibility == IrVisibility.Private
            && IrToDartNamingPolicy.FindNameOverride(member.Attributes) is null
            && !name.StartsWith('_')
        )
        {
            return "_" + name;
        }

        return name;
    }

    private static void AddVariable(
        IrMemberDeclaration variable,
        List<DartTopLevel> statements,
        DartIrRewriter rewriter
    )
    {
        var name = VariableNameOf(variable);

        switch (variable)
        {
            case IrFieldDeclaration field:
                statements.Add(
                    new DartTopLevelVariable(
                        Name: name,
                        Type: IrToDartTypeMapper.Map(field.Type),
                        IsFinal: field.IsReadonly,
                        IsLate: field.Type is not IrNullableTypeRef && field.Initializer is null,
                        Initializer: rewriter.Rewrite(field.Initializer)
                    )
                );
                break;

            case IrPropertyDeclaration prop when !(prop.Semantics?.HasGetterBody ?? false):
                var isFinal =
                    prop.Accessors is IrPropertyAccessors.GetOnly or IrPropertyAccessors.GetInit;

                statements.Add(
                    new DartTopLevelVariable(
                        Name: name,
                        Type: IrToDartTypeMapper.Map(prop.Type),
                        IsFinal: isFinal,
                        IsLate: prop.Type is not IrNullableTypeRef && prop.Initializer is null,
                        Initializer: rewriter.Rewrite(prop.Initializer)
                    )
                );
                break;

            case IrPropertyDeclaration prop:
                statements.Add(
                    new DartTopLevelGetter(
                        Name: name,
                        ReturnType: IrToDartTypeMapper.Map(prop.Type),
                        Body: rewriter.Rewrite(prop.GetterBody)
                    )
                );

                if (prop.Semantics?.HasSetterBody ?? false)
                {
                    statements.Add(
                        new DartTopLevelSetter(
                            Name: name,
                            ValueType: IrToDartTypeMapper.Map(prop.Type),
                            Body: rewriter.Rewrite(prop.SetterBody)
                        )
                    );
                }
                break;
        }
    }

    private static string NameOf(IrModuleFunction fn, DartOverloadClaims claims) =>
        fn is { OverloadRef: { } overloadRef, OverloadName: not null }
            ? claims.RequiredFastPathAt(overloadRef.MemberName, overloadRef.Ordinal)
            : IrToDartNamingPolicy.ToMemberName(fn.Name, fn.Attributes);

    private static DartFunction ConvertFunction(
        IrModuleFunction fn,
        string name,
        DartIrRewriter rewriter
    ) =>
        new(
            Name: name,
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
            Body: rewriter.Rewrite(fn.Body),
            IsAsync: fn.Semantics.IsAsync
        );
}
