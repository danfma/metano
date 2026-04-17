using System.Globalization;
using System.Text;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Dart.Bridge;

namespace Metano.Dart;

/// <summary>
/// Renders IR statements and expressions directly as Dart source, appending into a
/// <see cref="StringBuilder"/>. Keeps the output idiomatic for the supported subset
/// (see <c>IrExpressionExtractor</c>/<c>IrStatementExtractor</c>).
/// <para>
/// Unsupported IR nodes produce a visible comment so the resulting Dart stays
/// syntactically valid and reviewers can spot gaps.
/// </para>
/// </summary>
public static class IrBodyPrinter
{
    public static void PrintBody(StringBuilder sb, IReadOnlyList<IrStatement> body, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        // Expression-bodied optimization: single return of an expression → `=> expr;`
        if (body.Count == 1 && body[0] is IrReturnStatement { Value: { } returned })
        {
            sb.Append(" => ");
            PrintExpression(sb, returned);
            sb.AppendLine(";");
            return;
        }
        if (body.Count == 1 && body[0] is IrExpressionStatement singleExpr)
        {
            sb.Append(" => ");
            PrintExpression(sb, singleExpr.Expression);
            sb.AppendLine(";");
            return;
        }

        sb.AppendLine(" {");
        foreach (var stmt in body)
            PrintStatement(sb, stmt, indentLevel + 1);
        sb.Append(indent).AppendLine("}");
    }

    // ── Statements ────────────────────────────────────────────────────────

