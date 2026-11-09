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
        DartIrRewriter rewriter
    )
    {
        foreach (var fn in functions)
            statements.Add(ConvertFunction(fn, NameOf(fn, claims), rewriter));
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
