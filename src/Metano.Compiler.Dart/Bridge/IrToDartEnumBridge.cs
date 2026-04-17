using Metano.Compiler.IR;
using Metano.Dart.AST;

namespace Metano.Dart.Bridge;

/// <summary>
/// Converts an <see cref="IrEnumDeclaration"/> into a Dart <see cref="DartEnum"/>.
/// String-valued enums use Dart's enhanced-enum syntax so each member carries its
/// string value (<c>Color.red("red")</c>).
/// </summary>
public static class IrToDartEnumBridge
{
    public static void Convert(IrEnumDeclaration ir, List<DartTopLevel> statements)
    {
        var name = IrToDartNamingPolicy.ToTypeName(ir.Name, ir.Attributes);
        var values = ir
            .Members.Select(m =>
            {
                var memberName = IrToDartNamingPolicy.ToMemberName(m.Name, m.Attributes);
                // Plain numeric enums have no per-member constructor args.
                if (ir.Style == IrEnumStyle.Numeric)
                    return new DartEnumValue(memberName);
                // String enums preserve the source value verbatim.
                var stringValue = m.Value is string s ? $"'{EscapeDartString(s)}'" : "''";
                return new DartEnumValue(memberName, stringValue);
            })
            .ToList();

        statements.Add(new DartEnum(name, values));
    }

    private static string EscapeDartString(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