    private static void PrintStatement(StringBuilder sb, IrStatement stmt, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        switch (stmt)
        {
            case IrReturnStatement ret:
                sb.Append(indent).Append("return");
                if (ret.Value is not null)
                {
                    sb.Append(' ');
                    PrintExpression(sb, ret.Value);
                }
                sb.AppendLine(";");
                break;

            case IrExpressionStatement es:
                sb.Append(indent);
                PrintExpression(sb, es.Expression);
                sb.AppendLine(";");
                break;

            case IrVariableDeclaration vd:
                sb.Append(indent);
                if (vd.Type is not null)
                    sb.Append(FormatType(vd.Type)).Append(' ');
                else
                    sb.Append("var ");
                sb.Append(vd.Name);
                if (vd.Initializer is not null)
                {
                    sb.Append(" = ");
                    PrintExpression(sb, vd.Initializer);
                }
                sb.AppendLine(";");
                break;

            case IrIfStatement ifs:
                sb.Append(indent).Append("if (");
                PrintExpression(sb, ifs.Condition);
                sb.AppendLine(") {");
                foreach (var s in ifs.Then)
                    PrintStatement(sb, s, indentLevel + 1);
                sb.Append(indent).Append('}');
                if (ifs.Else is not null)
                {
                    sb.AppendLine(" else {");
                    foreach (var s in ifs.Else)
                        PrintStatement(sb, s, indentLevel + 1);
                    sb.Append(indent).AppendLine("}");
                }
                else
                {
                    sb.AppendLine();
                }
                break;

            case IrBlockStatement block:
                sb.Append(indent).AppendLine("{");
                foreach (var s in block.Statements)
                    PrintStatement(sb, s, indentLevel + 1);
                sb.Append(indent).AppendLine("}");
                break;

            case IrThrowStatement th:
                sb.Append(indent).Append("throw ");
                PrintExpression(sb, th.Expression);
                sb.AppendLine(";");
                break;

            case IrBreakStatement:
                sb.Append(indent).AppendLine("break;");
                break;

            case IrContinueStatement:
                sb.Append(indent).AppendLine("continue;");
                break;

            case IrForEachStatement fe:
                sb.Append(indent).Append("for (");
                if (fe.VariableType is not null)
                    sb.Append(FormatType(fe.VariableType)).Append(' ');
                else
                    sb.Append("var ");
                sb.Append(fe.Variable).Append(" in ");
                PrintExpression(sb, fe.Collection);
                sb.AppendLine(") {");
                foreach (var s in fe.Body)
                    PrintStatement(sb, s, indentLevel + 1);
                sb.Append(indent).AppendLine("}");
                break;

            case IrWhileStatement ws:
                sb.Append(indent).Append("while (");
                PrintExpression(sb, ws.Condition);
                sb.AppendLine(") {");
                foreach (var s in ws.Body)
                    PrintStatement(sb, s, indentLevel + 1);
                sb.Append(indent).AppendLine("}");
                break;

            case IrDoWhileStatement dw:
                sb.Append(indent).AppendLine("do {");
                foreach (var s in dw.Body)
                    PrintStatement(sb, s, indentLevel + 1);
                sb.Append(indent).Append("} while (");
                PrintExpression(sb, dw.Condition);
                sb.AppendLine(");");
                break;

            case IrTryStatement ts:
                sb.Append(indent).AppendLine("try {");
                foreach (var s in ts.Body)
                    PrintStatement(sb, s, indentLevel + 1);
                sb.Append(indent).Append('}');
                if (ts.Catches is not null)
                {
                    foreach (var c in ts.Catches)
                    {
                        if (c.ExceptionType is not null)
                        {
                            sb.Append(" on ").Append(FormatType(c.ExceptionType));
                        }
                        sb.Append(" catch (").Append(c.VariableName ?? "e").AppendLine(") {");
                        foreach (var s in c.Body)
                            PrintStatement(sb, s, indentLevel + 1);
                        sb.Append(indent).Append('}');
                    }
                }
                if (ts.Finally is not null)
                {
                    sb.AppendLine(" finally {");
                    foreach (var s in ts.Finally)
                        PrintStatement(sb, s, indentLevel + 1);
                    sb.Append(indent).AppendLine("}");
                }
                else
                {
                    sb.AppendLine();
                }
                break;

            case IrSwitchStatement sw:
                sb.Append(indent).Append("switch (");
                PrintExpression(sb, sw.Expression);
                sb.AppendLine(") {");
                foreach (var c in sw.Cases)
                {
                    if (c.Labels.Count == 0)
                    {
                        sb.Append(indent).Append("  default:").AppendLine();
                    }
                    else
                    {
                        foreach (var label in c.Labels)
                        {
                            sb.Append(indent).Append("  case ");
                            PrintExpression(sb, label);
                            sb.AppendLine(":");
                        }
                    }
                    foreach (var s in c.Body)
                        PrintStatement(sb, s, indentLevel + 2);
                }
                sb.Append(indent).AppendLine("}");
                break;

            default:
                sb.Append(indent)
                    .Append("// TODO: unsupported IR statement ")
                    .AppendLine(stmt.GetType().Name);
                break;
        }
    }

    // ── Expressions ───────────────────────────────────────────────────────

