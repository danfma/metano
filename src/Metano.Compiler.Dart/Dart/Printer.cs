using System.Text;
using Metano.Dart.AST;

namespace Metano.Dart;

/// <summary>
/// Renders a <see cref="DartSourceFile"/> to idiomatic Dart source code. The formatter
/// follows Dart style guidelines: 2-space indentation, trailing commas on multi-line
/// parameter lists, LF line endings, and a single trailing newline.
/// </summary>
public sealed class Printer
{
    private const string Indent = "  ";

    public string Print(DartSourceFile file)
    {
        var sb = new StringBuilder();

        // Imports first, separated by a blank line from declarations.
        var imports = file.Statements.OfType<DartImport>().ToList();
        var rest = file.Statements.Except(imports).ToList();

        foreach (var import in imports)
            PrintImport(sb, import);
        if (imports.Count > 0 && rest.Count > 0)
            sb.AppendLine();

        for (var i = 0; i < rest.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();
            PrintTopLevel(sb, rest[i]);
        }

        return sb.ToString();
    }

    // ── Top-level declarations ────────────────────────────────────────────

    private static void PrintTopLevel(StringBuilder sb, DartTopLevel stmt)
    {
        switch (stmt)
        {
            case DartClass cls:
                PrintClass(sb, cls);
                break;
            case DartEnum e:
                PrintEnum(sb, e);
                break;
            case DartFunction fn:
                PrintFunction(sb, fn);
                break;
        }
    }

    private static void PrintFunction(StringBuilder sb, DartFunction fn)
    {
        sb.Append(FormatType(fn.ReturnType)).Append(' ').Append(fn.Name).Append('(');
        AppendParameterList(sb, fn.Parameters);
        sb.Append(')');
        if (fn.IsAsync)
            sb.Append(" async");
        if (fn.Body is not null)
            IrBodyPrinter.PrintBody(sb, fn.Body, indentLevel: 0);
        else
            sb.AppendLine(" => throw UnimplementedError();");
    }

    /// <summary>
    /// Renders a parameter list into Dart's three parameter regions:
    /// required positional, <c>[…]</c> optional positional (with defaults),
    /// and <c>{…}</c> named (optionally marked <c>required</c>). Dart rejects
    /// mixing an unbracketed parameter with a default, and named + positional
    /// optional regions cannot coexist on the same callee, so the caller must
    /// not supply both shapes in the same list.
    /// </summary>
    private static void AppendParameterList(
        StringBuilder sb,
        IReadOnlyList<DartParameter> parameters
    )
    {
        var requiredPositional = parameters
            .Where(p => !p.IsNamed && p.DefaultValue is null)
            .ToList();
        var optionalPositional = parameters
            .Where(p => !p.IsNamed && p.DefaultValue is not null)
            .ToList();
        var named = parameters.Where(p => p.IsNamed).ToList();

        for (var i = 0; i < requiredPositional.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            AppendParameter(sb, requiredPositional[i]);
        }
        if (optionalPositional.Count > 0)
        {
            if (requiredPositional.Count > 0)
                sb.Append(", ");
            sb.Append('[');
            for (var i = 0; i < optionalPositional.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                AppendParameter(sb, optionalPositional[i]);
            }
            sb.Append(']');
        }
        if (named.Count > 0)
        {
            if (requiredPositional.Count > 0)
                sb.Append(", ");
            sb.Append('{');
            for (var i = 0; i < named.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                AppendParameter(sb, named[i]);
            }
            sb.Append('}');
        }
    }

    private static void AppendParameter(StringBuilder sb, DartParameter p)
    {
        if (p.IsNamed && p.IsRequired)
            sb.Append("required ");
        sb.Append(FormatType(p.Type)).Append(' ').Append(p.Name);
        if (p.DefaultValue is not null)
        {
            sb.Append(" = ");
            IrBodyPrinter.PrintExpression(sb, p.DefaultValue);
        }
    }

    private static void PrintImport(StringBuilder sb, DartImport import)
    {
        sb.Append("import '").Append(import.Path).Append('\'');
        if (import.ShowNames is { Count: > 0 })
            sb.Append(" show ").Append(string.Join(", ", import.ShowNames));
        if (!string.IsNullOrEmpty(import.Alias))
            sb.Append(" as ").Append(import.Alias);
        sb.AppendLine(";");
    }

    private static void PrintClass(StringBuilder sb, DartClass cls)
    {
        PrintClassHeader(sb, cls);
        sb.AppendLine(" {");

        // Constructor comes before members by Dart convention.
        if (cls.Constructor is not null)
        {
            PrintConstructor(sb, cls.Constructor);
            if (cls.Members is { Count: > 0 })
                sb.AppendLine();
        }

        if (cls.Members is not null)
        {
            for (var i = 0; i < cls.Members.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine();
                PrintClassMember(sb, cls.Members[i]);
            }
        }

        sb.AppendLine("}");
    }

    private static void PrintClassHeader(StringBuilder sb, DartClass cls)
    {
        var keyword = cls.Modifier switch
        {
            DartClassModifier.Abstract => "abstract class",
            DartClassModifier.AbstractInterface => "abstract interface class",
            DartClassModifier.Final => "final class",
            DartClassModifier.Sealed => "sealed class",
            DartClassModifier.Base => "base class",
            _ => "class",
        };
        sb.Append(keyword).Append(' ').Append(cls.Name);
        if (cls.TypeParameters is { Count: > 0 })
            sb.Append('<').Append(FormatTypeParams(cls.TypeParameters)).Append('>');
        if (cls.ExtendsType is not null)
            sb.Append(" extends ").Append(FormatType(cls.ExtendsType));
        if (cls.Implements is { Count: > 0 })
            sb.Append(" implements ").Append(string.Join(", ", cls.Implements.Select(FormatType)));
    }

