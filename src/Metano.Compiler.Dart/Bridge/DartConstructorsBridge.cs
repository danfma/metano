using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Converts an <see cref="IrClassDeclaration"/>'s constructors and promoted
/// primary-constructor fields into Dart AST nodes (<see cref="DartConstructor"/>
/// and <see cref="DartField"/>).
/// </summary>
internal static class DartConstructorsBridge
{
    public static void AddPromotedFields(IrConstructorDeclaration? ctor, List<DartClassMember> acc)
    {
        if (ctor is null)
            return;

        foreach (var p in ctor.Parameters)
        {
            if (p.Promotion == IrParameterPromotion.None)
                continue;

            var isFinal = p.Promotion == IrParameterPromotion.ReadonlyProperty;

            acc.Add(
                new DartField(
                    IrToDartNamingPolicy.ToParameterName(p.Parameter.Name),
                    IrToDartTypeMapper.Map(p.Parameter.Type),
                    IsFinal: isFinal
                )
            );
        }
    }

    /// <summary>
    /// The class's constructors: one named constructor per overload of a
    /// dispatched set, in declaration order, or today's unnamed one.
    /// </summary>
    public static IReadOnlyList<DartConstructor>? ConvertConstructors(
        IrClassDeclaration ir,
        string className,
        DartOverloadClaims claims,
        bool namesOverloadMembers,
        DartIrRewriter rewriter
    )
    {
        if (ir.Constructor is not { } constructor)
            return null;

        if (
            !namesOverloadMembers
            || constructor is not { Overloads.Count: > 0, OverloadName: not null }
        )
            return [ConvertConstructor(constructor, className, name: null, rewriter)];

        return constructor
            .DeclarationsInOrder.Select(overload =>
                ConvertNamedConstructor(overload, className, claims, rewriter)
            )
            .ToList();
    }

    /// <summary>
    /// The name of the constructor <c>copyWith</c> rebuilds a record through:
    /// the primary's named constructor when the set is dispatched, else
    /// <c>null</c> (the unnamed one).
    /// </summary>
    public static string? PrimaryConstructorNameOf(
        IrClassDeclaration ir,
        DartOverloadClaims claims,
        bool namesOverloadMembers
    ) =>
        namesOverloadMembers && ir.Constructor is { Overloads.Count: > 0 } primary
            ? claims.ConstructorNameAt(primary.DeclarationOrdinal)
            : null;

    /// <summary>
    /// Whether a constructor of a dispatched set chains through
    /// <c>: this(...)</c> and has a body of its own, which a Dart redirecting
    /// constructor cannot carry: its named constructor keeps the body without
    /// the chain.
    /// </summary>
    public static bool KeepsBodyWithoutChain(IrConstructorDeclaration overload) =>
        overload is { ThisChain: not null, Body.Count: > 0 };

    private static DartConstructor ConvertNamedConstructor(
        IrConstructorDeclaration overload,
        string className,
        DartOverloadClaims claims,
        DartIrRewriter rewriter
    )
    {
        var name = claims.RequiredConstructorNameAt(overload.DeclarationOrdinal);
        var constructor = ConvertConstructor(overload, className, name, rewriter);

        if (overload.ThisChain is not { } chain || KeepsBodyWithoutChain(overload))
            return constructor;

        var redirect = new DartConstructorRedirect(
            claims.RequiredConstructorNameAt(chain.Target.DeclarationOrdinal),
            DartConstructorChainArguments.Of(chain, rewriter)
        );

        // A redirecting constructor initializes nothing itself: Dart rejects
        // `this.x` parameters there, so promoted ones become plain parameters.
        return constructor with
        {
            Parameters = ConvertParameters(overload, rewriter, initializesFields: false),
            Body = null,
            Redirect = redirect,
        };
    }

    private static DartConstructor ConvertConstructor(
        IrConstructorDeclaration ctor,
        string className,
        string? name,
        DartIrRewriter rewriter
    )
    {
        var capturedAssignments = BuildCapturedAssignments(ctor);
        var explicitBody = rewriter.Rewrite(ctor.Body);

        IReadOnlyList<IrStatement>? body =
            capturedAssignments.Count == 0 ? explicitBody
            : explicitBody is null ? capturedAssignments
            : [.. capturedAssignments, .. explicitBody];

        return new DartConstructor(
            className,
            ConvertParameters(ctor, rewriter, initializesFields: true),
            Body: body,
            Name: name,
            SuperCall: BuildSuperCall(ctor, rewriter)
        );
    }

    private static DartSuperCall? BuildSuperCall(
        IrConstructorDeclaration ctor,
        DartIrRewriter rewriter
    )
    {
        if (ctor.ThisChain is not null)
            return null;

        var superName = ctor.BaseOverload is { } baseOverload
            ? rewriter.ConstructorNameOf(baseOverload.Target)
            : null;

        var hasArgs = ctor.BaseArguments is { Count: > 0 };

        if (superName is null && !hasArgs)
            return null;

        var args = (ctor.BaseArguments ?? [])
            .Select(arg => arg with { Value = rewriter.Rewrite(arg.Value)! })
            .ToList();

        return new DartSuperCall(superName, args);
    }

    private static IReadOnlyList<IrStatement> BuildCapturedAssignments(
        IrConstructorDeclaration ctor
    ) =>
        ctor
            .Parameters.Where(p => p.CapturedFieldName is not null)
            .Select(p =>
                (IrStatement)
                    new IrExpressionStatement(
                        new IrBinaryExpression(
                            new IrMemberAccess(
                                new IrThisExpression(),
                                IrToDartNamingPolicy.ToParameterName(p.CapturedFieldName!)
                            ),
                            IrBinaryOp.Assign,
                            new IrIdentifier(IrToDartNamingPolicy.ToParameterName(p.Parameter.Name))
                        )
                    )
            )
            .ToList();

    private static IReadOnlyList<DartConstructorParameter> ConvertParameters(
        IrConstructorDeclaration ctor,
        DartIrRewriter rewriter,
        bool initializesFields
    ) =>
        ctor
            .Parameters.Select(p =>
            {
                var isFieldInitializer =
                    initializesFields && p.Promotion != IrParameterPromotion.None;

                return new DartConstructorParameter(
                    Name: IrToDartNamingPolicy.ToParameterName(p.Parameter.Name),
                    Type: isFieldInitializer ? null : IrToDartTypeMapper.Map(p.Parameter.Type),
                    IsFieldInitializer: isFieldInitializer,
                    IsRequired: !p.Parameter.HasDefaultValue,
                    DefaultValue: rewriter.Rewrite(p.Parameter.DefaultValue)
                );
            })
            .ToList();
}