    public static void PrintExpression(StringBuilder sb, IrExpression expr)
    {
        switch (expr)
        {
            case IrLiteral lit:
                PrintLiteral(sb, lit);
                break;
            case IrIdentifier id:
                sb.Append(IrToDartNamingPolicy.ToParameterName(id.Name));
                break;
            case IrTypeReference tr:
                // Type references keep their source casing — Dart classes and enums are
                // PascalCase and should surface as written (e.g., `Counter.zero`).
                sb.Append(tr.Name);
                break;
            case IrThisExpression:
                sb.Append("this");
                break;
            case IrBaseExpression:
                sb.Append("super");
                break;
            case IrMemberAccess ma:
                PrintExpression(sb, ma.Target);
                sb.Append('.').Append(IrToDartNamingPolicy.ToParameterName(ma.MemberName));
                break;
            case IrElementAccess ea:
                PrintExpression(sb, ea.Target);
                sb.Append('[');
                PrintExpression(sb, ea.Index);
                sb.Append(']');
                break;
            case IrCallExpression call:
                PrintExpression(sb, call.Target);
                sb.Append('(');
                PrintArgs(sb, call.Arguments);
                sb.Append(')');
                break;
            case IrNewExpression ne:
                // Dart has no `new` keyword (optional since Dart 2). Constructor call
                // with positional args matches the common idiom for classes with
                // `this.field` field initializers.
                sb.Append(FormatType(ne.Type)).Append('(');
                PrintArgs(sb, ne.Arguments);
                sb.Append(')');
                break;
            case IrBinaryExpression bin:
                PrintExpression(sb, bin.Left);
                sb.Append(' ').Append(FormatBinaryOp(bin.Operator)).Append(' ');
                PrintExpression(sb, bin.Right);
                break;
            case IrUnaryExpression un:
                if (un.IsPrefix)
                {
                    sb.Append(FormatUnaryOp(un.Operator));
                    PrintExpression(sb, un.Operand);
                }
                else
                {
                    PrintExpression(sb, un.Operand);
                    sb.Append(FormatUnaryOp(un.Operator));
                }
                break;
            case IrConditionalExpression cond:
                PrintExpression(sb, cond.Condition);
                sb.Append(" ? ");
                PrintExpression(sb, cond.WhenTrue);
                sb.Append(" : ");
                PrintExpression(sb, cond.WhenFalse);
                break;
            case IrAwaitExpression aw:
                sb.Append("await ");
                PrintExpression(sb, aw.Expression);
                break;
            case IrThrowExpression th:
                sb.Append("throw ");
                PrintExpression(sb, th.Expression);
                break;
            case IrCastExpression cast:
                // Dart uses `expr as Type` for downcasts.
                PrintExpression(sb, cast.Expression);
                sb.Append(" as ").Append(FormatType(cast.TargetType));
                break;
            case IrLambdaExpression lambda:
                PrintLambda(sb, lambda);
                break;
            case IrStringInterpolation interp:
                PrintStringInterpolation(sb, interp);
                break;
            case IrIsPatternExpression isPattern:
                PrintIsPattern(sb, isPattern);
                break;
            case IrSwitchExpression switchExpr:
                PrintSwitchExpression(sb, switchExpr);
                break;
            case IrWithExpression withExpr:
                // C# `source with { X = e }` → Dart `source.copyWith(x: e)`
                // (the synthesized copyWith method is already emitted on
                // every record class by IrToDartClassBridge).
                PrintExpression(sb, withExpr.Source);
                sb.Append(".copyWith(");
                for (var i = 0; i < withExpr.Assignments.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    var a = withExpr.Assignments[i];
                    sb.Append(IrToDartNamingPolicy.ToParameterName(a.MemberName)).Append(": ");
                    PrintExpression(sb, a.Value);
                }
                sb.Append(')');
                break;
            case IrArrayLiteral array:
                sb.Append('[');
                for (var i = 0; i < array.Elements.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    PrintExpression(sb, array.Elements[i]);
                }
                sb.Append(']');
                break;
            case IrTemplateExpression tpl:
                // `[Emit("…")]` templates are defined against JavaScript
                // syntax. Dart can't reproduce them, so we emit a TODO
                // marker — real usage should gate [Emit] methods with
                // `[Ignore(TargetLanguage.Dart)]` or provide a Dart-
                // specific implementation.
                sb.Append("/* TODO: [Emit] template ‘")
                    .Append(tpl.Template)
                    .Append("’ has no Dart mapping */ null");
                break;
            case IrUnsupportedExpression u:
                sb.Append("/* TODO: unsupported IR expression ").Append(u.Kind).Append(" */");
                break;
            default:
                sb.Append("/* TODO: ").Append(expr.GetType().Name).Append(" */");
                break;
        }
    }