    private static void PrintConstructor(StringBuilder sb, DartConstructor ctor)
    {
        sb.Append(Indent);
        if (ctor.IsConst)
            sb.Append("const ");
        sb.Append(ctor.ClassName).Append('(');

        // Dart splits ctor parameters into two regions: required positional, then an
        // optional positional block delimited by [...]. To preserve C# semantics
        // (`new T(a)` works when `b` has a default) we emit any parameters with a
        // default into the optional block at the tail. Required parameters keep
        // their original order; optional ones do the same within the bracketed tail.
        var required = ctor.Parameters.Where(p => p.IsRequired).ToList();
        var optional = ctor.Parameters.Where(p => !p.IsRequired).ToList();

        for (var i = 0; i < required.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            AppendConstructorParameter(sb, required[i]);
        }
        if (optional.Count > 0)
        {
            if (required.Count > 0)
                sb.Append(", ");
            sb.Append('[');
            for (var i = 0; i < optional.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                AppendConstructorParameter(sb, optional[i]);
            }
            sb.Append(']');
        }
        sb.Append(')');
        EmitCtorBodyOrSemicolon(sb, ctor.Body);
    }

    private static void AppendConstructorParameter(StringBuilder sb, DartConstructorParameter p)
    {
        if (p.IsFieldInitializer)
            sb.Append("this.").Append(p.Name);
        else
            sb.Append(FormatType(p.Type!)).Append(' ').Append(p.Name);
        if (p.DefaultValue is not null)
        {
            sb.Append(" = ");
            IrBodyPrinter.PrintExpression(sb, p.DefaultValue);
        }
    }

    /// <summary>
    /// When a constructor has no body, Dart expects <c>;</c>. When it has one, the
    /// <see cref="IrBodyPrinter"/> emits the braced block (or arrow for a single
    /// statement). Separator formatting mirrors the method printer.
    /// </summary>
    private static void EmitCtorBodyOrSemicolon(
        StringBuilder sb,
        IReadOnlyList<Metano.Compiler.IR.IrStatement>? body
    )
    {
        if (body is null || body.Count == 0)
            sb.AppendLine(";");
        else
            IrBodyPrinter.PrintBody(sb, body, indentLevel: 1);
    }

    private static void PrintClassMember(StringBuilder sb, DartClassMember member)
    {
        switch (member)
        {
            case DartField f:
                sb.Append(Indent);
                if (f.IsStatic)
                    sb.Append("static ");
                if (f.IsLate)
                    sb.Append("late ");
                if (f.IsFinal)
                    sb.Append("final ");
                sb.Append(FormatType(f.Type)).Append(' ').Append(f.Name);
                if (f.Initializer is not null)
                {
                    sb.Append(" = ");
                    IrBodyPrinter.PrintExpression(sb, f.Initializer);
                }
                sb.AppendLine(";");
                break;

            case DartGetter g:
                if (g.IsOverride)
                    sb.Append(Indent).AppendLine("@override");
                sb.Append(Indent);
                if (g.IsStatic)
                    sb.Append("static ");
                sb.Append(FormatType(g.ReturnType)).Append(" get ").Append(g.Name);
                if (g.IsAbstract)
                    sb.AppendLine(";");
                else if (g.Body is not null)
                    IrBodyPrinter.PrintBody(sb, g.Body, indentLevel: 1);
                else
                    sb.AppendLine(" => throw UnimplementedError();");
                break;

            case DartMethodSignature m:
                if (m.IsOverride)
                    sb.Append(Indent).AppendLine("@override");
                sb.Append(Indent);
                if (m.IsStatic)
                    sb.Append("static ");
                sb.Append(FormatType(m.ReturnType)).Append(' ');
                if (m.OperatorSymbol is not null)
                    sb.Append("operator ").Append(m.OperatorSymbol);
                else
                    sb.Append(m.Name);
                if (m.TypeParameters is { Count: > 0 })
                    sb.Append('<').Append(FormatTypeParams(m.TypeParameters)).Append('>');
                sb.Append('(');
                AppendParameterList(sb, m.Parameters);
                sb.Append(')');
                if (m.IsAsync)
                    sb.Append(" async");
                if (m.IsAbstract)
                    sb.AppendLine(";");
                else if (m.Body is not null)
                    IrBodyPrinter.PrintBody(sb, m.Body, indentLevel: 1);
                else
                    sb.AppendLine(" => throw UnimplementedError();");
                break;
        }
    }

    private static void PrintEnum(StringBuilder sb, DartEnum e)
    {
        sb.Append("enum ").Append(e.Name).AppendLine(" {");
        for (var i = 0; i < e.Values.Count; i++)
        {
            var v = e.Values[i];
            sb.Append(Indent).Append(v.Name);
            if (v.Value is not null)
                sb.Append('(').Append(v.Value).Append(')');
            if (i < e.Values.Count - 1)
                sb.AppendLine(",");
            else
                sb.AppendLine(";");
        }
        sb.AppendLine("}");
    }

    // ── Type rendering ────────────────────────────────────────────────────

    private static string FormatType(DartType type) => DartTypeFormatter.Format(type);

    private static string FormatTypeParams(IReadOnlyList<DartTypeParameter> tps) =>
        string.Join(
            ", ",
            tps.Select(tp =>
                tp.Extends is not null ? $"{tp.Name} extends {FormatType(tp.Extends)}" : tp.Name
            )
        );
}
