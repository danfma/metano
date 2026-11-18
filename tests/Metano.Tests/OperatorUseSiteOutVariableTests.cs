namespace Metano.Tests;

/// <summary>
/// A user-defined conversion or operator whose operand declares an
/// <c>out var</c> (baseline FR-077, FR-079): the call is bound ahead of the
/// statement once, and both the TypeScript form and the source form Dart
/// prints read the bound value.
/// </summary>
public class OperatorUseSiteOutVariableTests
{
    private const string Source = """
        [Transpile]
        public class Money
        {
            public int C;

            public Money(int c) { C = c; }

            public static Money operator +(Money a, Money b) => new(a.C + b.C);

            public static implicit operator Money(int c) => new(c);
        }

        [Transpile]
        public static class Uses
        {
            static bool Try(out int value)
            {
                value = 4;
                return true;
            }

            public static Money Convert()
            {
                Money m = Try(out var k) ? k : 0;
                return m;
            }

            public static Money Add(Money b)
            {
                var s = (Try(out var q) ? q : 1) + b;
                return s;
            }

            public static Money Store(Money m)
            {
                m += Try(out var r) ? r : 2;
                return m;
            }
        }
        """;

    [Test]
    public async Task Dart_PrintsTheBoundSourceForm()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden("operator-use-out-variables.dart", files["uses.dart"]);
    }

    [Test]
    public async Task TypeScript_BindsEachCallOnce()
    {
        var output = TranspileHelper.Transpile(Source)["uses.ts"];

        await TranspileHelper.AssertGolden("operator-use-out-variables.ts", output);
    }
}
