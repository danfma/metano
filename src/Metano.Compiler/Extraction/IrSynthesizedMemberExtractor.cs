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

    /// <summary>Any value, <c>null</c> included: the argument the runtime passes.</summary>
    private static IrTypeRef AnyObject { get; } = new IrPrimitiveTypeRef(IrPrimitive.Object);

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
    /// The route C#'s <c>Equals(object)</c> takes to a typed replacing
    /// <c>Equals</c>: <c>return this.Equals(other is R ? (R)other : null);</c> for a
    /// record's <c>Equals(R)</c>, <c>return other is S ? this.Equals((S)other) : false;</c>
    /// for a struct's interface-implementing <c>Equals(S)</c>. <c>null</c> for any
    /// other member.
    /// </summary>
    private static IReadOnlyList<IrStatement>? RouteOf(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        IrExtractionContext context
    )
    {
        if (kind is not IrSynthesizedValueMember.Equals)
            return null;

        if (ValueEqualityShapes.RoutedEqualsOf(type, context.GroupingPolicy) is not { } routed)
            return null;

        // An explicit implementation the target drops has no member to call:
        // the target's own body compares the fields instead.
        if (MethodOverloadGroups.GroupOf(routed, context.GroupingPolicy) is null)
            return null;

        var ownType = IrTypeRefMapper.Map(type, context.OriginResolver, context.Target);
        var other = new IrIdentifier(EqualsParameter);
        var isOwnType = new IrIsPatternExpression(other, new IrTypePattern(ownType));
        var asOwnType = new IrCastExpression(other, ownType);

        if (!type.IsRecord)
        {
            return
            [
                new IrReturnStatement(
                    new IrConditionalExpression(
                        isOwnType,
                        RoutedCall(routed, asOwnType, context),
                        new IrLiteral(false, IrLiteralKind.Boolean)
                    )
                ),
            ];
        }

        var asRecord = new IrConditionalExpression(
            isOwnType,
            asOwnType,
            new IrLiteral(null, IrLiteralKind.Null)
        );

        return [new IrReturnStatement(RoutedCall(routed, asRecord, context))];
    }

    /// <summary><c>this.Equals(argument)</c>, bound to the routed declaration's overload.</summary>
    private static IrCallExpression RoutedCall(
        IMethodSymbol routed,
        IrExpression argument,
        IrExtractionContext context
    ) =>
        new(
            new IrMemberAccess(new IrThisExpression(), OverloadGroupName.Of(routed)),
            [new IrArgument(argument)]
        )
        {
            Overload = new IrSelectedOverload(
                MethodOverloadGroups.RefOf(routed, context.GroupingPolicy)
            ),
        };

    private static string SignatureTextOf(IrSynthesizedValueMember kind) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => "Equals(object)",
            IrSynthesizedValueMember.HashCode => "GetHashCode()",
            _ => "<Clone>$()",
        };
}
