namespace Metano.Tests;

/// <summary>
/// A local declared in one switch section and assigned or read in a later one
/// is declared ahead of the switch, so the later section never reaches a
/// section-level <c>let</c> in its temporal dead zone (#755).
/// </summary>
public class SwitchSharedLocalTests
{
    private const string Source = """
        [Transpile]
        public static class Picker
        {
            public static int Constant(int k)
            {
                switch (k)
                {
                    case 1:
                        int x = 1;
                        return x;
                    case 2:
                        x = 2;
                        return x;
                    default:
                        return 0;
                }
            }

            public static int Tested(object o)
            {
                switch (o)
                {
                    case null:
                        var y = 1;
                        return y;
                    case string:
                        y = 3;
                        return y;
                    default:
                        int local = 4;
                        return local;
                }
            }
        }
        """;

    [Test]
    public async Task LocalUsedInALaterSection_IsDeclaredAheadOfTheSwitch()
    {
        var output = TranspileHelper.Transpile(Source)["picker.ts"];

        await Assert
            .That(output)
            .IsEqualTo(
                """
                import { isString } from "metano-runtime";

                export class Picker {
                  private constructor() { }

                  static constant(k: number): number {
                    {
                      let x: number;

                      switch (k) {
                        case 1:
                          x = 1;

                          return x;
                        case 2:
                          x = 2;

                          return x;
                        default:
                          return 0;
                      }
                    }
                  }

                  static tested(o: Object): number {
                    {
                      let y: number;

                      switch (true) {
                        case o == null:
                          y = 1;

                          return y;
                        case isString(o):
                          y = 3;

                          return y;
                        default:
                          const local = 4;

                          return local;
                      }
                    }
                  }
                }

                """
            );
    }

    [Test]
    public async Task LocalUsedInALaterSection_PassesTsgo()
    {
        var files = TranspileHelper.Transpile(Source);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    private const string OutVarSource = """
        [Transpile]
        public static class Parser
        {
            public static string Read(int k)
            {
                switch (k)
                {
                    case 1:
                        int.TryParse("5", out var p);
                        return "o" + p;
                    default:
                        p = 9;
                        return "o" + p;
                }
            }
        }
        """;

    [Test]
    public async Task OutVarUsedInALaterSection_IsDeclaredAheadOfTheSwitch()
    {
        var output = TranspileHelper.Transpile(OutVarSource)["parser.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    {
                      let p: number;

                      switch (k) {
                        case 1:
                          [, p] = tryParseInt32("5");

                          return "o" + p;
                        default:
                          p = 9;

                          return "o" + p;
                      }
                    }
                """
            );
    }

    [Test]
    public async Task OutVarUsedInALaterSection_PassesTsgo()
    {
        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(OutVarSource),
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    private const string UsedResultSource = """
        [Transpile]
        public static class Lookup
        {
            static bool TryGet(string s, out int value)
            {
                value = s.Length;
                return s.Length > 2;
            }

            public static string InCondition(int k, string s)
            {
                switch (k)
                {
                    case 1:
                        if (int.TryParse(s, out var p))
                            return "ok" + p;

                        return "no" + p;
                    default:
                        p = 9;
                        return "d" + p;
                }
            }

            public static string InLocal(int k, string s)
            {
                switch (k)
                {
                    case 1:
                        var ok = TryGet(s, out var q);
                        return ok + ":" + q;
                    default:
                        q = 9;
                        return "d" + q;
                }
            }
        }
        """;

    [Test]
    public async Task OutCallWithAUsedResult_KeepsTheResultWhenHoisted()
    {
        var output = TranspileHelper.Transpile(UsedResultSource)["lookup.ts"];

        await Assert.That(output).Contains("let $found0: boolean;");
        await Assert.That(output).Contains("[$found0, p] = tryParseInt32(s);");
        await Assert.That(output).Contains("let ok: boolean;");
        await Assert.That(output).Contains("[ok, q] = Lookup.tryGet(s);");

        var (exitCode, typeCheck) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(UsedResultSource),
            new Dictionary<string, string>()
        );

        await Assert.That(typeCheck).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }
}
