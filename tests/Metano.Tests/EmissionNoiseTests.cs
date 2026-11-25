namespace Metano.Tests;

/// <summary>
/// The emission noise the Metanui audit listed (#787 N1, N2, N3, N5, N7, N8): a
/// nullable array element read is bare at run time, a string compound
/// assignment guards its target only when it may be <c>null</c>, a catch
/// takes the optional binding when nothing reads it, a catch variable binds
/// straight from the caught value, string constants fold, and <c>else if</c>
/// chains and <c>is not null</c> print flat.
/// </summary>
public class EmissionNoiseTests
{
    private static string Emit(string members, string usings = "")
    {
        var files = TranspileHelper.Transpile(
            $$"""
            #nullable enable
            {{usings}}
            [Transpile]
            public static class Probes
            {
                {{members}}
            }
            """
        );

        return files["probes.ts"];
    }

    [Test]
    public async Task NullableArrayElementRead_IsBareAtRunTime()
    {
        var output = Emit(
            "static string?[] Items = new string?[3]; static string? At(int i) => Items[i];"
        );

        await Assert.That(output).Contains("return Probes.items[i] as string | null;");
        await Assert.That(output).DoesNotContain("?? null");
    }

    [Test]
    public async Task StringCompoundAssignment_GuardsOnlyAMaybeNullTarget()
    {
        var output = Emit(
            """
            static string Local() { var log = "a"; log += "x"; return log; }
            static string Typed(string a) { string log = a; log += "x"; return log; }
            static string Maybe(string? a) { var log = a; log += "x"; return log; }
            """
        );

        await Assert.That(output).Contains("let log = \"a\";\n    log += \"x\";");
        await Assert.That(output).Contains("let log = a;\n    log += \"x\";");
        await Assert.That(output).Contains("log = (log ?? \"\") + \"x\";");
    }

    [Test]
    public async Task EmptyCatch_UsesTheOptionalCatchBinding()
    {
        var output = Emit(
            """
            static void Run() { }
            static void Swallow() { try { Run(); } catch (Exception) { } }
            static void Named() { try { Run(); } catch (Exception) { Run(); } }
            """,
            "using System;"
        );

        await Assert.That(output).Contains("} catch {");
        await Assert.That(output).DoesNotContain("_err");
    }

    [Test]
    public async Task CatchRethrow_KeepsItsBinding()
    {
        var output = Emit(
            """
            static void Run() { }
            static void Rethrow() { try { Run(); } catch (Exception) { throw; } }
            """,
            "using System;"
        );

        await Assert.That(output).Contains("catch (_err)");
        await Assert.That(output).Contains("throw _err;");
    }

    [Test]
    public async Task CatchVariable_BindsStraightFromTheCaughtValue()
    {
        var output = Emit(
            """
            static void Run() { }
            static string Err() { try { Run(); return ""; } catch (Exception error) { return error.Message; } }
            """,
            "using System;"
        );

        await Assert.That(output).Contains("catch (e$) {\n      const error = toException(e$);");
        await Assert.That(output).DoesNotContain("e0");
    }

    [Test]
    public async Task StringConstants_FoldToTheirValue()
    {
        var output = Emit(
            """
            public const string Joined = "a" + "b";
            public const string Interpolated = $"x{Joined}y";
            public static string Local() { const string Inner = "p" + "q"; return Inner; }
            """
        );

        await Assert.That(output).Contains("static readonly joined: string = \"ab\";");
        await Assert.That(output).Contains("static readonly interpolated: string = \"xaby\";");
        await Assert.That(output).Contains("const inner = \"pq\";");
    }

    [Test]
    public async Task ElseIfChain_AndIsNotNull_PrintFlat()
    {
        var output = Emit(
            """
            static int Chain(int a) { if (a == 1) return 1; else if (a == 2) return 2; else return 3; }
            static bool NotNull(string? s) => s is not null;
            """
        );

        await Assert.That(output).Contains("} else if (a === 2) {");
        await Assert.That(output).DoesNotContain("else {\n      if");
        await Assert.That(output).Contains("return s != null;");
    }
}
