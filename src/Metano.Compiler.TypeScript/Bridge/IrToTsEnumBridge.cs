using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

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
/// </summary>
public static class IrToTsEnumBridge
{
    public static void Convert(IrEnumDeclaration ir, List<TsTopLevel> statements)
    {
        var tsName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);

        var entries = ir
            .Members.Select(member => (Key: MemberKey(ir, member), Value: MemberValue(ir, member)))
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
            : new TsLiteral(NumericMemberValue(member.Value));

    /// <summary>
    /// The one rule for an enum's alias: a numeric <c>[Flags]</c> enum is the
    /// brand, every other enum the closed union of its values (which
    /// <see cref="EnumValueAssertion"/> asserts computed values into).
    /// </summary>
    internal static bool HasFlagsBrand(bool isNumeric, bool isFlags) => isNumeric && isFlags;

    private static TsType AliasType(IrEnumDeclaration ir, string tsName) =>
        HasFlagsBrand(ir.Style == IrEnumStyle.Numeric, ir.IsFlags)
            ? new TsNamedType($"number & {{ readonly __enum?: \"{tsName}\" }}")
            : new TsNamedType($"typeof {tsName}[keyof typeof {tsName}]");

    /// <summary>
    /// The value a numeric member prints. A 64-bit value prints as the double
    /// the member actually holds, the one <c>MS0064</c> names (spec 012
    /// FR-058).
    /// </summary>
    private static string NumericMemberValue(object? value)
    {
        if (value is not (long or ulong))
            return value?.ToString() ?? "";

        var nearest = Int64EnumPrecision.NearestDouble(Int64EnumPrecision.ToBigInteger(value));

        return nearest.ToString(CultureInfo.InvariantCulture);
    }
}