    /// <summary>
    /// Dart renders <c>x is Foo</c> natively. A type-with-designator (<c>x is Foo f</c>)
    /// needs the Dart 3 switch-expression form for a binding — outside an expression
    /// context we emit a TODO so callers see the gap instead of producing wrong code.
    /// Constant patterns become an equality check; discard patterns collapse to
    /// <c>true</c>.
    /// </summary>
    private static void PrintIsPattern(StringBuilder sb, IrIsPatternExpression node)
    {
        switch (node.Pattern)
        {
            case IrConstantPattern constant:
                PrintExpression(sb, node.Expression);
                sb.Append(" == ");
                PrintExpression(sb, constant.Value);
                break;
            case IrTypePattern typePat when typePat.DesignatorName is null:
                PrintExpression(sb, node.Expression);
                sb.Append(" is ").Append(FormatType(typePat.Type));
                break;
            case IrDiscardPattern:
                sb.Append("true");
                break;
            case IrPropertyPattern prop when prop.DesignatorName is null:
                // Dart lifts property patterns into the same `is X && a.b == …`
                // conjunction the TS bridge emits, because `is` isn't itself
                // pattern-aware in expression contexts — it's the `switch` arm
                // that hosts the pattern-matching syntax natively.
                PrintPropertyPatternAsBooleanExpression(sb, node.Expression, prop);
                break;
            case IrRelationalPattern or IrLogicalPattern:
                PrintSubPatternAsBooleanExpression(sb, node.Expression, node.Pattern);
                break;
            default:
                sb.Append("/* TODO: unsupported is-pattern ")
                    .Append(node.Pattern.GetType().Name)
                    .Append(" */");
                break;
        }
    }

    private static void PrintPropertyPatternAsBooleanExpression(
        StringBuilder sb,
        IrExpression value,
        IrPropertyPattern pattern
    )
    {
        var wroteAny = false;
        if (pattern.Type is not null)
        {
            PrintExpression(sb, value);
            sb.Append(" is ").Append(FormatType(pattern.Type));
            wroteAny = true;
        }
        foreach (var sub in pattern.Subpatterns)
        {
            if (wroteAny)
                sb.Append(" && ");
            // The member name stays in its original C# casing; `IrMemberAccess`
            // is rendered via the body printer which in turn calls the naming
            // policy, so we don't camelCase it twice here.
            var member = new IrMemberAccess(value, sub.MemberName);
            PrintSubPatternAsBooleanExpression(sb, member, sub.Pattern);
            wroteAny = true;
        }
        if (!wroteAny)
            sb.Append("true");
    }

    private static void PrintSubPatternAsBooleanExpression(
        StringBuilder sb,
        IrExpression value,
        IrPattern pattern
    )
    {
        switch (pattern)
        {
            case IrConstantPattern constant:
                PrintExpression(sb, value);
                sb.Append(" == ");
                PrintExpression(sb, constant.Value);
                break;
            case IrTypePattern typePat when typePat.DesignatorName is null:
                PrintExpression(sb, value);
                sb.Append(" is ").Append(FormatType(typePat.Type));
                break;
            case IrDiscardPattern or IrVarPattern:
                sb.Append("true");
                break;
            case IrPropertyPattern prop when prop.DesignatorName is null:
                PrintPropertyPatternAsBooleanExpression(sb, value, prop);
                break;
            case IrRelationalPattern rel:
                PrintExpression(sb, value);
                sb.Append(' ').Append(RelationalOpToken(rel.Operator)).Append(' ');
                PrintExpression(sb, rel.Value);
                break;
            case IrLogicalPattern { Operator: IrLogicalOp.Not } notPat:
                sb.Append("!(");
                PrintSubPatternAsBooleanExpression(sb, value, notPat.Left);
                sb.Append(')');
                break;
            case IrLogicalPattern log:
                PrintSubPatternAsBooleanExpression(sb, value, log.Left);
                sb.Append(log.Operator is IrLogicalOp.And ? " && " : " || ");
                PrintSubPatternAsBooleanExpression(sb, value, log.Right!);
                break;
            case IrListPattern:
            case IrPositionalPattern:
                // List / positional patterns in an `is` expression need a
                // destructuring binding to reach the sub-pattern values,
                // which Dart only offers inside switch arms — emit a TODO.
                sb.Append("/* TODO: Dart list/positional pattern outside switch */ false");
                break;
            default:
                sb.Append("/* TODO: unsupported pattern ")
                    .Append(pattern.GetType().Name)
                    .Append(" */");
                break;
        }
    }

