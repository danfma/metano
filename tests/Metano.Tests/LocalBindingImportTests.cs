namespace Metano.Tests;

/// <summary>
/// A name the emitted code binds itself (a <c>catch</c> parameter, a caught value, a
/// <c>foreach</c> variable, a parameter) is a local: it never resolves to a module
/// function another file exports under the same name, since that spurious import can
/// close an import cycle (#655).
/// </summary>
public class LocalBindingImportTests
{
    private const string FailureModule = """
        [Transpile, NoContainer]
        public static class Failures
        {
            public static string Failure() => "module";

            public static string Item() => "module";

            public static string Value() => "module";
        }

        """;

    [Test]
    public async Task CatchParameter_NamedLikeAnExport_AddsNoImport()
    {
        var result = TranspileHelper.Transpile(
            FailureModule
                + """
                [Transpile]
                public class Boot
                {
                    public string Run()
                    {
                        try
                        {
                            return "ok";
                        }
                        catch (Exception failure)
                        {
                            return failure.Message;
                        }
                    }
                }
                """
        );

        await Assert.That(result["boot.ts"]).DoesNotContain("import");
    }

    [Test]
    public async Task CaughtValueOfAChain_NamedLikeAnExport_AddsNoImport()
    {
        var result = TranspileHelper.Transpile(
            FailureModule
                + """
                [Transpile]
                public class AppException : Exception { }

                [Transpile]
                public class Boot
                {
                    public string Run()
                    {
                        try
                        {
                            return "ok";
                        }
                        catch (AppException failure)
                        {
                            return failure.Message;
                        }
                        catch (Exception other)
                        {
                            return other.Message;
                        }
                    }
                }
                """
        );

        await Assert.That(result["boot.ts"]).DoesNotContain("failures");
    }

    [Test]
    public async Task ForeachVariableAndParameter_NamedLikeAnExport_AddNoImport()
    {
        var result = TranspileHelper.Transpile(
            FailureModule
                + """
                [Transpile]
                public class Boot
                {
                    public int Run(int[] items, int value)
                    {
                        var total = value;

                        foreach (var item in items)
                            total += item;

                        return total;
                    }
                }
                """
        );

        await Assert.That(result["boot.ts"]).DoesNotContain("failures");
    }

    [Test]
    public async Task ReferenceOutsideTheCatch_StillImportsTheExport()
    {
        var result = TranspileHelper.Transpile(
            FailureModule
                + """
                [Transpile]
                public class Boot
                {
                    public string Run()
                    {
                        try
                        {
                            return Failures.Failure();
                        }
                        catch (Exception failure)
                        {
                            return failure.Message;
                        }
                    }
                }
                """
        );

        await Assert.That(result["boot.ts"]).Contains("import { failure } from");
    }

    [Test]
    public async Task StaticCallOnATypeRenamedWithADollar_ImportsAndUsesTheEmittedName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, Name("e$")]
            public class Renamed
            {
                public static string Make() => "made";
            }

            [Transpile]
            public class Boot
            {
                public string Run() => Renamed.Make();
            }
            """
        );

        await Assert.That(result["boot.ts"]).Contains("e$.make()");
        await Assert.That(result["boot.ts"]).Contains("import { e$ } from");
    }
}
