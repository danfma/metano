using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Converts an <see cref="IrEnumDeclaration"/> to TypeScript AST statements: an
/// <c>as const</c> object (<see cref="TsConstObject"/>) holding the members, plus a
/// companion <see cref="TsTypeAlias"/> of the same name (spec 011 FR-044). Never a TS
/// <c>enum</c>, whose compiled form is an IIFE every bundler keeps.
/// <list type="bullet">
///   <item><see cref="IrEnumStyle.String"/> and a plain numeric enum: the closed union
///   of the member values, <c>typeof E[keyof typeof E]</c>.</item>
///   <item>A <c>[Flags]</c> numeric enum: the brand
///   <c>number &amp; { readonly __enum?: "E" }</c>, so bit combinations, <c>0</c> and
///   casts type-check while another enum's values do not.</item>
/// </list>
/// An enum over <c>long</c> / <c>ulong</c> holds <c>bigint</c> values
/// (<c>High: 1099511627776n</c>) and its brand is <c>bigint &amp; { … }</c>
/// (spec 011 FR-044 as amended by #701).
/// </summary>
public static class IrToTsEnumBridge
{
    public static void Convert(IrEnumDeclaration ir, List<TsTopLevel> statements)
    {
        var tsName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);

        var entries = ir
            .Members.Select(member => new TsConstObjectEntry(
                MemberKey(ir, member),
                MemberValue(ir, member)
            ))
            .ToList();

        statements.Add(new TsConstObject(tsName, entries));
        statements.Add(new TsTypeAlias(tsName, AliasType(ir, tsName)));
    }

    /// <summary>
    /// The key of a member: its C# name for a string enum (whose value is the emitted
    /// name), its emitted name for a numeric enum.
    /// </summary>
    private static string MemberKey(IrEnumDeclaration ir, IrEnumMember member) =>
        ir.Style == IrEnumStyle.String
            ? member.Name
            : IrToTsNamingPolicy.ToEnumMemberName(member.Name, member.Attributes);

    private static TsExpression MemberValue(IrEnumDeclaration ir, IrEnumMember member) =>
        ir.Style == IrEnumStyle.String
            ? new TsStringLiteral(
                IrToTsNamingPolicy.ToEnumMemberName(member.Name, member.Attributes)
            )
            : new TsLiteral(NumericMemberValue(ir, member.Value));

    /// <summary>
    /// The one rule for an enum's alias: a numeric <c>[Flags]</c> enum is the
    /// brand, every other enum the closed union of its values (which
    /// <see cref="EnumValueAssertion"/> asserts computed values into).
    /// </summary>
    internal static bool HasFlagsBrand(bool isNumeric, bool isFlags) => isNumeric && isFlags;

    private static TsType AliasType(IrEnumDeclaration ir, string tsName) =>
        HasFlagsBrand(ir.Style == IrEnumStyle.Numeric, ir.IsFlags)
            ? new TsNamedType($"{BaseType(ir)} & {{ readonly __enum?: \"{tsName}\" }}")
            : new TsNamedType($"typeof {tsName}[keyof typeof {tsName}]");

    /// <summary>Whether the enum's values are <c>bigint</c>s: its underlying type is 64-bit.</summary>
    internal static bool HoldsBigInts(IrNumericKind underlying) => underlying.IsSixtyFourBit();

    /// <summary>Whether the enum <paramref name="type"/> (or its nullable form) holds <c>bigint</c>s.</summary>
    internal static bool HoldsBigInts(IrTypeRef type) =>
        (type is IrNullableTypeRef nullable ? nullable.Inner : type)
            is IrNamedTypeRef { Semantics.EnumUnderlyingKind: { } underlying }
        && HoldsBigInts(underlying);

    private static string BaseType(IrEnumDeclaration ir) =>
        HoldsBigInts(ir.UnderlyingKind) ? "bigint" : "number";

    /// <summary>The value a numeric member prints: its exact digits, a <c>bigint</c> literal for a 64-bit enum.</summary>
    private static string NumericMemberValue(IrEnumDeclaration ir, object? value)
    {
        var digits = value?.ToString() ?? "";

        return HoldsBigInts(ir.UnderlyingKind) ? $"{digits}n" : digits;
    }
}