    private static string RelationalOpToken(IrRelationalOp op) =>
        op switch
        {
            IrRelationalOp.LessThan => "<",
            IrRelationalOp.LessThanOrEqual => "<=",
            IrRelationalOp.GreaterThan => ">",
            IrRelationalOp.GreaterThanOrEqual => ">=",
            _ => "<",
        };

    /// <summary>
    /// Dart 3 has native switch expressions with the same shape as C#'s. Each
    /// arm lowers to <c>pattern => result</c>, with an optional <c>when guard</c>
    /// after the pattern. The rendering mirrors C# semantics — there is no IIFE
    /// wrapping needed here because Dart's switch itself is already an
    /// expression.
    /// </summary>
    private static void PrintSwitchExpression(StringBuilder sb, IrSwitchExpression sw)
    {
        sb.Append("switch (");
        PrintExpression(sb, sw.Scrutinee);
        sb.Append(") { ");
        for (var i = 0; i < sw.Arms.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            var arm = sw.Arms[i];
            PrintSwitchPattern(sb, arm.Pattern);
            if (arm.WhenClause is not null)
            {
                sb.Append(" when ");
                PrintExpression(sb, arm.WhenClause);
            }
            sb.Append(" => ");
            PrintExpression(sb, arm.Result);
        }
        sb.Append(" }");
    }

    /// <summary>
    /// Renders the `...` rest element inside a Dart list pattern. When the
    /// slice carries an inner pattern (<c>.. var tail</c> / <c>.. [0, _]</c>),
    /// Dart 3 spells it as `...tail` for a var-binding or `...pattern` for an
    /// inline pattern; a bare `..` becomes `...`.
    /// </summary>
    private static void PrintSliceToken(StringBuilder sb, IrPattern? slicePattern)
    {
        sb.Append("...");
        if (slicePattern is null)
            return;
        if (slicePattern is IrVarPattern varPat)
            sb.Append(varPat.Name);
        else
            PrintSwitchPattern(sb, slicePattern);
    }

