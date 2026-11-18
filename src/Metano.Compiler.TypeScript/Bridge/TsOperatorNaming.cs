using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript names of user-defined operators and conversions (baseline
/// FR-078), in one place for the class emitter, the claim pass and the
/// companion namespace of a branded type: an operator's static helper is
/// <c>__</c> + its name (<c>__add</c>, <c>__negate</c>, <c>__checkedAdd</c>)
/// and its instance form <c>$</c> + its name; a conversion's static is
/// <c>__</c> + its core candidate, camelCased (<c>__toInt32</c>,
/// <c>__fromInt32OrNull</c>, <c>__checkedToInt32</c>).
/// </summary>
internal static class TsOperatorNaming
{
    private const string StaticPrefix = "__";
    private const string InstancePrefix = "$";
    private const string CheckedNamePrefix = "checked";

    /// <summary>
    /// The name an operator lowers to (<c>add</c>, <c>negate</c>): a single
    /// operator's <c>[Name]</c> override, else the name of its operator kind;
    /// <c>null</c> for a kind with no name (a conversion, <c>true</c> /
    /// <c>false</c>), which declares no operator helper. An overloaded
    /// operator group keeps its kind's name, which its fast paths extend: a
    /// <c>[Name]</c> on one of its overloads pins that overload's suffix (spec
    /// 013 FR-005).
    /// </summary>
    public static string? OperatorNameOf(IrMethodDeclaration op)
    {
        if (op.Semantics.OperatorKind is not { } kind || KindNameOf(kind) is not { } kindName)
            return null;

        return IrToTsNamingPolicy.FindMemberNameOverride(op.Attributes, op.OverloadName)
            ?? kindName;
    }

    /// <summary>The static helper of an operator: <c>__</c> + its name.</summary>
    public static string StaticHelperName(string operatorName) => StaticPrefix + operatorName;

    /// <summary>The instance form of an operator: <c>$</c> + its name.</summary>
    public static string InstanceHelperName(string operatorName) => InstancePrefix + operatorName;

    /// <summary>The static of a conversion: <c>__</c> + its candidate, camelCased.</summary>
    public static string ConversionHelperName(IrConversionName name) =>
        StaticPrefix + TypeScriptNaming.ToCamelCaseMember(name.Candidate);

    /// <summary>
    /// The name of a C# operator kind (<c>"Addition"</c> → <c>add</c>,
    /// <c>"UnaryNegation"</c> → <c>negate</c>); a <c>checked</c> operator
    /// takes <c>checked</c> + its unchecked form's name, capitalized
    /// (<c>"CheckedAddition"</c> → <c>checkedAdd</c>). <c>null</c> for a kind
    /// with no helper.
    /// </summary>
    public static string? KindNameOf(IrOperatorKind kind) =>
        kind.IsChecked && UncheckedKindNameOf(kind.Unchecked.Name) is { } uncheckedName
            ? CheckedNamePrefix + char.ToUpperInvariant(uncheckedName[0]) + uncheckedName[1..]
            : UncheckedKindNameOf(kind.Name);

    private static string? UncheckedKindNameOf(string kind) =>
        kind switch
        {
            "Addition" => "add",
            "Subtraction" => "subtract",
            "Multiply" => "multiply",
            "Division" => "divide",
            "Modulus" => "modulo",
            "Equality" => "equals",
            "Inequality" => "notEquals",
            "LessThan" => "lessThan",
            "GreaterThan" => "greaterThan",
            "LessThanOrEqual" => "lessThanOrEqual",
            "GreaterThanOrEqual" => "greaterThanOrEqual",
            "LogicalNot" => "not",
            "OnesComplement" => "bitwiseNot",
            "BitwiseAnd" => "bitwiseAnd",
            "BitwiseOr" => "bitwiseOr",
            "ExclusiveOr" => "xor",
            "LeftShift" => "shiftLeft",
            "RightShift" => "shiftRight",
            "UnsignedRightShift" => "shiftRightUnsigned",
            "UnaryNegation" => "negate",
            "UnaryPlus" => "plus",
            "Increment" => "increment",
            "Decrement" => "decrement",
            _ => null,
        };
}
