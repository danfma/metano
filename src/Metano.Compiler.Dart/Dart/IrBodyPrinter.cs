using System.Globalization;
using System.Text;
using Metano.Compiler.Analysis;
using Metano.Compiler.Dart.Bridge;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart;

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
    /// <param name="allowArrow">Whether a single-statement body may print as
    /// <c>=&gt; expr;</c>. A generative constructor passes <c>false</c>: Dart
    /// rejects an arrow body there (<c>return_in_generative_constructor</c>).</param>
    public static void PrintBody(
        StringBuilder sb,
        IReadOnlyList<IrStatement> body,
        int indentLevel,
        bool allowArrow = true
    )
    {
        var indent = new string(' ', indentLevel * 2);
        // Expression-bodied optimization: single return of an expression → `=> expr;`
        if (allowArrow && body.Count == 1 && body[0] is IrReturnStatement { Value: { } returned })
        {
            sb.Append(" => ");
            PrintExpression(sb, returned);
            sb.AppendLine(";");
            return;
        }
        if (allowArrow && body.Count == 1 && body[0] is IrExpressionStatement singleExpr)
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
                    foreach (var label in c.Labels)
                        PrintCaseLabel(sb, indent, label);

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
                // Dart flattens nested types, so drop the container qualification.
                sb.Append(InnermostTypeSegment(tr.Name));
                break;
            case IrThisExpression:
                sb.Append("this");
                break;
            case IrBaseExpression:
                sb.Append("super");
                break;
            case IrMemberAccess { Origin: { } origin } ma when IsRecordFieldRead(origin):
                PrintAccessTarget(sb, ma.Target);
                sb.Append(".$").Append(origin.TupleIndex + 1);
                break;
            case IrMemberAccess ma:
                PrintAccessTarget(sb, ma.Target);
                sb.Append('.').Append(IrToDartNamingPolicy.ToParameterName(ma.MemberName));
                break;
            case IrElementAccess ea:
                PrintAccessTarget(sb, ea.Target);
                sb.Append('[');
                PrintExpression(sb, ea.Index);
                sb.Append(']');
                break;
            case IrSliceExpression slice:
                PrintSlice(sb, slice);
                break;
            case IrLetExpression let:
                // `((name) => body)(value)` — evaluates the value once and
                // scopes the binding to the body, like the TypeScript IIFE.
                sb.Append("((")
                    .Append(IrToDartNamingPolicy.ToParameterName(let.Name))
                    .Append(')');
                PrintClosureBody(sb, let.Body);
                sb.Append(")(");
                PrintExpression(sb, let.Value);
                sb.Append(')');
                break;
            case IrSequenceExpression sequence:
                // Dart has no comma operator: `(() { effect; return result; })()`.
                sb.Append("(()");
                PrintClosureBody(sb, sequence);
                sb.Append(")()");
                break;
            case IrCallExpression
            {
                Target: IrMemberAccess hashed,
                Arguments.Count: 0,
                Origin: { } hashOrigin,
            } when IsHashCodeCall(hashOrigin):
                PrintAccessTarget(sb, hashed.Target);
                sb.Append(".hashCode");
                break;
            case IrCallExpression call:
                PrintCallTarget(sb, call.Target);
                if (call.IsOptional)
                    sb.Append("?.call");
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
                PrintOperand(sb, bin.Left, DartBinaryOperandGrouping.NeedsLeftParentheses(bin));
                sb.Append(' ').Append(FormatBinaryOp(bin.Operator)).Append(' ');
                PrintOperand(sb, bin.Right, DartBinaryOperandGrouping.NeedsRightParentheses(bin));
                break;
            case IrUnaryExpression un:
                if (un.IsPrefix)
                {
                    sb.Append(FormatUnaryOp(un.Operator));
                    PrintGroupedOperand(sb, un.Operand);
                }
                else
                {
                    PrintGroupedOperand(sb, un.Operand);
                    sb.Append(FormatUnaryOp(un.Operator));
                }
                break;
            case IrConditionalExpression cond:
                PrintOperand(
                    sb,
                    cond.Condition,
                    DartBinaryOperandGrouping.BindsLooserThanAnyOperator(cond.Condition)
                );
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
                PrintGroupedOperand(sb, cast.Expression);
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
            case IrTupleLiteral tuple:
                PrintRecordLiteral(sb, tuple);
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
            case IrNullConditionalAccess access:
                PrintNullConditionalAccess(sb, access);
                break;
            case IrConditionalReceiver:
                // Read outside an access chain only under a guard, where it
                // is the bound parameter.
                PrintExpression(sb, CurrentConditionalReceiver().Expression);
                break;
            case IrUnsupportedExpression u:
                // Already reported as MS0001 by the extractor; `throw` is an
                // expression in Dart, so the placeholder parses anywhere.
                sb.Append("(throw UnsupportedError('Unsupported expression: ")
                    .Append(u.Kind)
                    .Append("'))");
                break;
            default:
                sb.Append("/* TODO: ").Append(expr.GetType().Name).Append(" */");
                break;
        }
    }

    /// <summary>
    /// Print a call target, wrapping it in parentheses when its node
    /// kind has lower precedence than the call. Required because the
    /// IR may surface low-precedence shapes as the call target (e.g.
    /// <c>(a ?? b)?.Invoke()</c> lowers to a call whose target is a
    /// binary expression — printed bare it would parse as
    /// <c>a ?? b()</c>).
    /// </summary>
    private static void PrintCallTarget(StringBuilder sb, IrExpression target)
    {
        if (CallTargetRequiresParens(target))
        {
            sb.Append('(');
            PrintExpression(sb, target);
            sb.Append(')');
        }
        else
        {
            PrintExpression(sb, target);
        }
    }

    private static bool CallTargetRequiresParens(IrExpression expression) =>
        expression
            is IrBinaryExpression
                or IrConditionalExpression
                or IrAwaitExpression
                or IrLambdaExpression;

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
                PrintGroupedOperand(sb, node.Expression);
                sb.Append(" == ");
                PrintExpression(sb, constant.Value);
                break;
            case IrTypePattern { Designator: null } typePat:
                PrintGroupedOperand(sb, node.Expression);
                sb.Append(" is ").Append(FormatType(typePat.Type));
                break;
            case IrDiscardPattern:
                sb.Append("true");
                break;
            case IrPropertyPattern { Designator: null } prop:
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
            case IrTypePattern { Designator: null } typePat:
                PrintExpression(sb, value);
                sb.Append(" is ").Append(FormatType(typePat.Type));
                break;
            case IrDiscardPattern or IrVarPattern:
                sb.Append("true");
                break;
            case IrPropertyPattern { Designator: null } prop:
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
    /// One label of a switch statement. Dart 3 switch statements match
    /// patterns natively, so a pattern label prints as
    /// <c>case pattern when guard:</c>.
    /// </summary>
    private static void PrintCaseLabel(StringBuilder sb, string indent, IrCaseLabel label)
    {
        switch (label)
        {
            case IrConstantCaseLabel constant:
                sb.Append(indent).Append("  case ");
                PrintExpression(sb, constant.Value);
                sb.AppendLine(":");
                break;

            case IrPatternCaseLabel pattern:
                sb.Append(indent).Append("  case ");
                PrintSwitchPattern(sb, pattern.Pattern);

                if (pattern.WhenClause is not null)
                {
                    sb.Append(" when ");
                    PrintExpression(sb, pattern.WhenClause);
                }

                sb.AppendLine(":");
                break;

            default:
                sb.Append(indent).AppendLine("  default:");
                break;
        }
    }

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
            sb.Append(varPat.Designator.Name);
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
                if (typePat.Designator is not null)
                    sb.Append(' ').Append(typePat.Designator.Name);
                break;
            case IrVarPattern varPat:
                sb.Append("var ").Append(varPat.Designator.Name);
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
                if (prop.Designator is not null)
                    sb.Append(' ').Append(prop.Designator.Name);
                break;
            case IrRelationalPattern rel:
                // Dart 3 relational patterns match C#'s shape syntactically:
                // `> 0`, `<= 10`, etc.
                sb.Append(RelationalOpToken(rel.Operator)).Append(' ');
                PrintExpression(sb, rel.Value);
                break;
            case IrLogicalPattern
            {
                Operator: IrLogicalOp.Not,
                Left: IrConstantPattern { Value: IrLiteral { Kind: IrLiteralKind.Null } },
            }:
                // `not null` is Dart's null-check pattern on a wildcard.
                sb.Append("_?");
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
                if (pos.Designator is not null)
                    sb.Append(' ').Append(pos.Designator.Name);
                break;
            default:
                sb.Append("/* TODO: unsupported pattern ")
                    .Append(pattern.GetType().Name)
                    .Append(" */");
                break;
        }
    }

    /// <summary>
    /// The parameter a guarded null-conditional access binds its receiver
    /// to. Nested guards may reuse the name: a nested receiver is the
    /// closure's argument, printed outside the nested closure, where the
    /// name still means the enclosing receiver. <c>$</c> cannot appear in a
    /// C# identifier, so the name never captures a source local.
    /// </summary>
    private const string GuardedReceiverParameter = "$receiver";

    /// <summary>
    /// What each <see cref="IrConditionalReceiver"/> prints as, innermost
    /// access on top, while that access's <c>WhenNotNull</c> is printed:
    /// the receiver itself followed by the null-shorting <c>?</c>, or the
    /// guard's bound parameter.
    /// </summary>
    [ThreadStatic]
    private static Stack<ConditionalReceiver>? _conditionalReceivers;

    private sealed record ConditionalReceiver(IrExpression Expression, bool IsNullShorting);

    /// <summary>
    /// Dart null-shorting covers the rest of a <c>?.</c> chain like C#, so a
    /// null-conditional access whose steps read the receiver once, at the
    /// root of a member/element chain, prints natively
    /// (<c>order?.items.length</c>). Other shapes (an operator over the
    /// chain, a step lowered to a helper that takes the receiver as an
    /// argument, a receiver read twice) bind the receiver once and guard it:
    /// <c>(($receiver) =&gt; $receiver == null ? null : step($receiver))(order)</c>.
    /// </summary>
    private static void PrintNullConditionalAccess(StringBuilder sb, IrNullConditionalAccess access)
    {
        if (!PrintsAsNullShortingChain(access))
        {
            PrintGuardedNullConditionalAccess(sb, access);
            return;
        }

        PrintWithConditionalReceiver(
            sb,
            access.WhenNotNull,
            new ConditionalReceiver(access.Receiver, IsNullShorting: true)
        );
    }

    private static bool PrintsAsNullShortingChain(IrNullConditionalAccess access) =>
        IrEvaluationTraits.CountConditionalReceiverReads(access.WhenNotNull) == 1
        && ReadsConditionalReceiverAtChainRoot(access.WhenNotNull);

    private static void PrintGuardedNullConditionalAccess(
        StringBuilder sb,
        IrNullConditionalAccess access
    )
    {
        sb.Append("((")
            .Append(GuardedReceiverParameter)
            .Append(") => ")
            .Append(GuardedReceiverParameter)
            .Append(" == null ? null : ");

        PrintWithConditionalReceiver(
            sb,
            access.WhenNotNull,
            new ConditionalReceiver(
                new IrIdentifier(GuardedReceiverParameter),
                IsNullShorting: false
            )
        );

        sb.Append(")(");
        PrintExpression(sb, access.Receiver);
        sb.Append(')');
    }

    private static void PrintWithConditionalReceiver(
        StringBuilder sb,
        IrExpression whenNotNull,
        ConditionalReceiver receiver
    )
    {
        var receivers = _conditionalReceivers ??= new Stack<ConditionalReceiver>();
        receivers.Push(receiver);

        try
        {
            PrintExpression(sb, whenNotNull);
        }
        finally
        {
            receivers.Pop();
        }
    }

    private static ConditionalReceiver CurrentConditionalReceiver() =>
        _conditionalReceivers is { Count: > 0 } receivers
            ? receivers.Peek()
            : throw new InvalidOperationException(
                "A conditional receiver was printed outside its null-conditional access."
            );

    private static bool ReadsConditionalReceiverAtChainRoot(IrExpression step) =>
        step switch
        {
            IrMemberAccess { Target: IrConditionalReceiver } => true,
            IrElementAccess { Target: IrConditionalReceiver } => true,
            IrMemberAccess access => ReadsConditionalReceiverAtChainRoot(access.Target),
            IrElementAccess element => ReadsConditionalReceiverAtChainRoot(element.Target),
            IrCallExpression { IsOptional: false } call => ReadsConditionalReceiverAtChainRoot(
                call.Target
            ),
            _ => false,
        };

    /// <summary>
    /// The object of a member or element access; the conditional receiver
    /// of a null-shorting chain prints as the link (<c>receiver?</c>,
    /// followed by the access's own <c>.name</c> / <c>[index]</c>).
    /// </summary>
    private static void PrintAccessTarget(StringBuilder sb, IrExpression target)
    {
        if (target is not IrConditionalReceiver)
        {
            PrintExpression(sb, target);
            return;
        }

        var receiver = CurrentConditionalReceiver();

        PrintCallTarget(sb, receiver.Expression);

        if (receiver.IsNullShorting)
            sb.Append('?');
    }

    /// <summary>
    /// The operand of a unary operator, <c>is</c>, <c>as</c> or a constant
    /// pattern, parenthesized when it would otherwise regroup.
    /// </summary>
    private static void PrintGroupedOperand(StringBuilder sb, IrExpression operand) =>
        PrintOperand(sb, operand, DartBinaryOperandGrouping.NeedsUnaryOperandParentheses(operand));

    private static void PrintOperand(StringBuilder sb, IrExpression operand, bool parenthesize)
    {
        if (parenthesize)
            sb.Append('(');

        PrintExpression(sb, operand);

        if (parenthesize)
            sb.Append(')');
    }

    /// <summary>
    /// The body of a closure that yields <paramref name="body"/>: an arrow
    /// (<c> => body</c>) for a plain expression, or a block running each effect
    /// of an <see cref="IrSequenceExpression"/> before returning its result.
    /// </summary>
    private static void PrintClosureBody(StringBuilder sb, IrExpression body)
    {
        if (body is not IrSequenceExpression sequence)
        {
            sb.Append(" => ");
            PrintExpression(sb, body);

            return;
        }

        sb.Append(" { ");

        foreach (var effect in sequence.Effects)
        {
            PrintExpression(sb, effect);
            sb.Append("; ");
        }

        sb.Append("return ");
        PrintExpression(sb, sequence.Result);
        sb.Append("; }");
    }

    /// <summary>
    /// <c>s.substring(start, end)</c> for a string, <c>list.sublist(start, end)</c>
    /// otherwise; an absent start is <c>0</c> and an absent end is omitted.
    /// </summary>
    private static void PrintSlice(StringBuilder sb, IrSliceExpression slice)
    {
        var method = slice.TargetType is IrPrimitiveTypeRef { Primitive: IrPrimitive.String }
            ? "substring"
            : "sublist";

        PrintExpression(sb, slice.Target);
        sb.Append('.').Append(method).Append('(');

        if (slice.Start is null)
            sb.Append('0');
        else
            PrintExpression(sb, slice.Start);

        if (slice.End is not null)
        {
            sb.Append(", ");
            PrintExpression(sb, slice.End);
        }

        sb.Append(')');
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

    private static string InnermostTypeSegment(string qualifiedName) =>
        qualifiedName[(qualifiedName.LastIndexOf('.') + 1)..];

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");

    /// <summary>
    /// A positional element read of a C# tuple, which Dart models as a record:
    /// its positional fields are <c>$1</c>, <c>$2</c>, … whatever the C#
    /// element name. A <c>[JsTuple]</c> record's positional member is a plain
    /// field of the emitted class and keeps its name.
    /// </summary>
    private static bool IsRecordFieldRead(IrMemberOrigin origin) =>
        origin is { TupleElement: IrTupleElementKind.ValueTuple, TupleIndex: >= 0 };

    /// <summary>
    /// A <c>GetHashCode()</c> call: <c>object.GetHashCode</c>'s origin, which
    /// every such call carries, or an emitted override's (a
    /// <c>base.GetHashCode()</c>). Both read Dart's <c>hashCode</c> getter, the
    /// member a <c>GetHashCode</c> override is declared as.
    /// </summary>
    private static bool IsHashCodeCall(IrMemberOrigin origin) =>
        IrObjectContract.IsObjectGetHashCode(origin)
        || IrObjectContract.IsHashCodeOverrideAccess(origin);

    /// <summary>
    /// A C# tuple literal as a Dart 3 record literal, <c>(a, b)</c>; a
    /// one-element record needs the trailing comma, <c>(a,)</c>.
    /// </summary>
    private static void PrintRecordLiteral(StringBuilder sb, IrTupleLiteral tuple)
    {
        sb.Append('(');

        for (var i = 0; i < tuple.Elements.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            PrintExpression(sb, tuple.Elements[i]);
        }

        if (tuple.Elements.Count == 1)
            sb.Append(',');

        sb.Append(')');
    }

    private static string FormatBinaryOp(IrBinaryOp op) =>
        op switch
        {
            IrBinaryOp.Add => "+",
            IrBinaryOp.Subtract => "-",
            IrBinaryOp.Multiply => "*",
            IrBinaryOp.Divide => "/",
            IrBinaryOp.IntegerDivide => "~/",
            IrBinaryOp.Modulo => "%",
            // Dart's `==` dispatches to the operand's `operator ==`, which is
            // value equality for the classes emitted from records.
            IrBinaryOp.Equal or IrBinaryOp.ValueEqual => "==",
            IrBinaryOp.NotEqual or IrBinaryOp.ValueNotEqual => "!=",
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
            IrBinaryOp.UnsignedRightShiftAssign => ">>>=",
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
