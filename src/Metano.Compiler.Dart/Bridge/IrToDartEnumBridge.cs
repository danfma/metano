using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

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
                // String enums prefer a Dart-specific [Name(Dart, "…")] override
                // first, then the positional [Name("…")] (which the extractor
                // already baked into m.Value), and finally the source name.
                // m.Value alone would silently drop per-target overrides.
                var rawValue =
                    IrToDartNamingPolicy.FindNameOverride(m.Attributes)
                    ?? (m.Value is string s ? s : m.Name);
                return new DartEnumValue(memberName, $"'{EscapeDartString(rawValue)}'");
            })
            .ToList();

        statements.Add(new DartEnum(name, values));
    }

    private static string EscapeDartString(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
