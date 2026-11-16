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

        var values = ir.Members.Select(member => ToValue(ir, member)).ToList();

        statements.Add(new DartEnum(name, values));
    }

    /// <summary>
    /// A numeric enum member has no constructor argument and takes the
    /// <c>[Name]</c> override as its identifier. A string enum member keeps
    /// its C# name as the identifier (a <c>[Name]</c> value such as
    /// <c>in-progress</c> is not one) and carries the string value
    /// (<c>Color.red('red')</c>), which a Dart-specific <c>[Name(Dart, "…")]</c>
    /// override takes first, then the positional <c>[Name("…")]</c> the
    /// extractor already baked into <c>m.Value</c>, and finally the source
    /// name.
    /// </summary>
    private static DartEnumValue ToValue(IrEnumDeclaration ir, IrEnumMember member)
    {
        if (ir.Style == IrEnumStyle.Numeric)
            return new DartEnumValue(
                IrToDartNamingPolicy.ToMemberName(member.Name, member.Attributes)
            );

        var rawValue = IrToDartNamingPolicy.FindNameOverride(member.Attributes)
            ?? (member.Value is string s ? s : member.Name);

        return new DartEnumValue(
            IrToDartNamingPolicy.ToMemberName(member.Name, attributes: null),
            $"'{DartStringLiteral.Escape(rawValue)}'"
        );
    }
}