    private static void PrintSwitchPattern(StringBuilder sb, IrPattern pattern)
    {
        switch (pattern)
        {
            case IrConstantPattern constant:
                PrintExpression(sb, constant.Value);
                break;
            case IrTypePattern typePat:
                sb.Append(FormatType(typePat.Type));
                if (typePat.DesignatorName is not null)
                    sb.Append(' ').Append(typePat.DesignatorName);
                break;
            case IrVarPattern varPat:
                sb.Append("var ").Append(varPat.Name);
                break;
            case IrDiscardPattern:
                sb.Append('_');
                break;
            case IrPropertyPattern prop:
                if (prop.Type is not null)
                    sb.Append(FormatType(prop.Type));
                sb.Append('(');
                for (var i = 0; i < prop.Subpatterns.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    // Dart 3 object pattern subpatterns: `getterName: pattern`.
                    sb.Append(IrToDartNamingPolicy.ToParameterName(prop.Subpatterns[i].MemberName))
                        .Append(": ");
                    PrintSwitchPattern(sb, prop.Subpatterns[i].Pattern);
                }
                sb.Append(')');
                if (prop.DesignatorName is not null)
                    sb.Append(' ').Append(prop.DesignatorName);
                break;
            case IrRelationalPattern rel:
                // Dart 3 relational patterns match C#'s shape syntactically:
                // `> 0`, `<= 10`, etc.
                sb.Append(RelationalOpToken(rel.Operator)).Append(' ');
                PrintExpression(sb, rel.Value);
                break;
            case IrLogicalPattern { Operator: IrLogicalOp.Not } notPat:
                // Dart doesn't have a unary `not` pattern; the idiomatic form
                // uses a when-guard instead. At the pattern level we fall back
                // to `_ when !(<inner as boolean expression>)` by emitting a
                // discard and letting the caller attach the when clause — but
                // since we're building the pattern itself here, we route
                // through a best-effort wildcard + negated test by lowering to
                // a `_` pattern with a TODO marker for callers that should
                // rewrite into a when-guard.
                sb.Append("/* TODO: Dart lacks `not` patterns — rewrite as when-guard */ _");
                break;
            case IrLogicalPattern log:
                PrintSwitchPattern(sb, log.Left);
                sb.Append(log.Operator is IrLogicalOp.And ? " && " : " || ");
                PrintSwitchPattern(sb, log.Right!);
                break;
            case IrListPattern list:
                sb.Append('[');
                for (var i = 0; i < list.Elements.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    if (list.SliceIndex == i)
                    {
                        PrintSliceToken(sb, list.SlicePattern);
                        sb.Append(", ");
                    }
                    PrintSwitchPattern(sb, list.Elements[i]);
                }
                if (list.SliceIndex == list.Elements.Count)
                {
                    if (list.Elements.Count > 0)
                        sb.Append(", ");
                    PrintSliceToken(sb, list.SlicePattern);
                }
                sb.Append(']');
                break;
            case IrPositionalPattern pos:
                if (pos.Type is not null)
                    sb.Append(FormatType(pos.Type));
                sb.Append('(');
                for (var i = 0; i < pos.Elements.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    PrintSwitchPattern(sb, pos.Elements[i]);
                }
                sb.Append(')');
                if (pos.DesignatorName is not null)
                    sb.Append(' ').Append(pos.DesignatorName);
                break;
            default:
                sb.Append("/* TODO: unsupported pattern ")
                    .Append(pattern.GetType().Name)
                    .Append(" */");
                break;
        }
    }

    private static void PrintLambda(StringBuilder sb, IrLambdaExpression lambda)
    {
        sb.Append('(');
        for (var i = 0; i < lambda.Parameters.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            // Dart allows implicit-type parameters in lambda literals — drop the
            // type when the extractor couldn't resolve it (IrUnknownTypeRef) so we
            // don't emit the unresolved marker into the source.
            var param = lambda.Parameters[i];
            if (param.Type is not IrUnknownTypeRef)
                sb.Append(FormatType(param.Type)).Append(' ');
            sb.Append(IrToDartNamingPolicy.ToParameterName(param.Name));
        }
        sb.Append(") ");
        if (lambda.IsAsync)
            sb.Append("async ");
        // Single-return bodies collapse to arrow form; otherwise we emit a block
        // with the same indentation hint the method printer uses.
        if (lambda.Body is [IrReturnStatement { Value: { } returned }])
        {
            sb.Append("=> ");
            PrintExpression(sb, returned);
        }
        else
        {
            sb.AppendLine("{");
            foreach (var stmt in lambda.Body)
                PrintStatement(sb, stmt, indentLevel: 2);
            sb.Append("  }");
        }
    }

    private static void PrintStringInterpolation(StringBuilder sb, IrStringInterpolation interp)
    {
        // Dart single-quoted strings with ${expr} interpolation. Expressions that are
        // a bare identifier can use the shorthand $name, but the general-purpose
        // `${expr}` form always works — stick with it for consistency and to avoid
        // ambiguity when the expression is a member access or call.
        sb.Append('\'');
        foreach (var part in interp.Parts)
        {
            switch (part)
            {
                case IrInterpolationText text:
                    sb.Append(EscapeInterpolationText(text.Text));
                    break;
                case IrInterpolationExpression expr:
                    sb.Append("${");
                    PrintExpression(sb, expr.Expression);
                    sb.Append('}');
                    break;
            }
        }
        sb.Append('\'');
    }

