namespace Metano.Tests;

/// <summary>
/// How an <c>is</c> pattern that reads its subject more than once lowers
/// (#735): a string or array element read is repeated as such, any other
/// subject runs once into a temporary declared in the innermost function.
/// </summary>
public class PatternSubjectReuseTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        [Transpile]
        public class Probe
        {
            public int Calls;
            public string Next() { Calls++; return "5"; }
            public Task<string> Load() => Task.FromResult("5");
            public List<char> Chars = new();

            public static bool DigitAt(string text, int index) =>
                index >= 0 && index < text.Length && text[index] is >= '0' and <= '9';

            public static bool DigitAtArray(char[] chars, int index) =>
                chars[index] is >= '0' and <= '9';

            public bool ListDigit(int i) => Chars[i] is >= '0' and <= '9';

            public bool InIf(Func<char> next)
            {
                if (next() is >= '0' and <= '9')
                    return true;

                return false;
            }

            public bool ShortCircuit(bool a, Func<char> next) => a && next() is >= '0' and <= '9';

            public Func<Func<char>, bool> InLambda() => next => next() is >= '0' and <= '9';

            public static bool Recurse(Func<char> next, int depth) =>
                next() is >= '0' and <= '9' && (depth == 0 || Recurse(next, depth - 1));

            public static Func<char> Source = () => '5';
            public static bool FieldInit = Source() is >= '0' and <= '9';
            public bool Prop => Next() is "5" or "6";
            public bool InTernary(bool a) => a ? Next() is "5" or "6" : false;
            public bool Local()
            {
                bool Inner() => Next() is "5" or "6";
                return Inner();
            }
            public string Sw(Func<object> next) => next() switch { int i when i > 3 => "a", string => "b", _ => "c" };
            public bool Neg(Func<char> next) => next() is not (>= '0' and <= '9');
            public bool ListP(Func<int[]> next) => next() is [1, 2];

            public async Task<bool> AwaitIn() => (await Load()) is "5" or "6";
        }
        """;

    private static string Output() => TranspileHelper.Transpile(Source)["probe.ts"];

    [Test]
    public async Task StringElementSubject_IsRepeatedWithoutATemporary()
    {
        await Assert
            .That(Output())
            .Contains(
                "return index >= 0 && index < text.length && text[index]! >= \"0\" && text[index]! <= \"9\";"
            );
    }

    [Test]
    public async Task ArrayElementSubject_IsRepeatedWithoutATemporary()
    {
        await Assert
            .That(Output())
            .Contains("return chars[index]! >= \"0\" && chars[index]! <= \"9\";");
    }

    [Test]
    public async Task CollectionClassGet_KeepsTheTemporary()
    {
        await Assert
            .That(Output())
            .Contains(
                "($subject0 = this.chars.get(i), true) && $subject0 >= \"0\" && $subject0 <= \"9\""
            );
    }

    [Test]
    public async Task SubjectInAnIfCondition_IsAssignedInTheCondition()
    {
        await Assert
            .That(Output())
            .Contains(
                "if (($subject0 = next(), true) && $subject0 >= \"0\" && $subject0 <= \"9\") {"
            );
    }

    [Test]
    public async Task SubjectOfAShortCircuitOperand_IsAssignedOnlyWhenReached()
    {
        await Assert
            .That(Output())
            .Contains(
                "return a && ($subject0 = next(), true) && $subject0 >= \"0\" && $subject0 <= \"9\";"
            );
    }

    [Test]
    public async Task SubjectInAnExpressionLambda_IsDeclaredInTheLambda()
    {
        await Assert
            .That(Output())
            .Contains(
                """
                  inLambda(): (arg: () => string) => boolean {
                    return (next: () => string) => {
                      let $subject0: string;

                      return ($subject0 = next(), true) && $subject0 >= "0" && $subject0 <= "9";
                    };
                  }
                """
            );
    }

    [Test]
    public async Task AwaitedSubject_IsAssignedInTheAsyncFunction()
    {
        await Assert
            .That(Output())
            .Contains(
                "return ($subject0 = await this.load(), true) && ($subject0 === \"5\" || $subject0 === \"6\");"
            );
    }

    [Test]
    public async Task NoPatternSubject_IsBoundThroughAnArrow()
    {
        await Assert.That(Output()).DoesNotContain("($v) =>");
    }
}
