using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Converts an <see cref="IrEnumDeclaration"/> to TypeScript AST statements.
/// Two shapes are produced depending on <see cref="IrEnumDeclaration.Style"/>:
/// <list type="bullet">
///   <item><see cref="IrEnumStyle.Numeric"/> → a single <see cref="TsEnum"/>.</item>
///   <item><see cref="IrEnumStyle.String"/> → a <see cref="TsConstObject"/> plus a
///   companion <see cref="TsTypeAlias"/> that gives the string union type.</item>
/// </list>
/// </summary>
public static class IrToTsEnumBridge
{
    public static void Convert(IrEnumDeclaration ir, List<TsTopLevel> statements)
    {
        var tsName = IrToTsNamingPolicy.ToTypeName(ir.Name, ir.Attributes);

        if (ir.Style == IrEnumStyle.String)
        {
            var entries = ir
                .Members.Select(m =>
                    (
                        Key: m.Name,
                        Value: (TsExpression)
                            new TsStringLiteral(
                                IrToTsNamingPolicy.ToEnumMemberName(m.Name, m.Attributes)
                            )
                    )
                )
                .ToList();

            statements.Add(new TsConstObject(tsName, entries));
            statements.Add(
                new TsTypeAlias(tsName, new TsNamedType($"typeof {tsName}[keyof typeof {tsName}]"))
            );
        }
        else
        {
            var members = ir
                .Members.Select(m => new TsEnumMember(
                    IrToTsNamingPolicy.ToEnumMemberName(m.Name, m.Attributes),
                    new TsLiteral(NumericMemberValue(m.Value))
                ))
                .ToList();

            statements.Add(new TsEnum(tsName, members));
        }
    }

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
