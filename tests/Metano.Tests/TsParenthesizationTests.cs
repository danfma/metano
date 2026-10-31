using System.Runtime.CompilerServices;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Parenthesization;

namespace Metano.Tests;

/// <summary>
/// Printer-level parenthesization rules (#244, FR-014), exercised on
/// hand-built TypeScript AST so shapes no C# source produces today
/// (<c>**</c>, <c>new (f())()</c>, statement-leading object literals) are
/// covered too.
/// </summary>
public class TsParenthesizationTests
{
    private static readonly TsExpression A = Id("a");
    private static readonly TsExpression B = Id("b");
    private static readonly TsExpression C = Id("c");

    public static IEnumerable<Func<(TsExpression Expression, string Expected)>> Cases()
    {
        // `**` is right-associative and rejects a bare unary base.
        yield return () => (Bin(Lit("2"), "**", Bin(Lit("3"), "**", Lit("2"))), "2 ** 3 ** 2");
        yield return () => (Bin(Bin(Lit("2"), "**", Lit("3")), "**", Lit("2")), "(2 ** 3) ** 2");
        yield return () => (Bin(Neg(A), "**", B), "(-a) ** b");

        // `new` callees containing a call.
        yield return () => (New(Call(Id("f"))), "new (f())()");
        yield return () => (New(Call(Prop(Id("o"), "f"))), "new (o.f())()");
        yield return () => (New(Prop(A, "B")), "new a.B()");

        // Numeric literal receivers.
        yield return () => (Call(Prop(Lit("1"), "toString")), "(1).toString()");
        yield return () => (Call(Prop(Lit("-1"), "toString")), "(-1).toString()");

        // Token gluing between same-sign prefix operators.
        yield return () => (Neg(Neg(A)), "-(-a)");
        yield return () => (Un("+", Un("+", A)), "+(+a)");
        yield return () => (Neg(Un("--", B)), "-(--b)");
        yield return () => (Neg(Lit("-1")), "-(-1)");
        yield return () => (Un("!", Un("!", A)), "!!a");
        yield return () => (Un("~", Neg(A)), "~-a");
        yield return () => (Bin(A, "-", Neg(B)), "a - -b");
        yield return () => (Un("typeof ", Bin(A, "+", B)), "typeof (a + b)");

        // Concise arrow bodies that start with an object literal.
        yield return () => (Arrow(ObjA1()), "() => ({ a: 1 })");
        yield return () => (Arrow(Prop(ObjA1(), "a")), "() => ({ a: 1 }.a)");

        // Optional member access is a real node.
        yield return () => (Prop(A, "b", optional: true), "a?.b");
        yield return () => (Prop(Bin(A, "??", B), "c", optional: true), "(a ?? b)?.c");

        // An explicit TsParenthesized is a primary atom: never wrapped twice.
        yield return () =>
            (Call(Prop(new TsParenthesized(Bin(A, "??", B)), "trim")), "(a ?? b).trim()");

        // TS5076: `??` never mixes with a bare `||` / `&&`.
        yield return () => (Bin(Bin(A, "??", B), "||", C), "(a ?? b) || c");
        yield return () => (Bin(A, "??", Bin(B, "||", C)), "a ?? (b || c)");
        yield return () => (Bin(Bin(A, "||", B), "??", C), "(a || b) ?? c");
        yield return () => (Bin(A, "??", Bin(B, "&&", C)), "a ?? (b && c)");
        yield return () => (Bin(A, "??", Bin(B, "??", C)), "a ?? b ?? c");
        yield return () => (Bin(A, "||", Bin(B, "&&", C)), "a || b && c");
        yield return () => (Bin(A, "&&", Bin(B, "||", C)), "a && (b || c)");

        // Assignment is right-associative and needs a member-level target.
        yield return () => (Bin(A, "=", Bin(B, "=", C)), "a = b = c");
        yield return () => (Prop(Bin(A, "=", B), "c"), "(a = b).c");
        yield return () => (Bin(A, "*", Bin(A, "=", B)), "a * (a = b)");

        // Casts, await, conditionals.
        yield return () => (Cast(Bin(A, "+", B)), "a + b as number");
        yield return () => (Cast(Bin(A, "||", B)), "(a || b) as number");
        yield return () => (Prop(Cast(A), "x"), "(a as number).x");
        yield return () => (Prop(new TsAwaitExpression(A), "length"), "(await a).length");
        yield return () => (new TsAwaitExpression(Bin(A, "+", B)), "await (a + b)");
        yield return () => (Cond(Cond(A, B, C), Lit("1"), Lit("2")), "(a ? b : c) ? 1 : 2");
        yield return () => (Cond(A, Bin(B, "=", C), Lit("0")), "a ? b = c : 0");
        yield return () => (Cond(Bin(A, "||", B), Lit("1"), Lit("2")), "a || b ? 1 : 2");
        yield return () => (Bin(Cond(A, B, C), "+", Lit("1")), "(a ? b : c) + 1");

        // Template placeholders: wrapped by the neighbors of each occurrence.
        yield return () =>
            (
                Template("($this & $0) === $0", Bin(Id("f"), "|", Id("g")), Id("F.B")),
                "((f | g) & F.B) === F.B"
            );
        yield return () =>
            (
                Template("($this & $0) === $0", Id("x"), Bin(Id("F.A"), "|", Id("F.B"))),
                "(x & (F.A | F.B)) === (F.A | F.B)"
            );
        yield return () =>
            (Template("$this[$this.length - 1]", Bin(A, "??", B)), "(a ?? b)[(a ?? b).length - 1]");
        yield return () =>
            (Template("console.log($0)", null, Bin(A, "+", B)), "console.log(a + b)");
        yield return () => (Template("$this.length = 0", Cond(A, B, C)), "(a ? b : c).length = 0");
        yield return () =>
            (
                Template("$this.replace(/-/g, \"\")", Bin(A, "??", B)),
                "(a ?? b).replace(/-/g, \"\")"
            );
        yield return () =>
            (Template("(_, i) => $0 + i", null, Cond(A, B, C)), "(_, i) => (a ? b : c) + i");
        yield return () =>
            (Template("$this.push(...$0)", A, Cond(A, B, C)), "a.push(...a ? b : c)");
        yield return () =>
            (
                new TsTemplate("$T0[$0 as keyof typeof $T0]", null, [Bin(A, "??", B)])
                {
                    TypeArgumentNames = ["E"],
                },
                "E[(a ?? b) as keyof typeof E]"
            );

        // Template as an operand: classified by its own text.
        yield return () => (Bin(Template("$0 + $1", null, A, B), "*", C), "(a + b) * c");
        yield return () =>
            (Call(Prop(Template("$0 + $1", null, A, B), "toString")), "(a + b).toString()");
        yield return () => (Bin(Template("Math.floor($0)", null, A), "+", B), "Math.floor(a) + b");
        yield return () => (Prop(Template("$this", Bin(A, "??", B)), "length"), "(a ?? b).length");
        yield return () => (Neg(Template("-$0", null, A)), "-(-a)");
        yield return () => (Bin(Template("$this.length = 0", A), "||", B), "(a.length = 0) || b");
        yield return () =>
            (Prop(Template("new Decimal($0)", null, A), "plus"), "new Decimal(a).plus");

        // A plain member access on an optional chain closes the chain.
        yield return () => (Prop(Prop(A, "b", optional: true), "c"), "(a?.b).c");
        yield return () => (Prop(Prop(A, "b", optional: true), "c", optional: true), "a?.b?.c");
        yield return () => (Call(Prop(A, "m", optional: true)), "a?.m()");

        // TS5076 and cast continuations seen through template text.
        yield return () => (Bin(Template("$0 ?? $1", null, A, B), "||", C), "(a ?? b) || c");
        yield return () => (Bin(A, "??", Template("$this", Bin(B, "||", C))), "a ?? (b || c)");
        yield return () => (Bin(Cast(A), "|", B), "(a as number) | b");
        yield return () => (Bin(Cast(A), "<", B), "(a as number) < b");
        yield return () => (Bin(Cast(A), "===", B), "a as number === b");
        yield return () => (Bin(Template("$0 as any", null, A), "&", B), "(a as any) & b");

        // A template's leading placeholder is what prints first.
        yield return () => (Arrow(Template("$0.foo()", null, ObjA1())), "() => ({ a: 1 }.foo())");

        yield return () => (Bin(Bin(A, "===", Cast(B)), "|", C), "(a === b as number) | c");
        yield return () => (Prop(Template("$this", Prop(A, "b", optional: true)), "c"), "(a?.b).c");
        yield return () => (Prop(Template("$0", null, Lit("1")), "x"), "(1).x");

        // Placeholder fallbacks: a lone `$`, out-of-range `$N`/`$TN` and a
        // missing receiver keep the raw text (no receiver prints nothing).
        yield return () => (Template("$ + $5 + $T3", null, A), "$ + $5 + $T3");
        yield return () => (Template("f($this)", null), "f()");

        // Text the lexer cannot analyze over-wraps its substitutions.
        yield return () => (Template("f($0", null, Bin(A, "+", B)), "f((a + b)");
        yield return () => (Template("$0 + \"x", null, Cond(A, B, C)), "(a ? b : c) + \"x");
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task RenderExpression_ParenthesizesByOperandSlot(
        TsExpression expression,
        string expected
    )
    {
        await Assert.That(Printer.RenderExpression(expression)).IsEqualTo(expected);
    }

    [Test]
    public async Task ExpressionStatement_StartingWithObjectLiteral_IsWrapped()
    {
        var statement = new TsExpressionStatement(Prop(ObjA1(), "a"));

        await Assert.That(PrintStatement(statement)).IsEqualTo("({ a: 1 }.a);\n");
    }

    [Test]
    public async Task ExpressionStatement_StartingWithFunctionExpression_IsWrapped()
    {
        var function = new TsNamedFunctionExpression("f", [], null, []);
        var statement = new TsExpressionStatement(Call(function));

        await Assert.That(PrintStatement(statement)).StartsWith("(function f()");
    }

    [Test]
    [Arguments("instanceof", "Relational")]
    [Arguments("in", "Relational")]
    [Arguments("==", "Equality")]
    [Arguments("!=", "Equality")]
    [Arguments("??", "ShortCircuit")]
    [Arguments(">>>=", "Assignment")]
    [Arguments("**", "Exponent")]
    [Arguments("?", "Lowest")]
    public async Task OfOperator_ClassifiesToken(string token, string expected)
    {
        var precedence = TsPrecedenceClassifier.OfOperator(token);

        await Assert.That(precedence.ToString()).IsEqualTo(expected);
    }

    /// <summary>
    /// A new <see cref="TsExpression"/> subtype must be given a precedence
    /// level: falling through to <see cref="TsPrecedence.Lowest"/> over-wraps
    /// it everywhere. Nodes whose level depends on their payload are
    /// covered by the rendering cases instead.
    /// </summary>
    [Test]
    public async Task Of_ClassifiesEveryExpressionNode()
    {
        var payloadDependent = new[]
        {
            typeof(TsLiteral),
            typeof(TsBinaryExpression),
            typeof(TsTemplate),
        };
        var unclassified = typeof(TsExpression)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TsExpression)) && !t.IsAbstract)
            .Except(payloadDependent)
            .Where(t =>
                TsPrecedenceClassifier.Of((TsExpression)RuntimeHelpers.GetUninitializedObject(t))
                is TsPrecedence.Lowest
            )
            .Select(t => t.Name)
            .ToList();

        await Assert.That(unclassified).IsEmpty();
    }

    private static string PrintStatement(TsStatement statement) =>
        new Printer().Print(new TsSourceFile("probe.ts", [new TsTopLevelStatement(statement)]));

    private static TsIdentifier Id(string name) => new(name);

    private static TsLiteral Lit(string raw) => new(raw);

    private static TsBinaryExpression Bin(TsExpression left, string op, TsExpression right) =>
        new(left, op, right);

    private static TsUnaryExpression Un(string op, TsExpression operand) => new(op, operand);

    private static TsUnaryExpression Neg(TsExpression operand) => Un("-", operand);

    private static TsPropertyAccess Prop(
        TsExpression target,
        string property,
        bool optional = false
    ) => new(target, property, optional);

    private static TsCallExpression Call(TsExpression callee) => new(callee, []);

    private static TsNewExpression New(TsExpression callee) => new(callee, []);

    private static TsCastExpression Cast(TsExpression expression) =>
        new(expression, new TsNumberType());

    private static TsConditionalExpression Cond(
        TsExpression test,
        TsExpression whenTrue,
        TsExpression whenFalse
    ) => new(test, whenTrue, whenFalse);

    private static TsArrowFunction Arrow(TsExpression body) =>
        new([], [new TsReturnStatement(body)]);

    private static TsObjectLiteral ObjA1() => new([new TsObjectProperty("a", Lit("1"))]);

    private static TsTemplate Template(
        string text,
        TsExpression? receiver,
        params TsExpression[] arguments
    ) => new(text, receiver, arguments);
}