    private static string EscapeInterpolationText(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("$", "\\$");

    /// <summary>
    /// Dart supports named arguments natively (<c>foo(x, priority: p)</c>),
    /// so when an <see cref="IrArgument"/> carries a source-side name we
    /// render it verbatim. Positional arguments (no name) lower to the
    /// value only.
    /// </summary>
    private static void PrintArgs(StringBuilder sb, IReadOnlyList<IrArgument> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            var arg = args[i];
            if (arg.Name is not null)
                sb.Append(IrToDartNamingPolicy.ToParameterName(arg.Name)).Append(": ");
            PrintExpression(sb, arg.Value);
        }
    }

    private static void PrintLiteral(StringBuilder sb, IrLiteral lit)
    {
        switch (lit.Kind)
        {
            case IrLiteralKind.Null:
                sb.Append("null");
                break;
            case IrLiteralKind.Boolean:
                sb.Append((bool)lit.Value! ? "true" : "false");
                break;
            case IrLiteralKind.String:
                sb.Append('\'').Append(EscapeString((string)lit.Value!)).Append('\'');
                break;
            case IrLiteralKind.Char:
                sb.Append('\'').Append(lit.Value).Append('\'');
                break;
            case IrLiteralKind.Default:
                sb.Append("/* default */ null");
                break;
            default:
                sb.Append(
                    lit.Value is IFormattable f
                        ? f.ToString(null, CultureInfo.InvariantCulture)
                        : lit.Value?.ToString() ?? "null"
                );
                break;
        }
    }

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string FormatBinaryOp(IrBinaryOp op) =>
        op switch
        {
            IrBinaryOp.Add => "+",
            IrBinaryOp.Subtract => "-",
            IrBinaryOp.Multiply => "*",
            IrBinaryOp.Divide => "/",
            IrBinaryOp.Modulo => "%",
            IrBinaryOp.Equal => "==",
            IrBinaryOp.NotEqual => "!=",
            IrBinaryOp.LessThan => "<",
            IrBinaryOp.LessThanOrEqual => "<=",
            IrBinaryOp.GreaterThan => ">",
            IrBinaryOp.GreaterThanOrEqual => ">=",
            IrBinaryOp.LogicalAnd => "&&",
            IrBinaryOp.LogicalOr => "||",
            IrBinaryOp.BitwiseAnd => "&",
            IrBinaryOp.BitwiseOr => "|",
            IrBinaryOp.BitwiseXor => "^",
            IrBinaryOp.LeftShift => "<<",
            IrBinaryOp.RightShift => ">>",
            IrBinaryOp.UnsignedRightShift => ">>>",
            IrBinaryOp.NullCoalescing => "??",
            IrBinaryOp.Assign => "=",
            IrBinaryOp.AddAssign => "+=",
            IrBinaryOp.SubtractAssign => "-=",
            IrBinaryOp.MultiplyAssign => "*=",
            IrBinaryOp.DivideAssign => "/=",
            IrBinaryOp.ModuloAssign => "%=",
            IrBinaryOp.BitwiseAndAssign => "&=",
            IrBinaryOp.BitwiseOrAssign => "|=",
            IrBinaryOp.BitwiseXorAssign => "^=",
            IrBinaryOp.LeftShiftAssign => "<<=",
            IrBinaryOp.RightShiftAssign => ">>=",
            IrBinaryOp.NullCoalescingAssign => "??=",
            _ => "/* ?? */",
        };

    private static string FormatUnaryOp(IrUnaryOp op) =>
        op switch
        {
            IrUnaryOp.Negate => "-",
            IrUnaryOp.LogicalNot => "!",
            IrUnaryOp.BitwiseNot => "~",
            IrUnaryOp.Increment => "++",
            IrUnaryOp.Decrement => "--",
            _ => "",
        };

    private static string FormatType(IrTypeRef type) =>
        DartTypeFormatter.Format(IrToDartTypeMapper.Map(type));
}
