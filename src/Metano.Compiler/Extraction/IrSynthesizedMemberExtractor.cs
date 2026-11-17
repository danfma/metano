using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts a synthesized value-equality member of a group (spec 013 FR-038,
/// FR-040) as an <see cref="IrMethodDeclaration"/> carrying its
/// <see cref="IrMethodDeclaration.SynthesizedKind"/>: the shape its kind fixes,
/// and a body when the type supplies one — the user declaration folded into it,
/// or, for a record declaring <c>Equals(R)</c>, the route C#'s
/// <c>Equals(object)</c> takes (<c>Equals(other as R)</c>). Without one the body
/// is <c>null</c> and the target synthesizes it.
/// </summary>
internal static class IrSynthesizedMemberExtractor
{
    private const string EqualsParameter = "other";
    private const string CopyParameter = "overrides";

    /// <summary>The member as its group declares it, named with <paramref name="suffix"/>.</summary>
    public static IrMethodDeclaration Extract(
        INamedTypeSymbol type,
        string groupName,
        SynthesizedMember member,
        string suffix,
        IrExtractionContext context
    )
    {
        var declaration = member.Replacement is { } replacement
            ? IrMethodExtractor.Extract(replacement, context)
            : SignatureOf(type, groupName, member.Kind, context) with
            {
                Body = RouteOf(type, member.Kind, context),
            };

        return declaration with
        {
            Name = groupName,
            Visibility = IrVisibility.Public,
            Attributes = null,
            Semantics = declaration.Semantics with { IsSynthesized = true },
            SynthesizedKind = member.Kind,
            OverloadName = new IrOverloadName(suffix),
            SignatureText = SignatureTextOf(member.Kind),
        };
    }

    /// <summary>
    /// The signature of <paramref name="kind"/> on <paramref name="type"/>, as an
    /// inherited dispatcher lists it or the member declares it before its body.
    /// </summary>
    public static IrMethodDeclaration SignatureOf(
        INamedTypeSymbol type,
        string groupName,
        IrSynthesizedValueMember kind,
        IrExtractionContext context
    ) =>
        new(
            groupName,
            IrVisibility.Public,
            IsStatic: false,
            ParametersOf(kind),
            ReturnTypeOf(type, kind, context),
            Body: null,
            new IrMethodSemantics(IsSynthesized: true)
        )
        {
            SynthesizedKind = kind,
            SignatureText = SignatureTextOf(kind),
        };

    private static IReadOnlyList<IrParameter> ParametersOf(IrSynthesizedValueMember kind) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => [new IrParameter(EqualsParameter, AnyObject)],
            IrSynthesizedValueMember.Copy =>
            [
                new IrParameter(CopyParameter, AnyObject, IsOptional: true),
            ],
            _ => [],
        };

    /// <summary>Any value, <c>null</c> included: the argument the runtime passes.</summary>
    private static IrTypeRef AnyObject { get; } = new IrPrimitiveTypeRef(IrPrimitive.Object);

    private static IrTypeRef ReturnTypeOf(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        IrExtractionContext context
    ) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => new IrPrimitiveTypeRef(IrPrimitive.Boolean),
            IrSynthesizedValueMember.HashCode => new IrPrimitiveTypeRef(IrPrimitive.Int32),
            _ => IrTypeRefMapper.Map(type, context.OriginResolver, context.Target),
        };

    /// <summary>
    /// <c>return this.Equals(other is R ? (R)other : null);</c> for a record that
    /// declares <c>Equals(R)</c>: C#'s synthesized <c>Equals(object)</c> calls it
    /// with <c>other as R</c>. <c>null</c> for any other member.
    /// </summary>
    private static IReadOnlyList<IrStatement>? RouteOf(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        IrExtractionContext context
    )
    {
        if (kind is not IrSynthesizedValueMember.Equals || !type.IsRecord)
            return null;

        if (ValueEqualityShapes.ReplacingEqualsOf(type, context.Target) is not { } replacing)
            return null;

        var recordType = IrTypeRefMapper.Map(type, context.OriginResolver, context.Target);
        var other = new IrIdentifier(EqualsParameter);

        var asRecord = new IrConditionalExpression(
            new IrIsPatternExpression(other, new IrTypePattern(recordType)),
            new IrCastExpression(other, recordType),
            new IrLiteral(null, IrLiteralKind.Null)
        );

        var call = new IrCallExpression(
            new IrMemberAccess(new IrThisExpression(), replacing.Name),
            [new IrArgument(asRecord)]
        )
        {
            Overload = new IrSelectedOverload(
                MethodOverloadGroups.RefOf(replacing, context.GroupingPolicy)
            ),
        };

        return [new IrReturnStatement(call)];
    }

    private static string SignatureTextOf(IrSynthesizedValueMember kind) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => "Equals(object)",
            IrSynthesizedValueMember.HashCode => "GetHashCode()",
            _ => "<Clone>$()",
        };
}
