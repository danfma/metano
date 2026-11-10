using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The catch clauses of a C# <c>try</c> lower to one JS <c>catch</c> that
/// tests the caught value against each clause in source order (its type,
/// then its <c>when</c> filter) and rethrows it when no clause matches
/// (#628). A lone clause that takes every exception keeps the plain
/// <c>catch (e) { … }</c>.
/// </summary>
public class CatchClauseLoweringTests
{
    private const string Exceptions = """
        [Transpile]
        public class AppException : Exception
        {
            public AppException(string message, int code) : base(message) { Code = code; }

            public int Code { get; }
        }

        [Transpile]
        public class NotFoundException : AppException
        {
            public NotFoundException(string message) : base(message, 404) { }
        }

        """;

    [Test]
    public async Task CatchClauses_LowerToOneCatchWithAChain()
    {
        var result = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            [Transpile, EmitInFile("catch-clauses")]
            public class AppException : Exception
            {
                public AppException(string message, int code) : base(message) { Code = code; }

                public int Code { get; }
            }

            [Transpile, EmitInFile("catch-clauses")]
            public class NotFoundException : AppException
            {
                public NotFoundException(string message) : base(message, 404) { }
            }

            [Transpile, EmitInFile("catch-clauses")]
            public class Handlers
            {
                private readonly List<string> _log = new();

                public bool Verbose { get; set; }

                public void Work(string kind)
                {
                    if (kind == "missing")
                        throw new NotFoundException(kind);

                    if (kind == "app")
                        throw new AppException(kind, 1);

                    if (kind == "invalid")
                        throw new InvalidOperationException(kind);
                }

                public string Typed(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (NotFoundException)
                    {
                        return "missing";
                    }
                    catch (AppException e)
                    {
                        return "app:" + e.Code;
                    }
                }

                public string SingleTyped(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (NotFoundException e)
                    {
                        return "missing:" + e.Code;
                    }
                }

                public string GeneralLast(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (AppException e)
                    {
                        return "app:" + e.Code;
                    }
                    catch
                    {
                        return "general";
                    }
                }

                public string ExceptionLast(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (NotFoundException)
                    {
                        return "missing";
                    }
                    catch (Exception e)
                    {
                        return e is AppException ? "app" : "any";
                    }
                }

                public string Filtered(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (AppException e) when (e.Code == 404)
                    {
                        return "missing";
                    }
                    catch (AppException e) when (Verbose)
                    {
                        return "verbose:" + e.Code;
                    }
                    catch (Exception) when (kind == "quiet")
                    {
                        return "quiet";
                    }
                    catch (AppException)
                    {
                        return "app";
                    }
                }

                public void Rethrown(string kind)
                {
                    try
                    {
                        Work(kind);
                    }
                    catch (NotFoundException)
                    {
                        _log.Add("missing");

                        throw;
                    }
                    catch (AppException e)
                    {
                        e = new AppException("replaced", e.Code);
                        _log.Add(e.Message);

                        throw;
                    }
                    finally
                    {
                        _log.Add("done");
                    }
                }

                public void Nested(string kind)
                {
                    try
                    {
                        Work(kind);
                    }
                    catch (AppException e)
                    {
                        try
                        {
                            if (e.Code == 404)
                                throw;

                            Work("retry");
                        }
                        catch (NotFoundException inner)
                        {
                            _log.Add("inner:" + inner.Code);
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
                }

                public List<Func<int>> Deferred(List<string> kinds)
                {
                    var codes = new List<Func<int>>();

                    foreach (var kind in kinds)
                    {
                        try
                        {
                            Work(kind);
                        }
                        catch (NotFoundException)
                        {
                            continue;
                        }
                        catch (AppException e) when (e.Code < 0)
                        {
                            break;
                        }
                        catch (AppException e)
                        {
                            codes.Add(() => e.Code);
                        }
                    }

                    return codes;
                }

                public async Task<string> LoadAsync(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (NotFoundException e)
                    {
                        return await RecoverAsync(e.Code);
                    }
                    catch (AppException)
                    {
                        await RecoverAsync(0);

                        throw;
                    }
                }

                public string BclLast(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (AppException)
                    {
                        return "app";
                    }
                    catch (InvalidOperationException e)
                    {
                        return "invalid:" + e.Message;
                    }
                }

                public string ExceptionThenGeneral(string kind)
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (NotFoundException)
                    {
                        return "missing";
                    }
                    catch (Exception)
                    {
                        return "ex";
                    }
                    catch
                    {
                        return "general";
                    }
                }

                public string TypeParameterFirst<TFailure>(string kind)
                    where TFailure : Exception
                {
                    try
                    {
                        Work(kind);

                        return "ok";
                    }
                    catch (TFailure)
                    {
                        return "failure";
                    }
                    catch (Exception)
                    {
                        return "ex";
                    }
                }

                public void Logged(string kind)
                {
                    try
                    {
                        Work(kind);
                    }
                    catch
                    {
                        _log.Add("failed");

                        throw;
                    }
                }

                public void Wrapped(string kind)
                {
                    try
                    {
                        Work(kind);
                    }
                    catch (Exception e)
                    {
                        _log.Add(e is AppException ? "app" : "other");

                        throw;
                    }
                }

                private Task<string> RecoverAsync(int code) => Task.FromResult("recovered:" + code);
            }
            """
        );

        // The clause on a type parameter has no runtime test: reported, and
        // its arm ends the chain like any clause that takes every value.
        await Assert
            .That(result.Diagnostics.Select(d => d.Message))
            .IsEquivalentTo([
                "Catch clause '(TFailure)' cannot be lowered: its exception type has no runtime "
                    + "type test in the target (type parameters are erased), so the clause takes "
                    + "every exception.",
            ]);

        await TranspileHelper.AssertGolden("catch-clauses.ts", result.Files["catch-clauses.ts"]);
    }

    [Test]
    public async Task LoneCatchAllClause_KeepsThePlainCatch()
    {
        var output = TranspileHandler(
            """
            try { Work(); }
            catch (Exception failure) { Note(failure is AppException); }
            """
        );

        await Assert.That(output).Contains("} catch (failure) {");
        await Assert.That(output).DoesNotContain("e$");
    }

    [Test]
    public async Task LoneGeneralCatch_RethrowsItsSynthesizedParameter()
    {
        var output = TranspileHandler(
            """
            try { Work(); }
            catch { Note(true); throw; }
            """
        );

        await Assert.That(output).Contains("} catch (_err) {");
        await Assert.That(output).Contains("throw _err;");
    }

    [Test]
    public async Task LoneCatchAllClause_RethrowsTheCaughtValue_WhenTheVariableIsRebound()
    {
        var output = TranspileHandler(
            """
            try { Work(); }
            catch (Exception failure)
            {
                failure = new AppException("replaced", 1);
                Note(failure is AppException);
                throw;
            }
            """
        );

        await Assert.That(output).Contains("} catch (e$) {");
        await Assert.That(output).Contains("let failure = e$;");
        await Assert.That(output).Contains("throw e$;");
    }

    [Test]
    public async Task FilterOnALoneClause_RethrowsWhenItIsFalse()
    {
        var output = TranspileHandler(
            """
            try { Work(); }
            catch (AppException failure) when (failure.Code == 1) { Note(true); }
            """
        );

        await Assert
            .That(output)
            .Contains("if (e$ instanceof AppException && ((failure) => failure.code === 1)(e$)) {");

        await Assert.That(output).Contains("} else {\n        throw e$;\n      }");
        await Assert.That(output).DoesNotContain("const failure");
    }

    [Test]
    public async Task FilteredGeneralCatch_IsAnArmWithoutATypeTest()
    {
        var output = TranspileHandler(
            """
            try { Work(); }
            catch when (Ready) { Note(true); }
            catch (AppException) { Note(false); }
            """
        );

        await Assert.That(output).Contains("if (this.ready) {");
        await Assert.That(output).Contains("} else if (e$ instanceof AppException) {");
    }

    [Test]
    public async Task ClauseThatTakesEverything_EndsTheChain()
    {
        var output = TranspileHandler(
            """
            try { Work(); }
            catch (Exception) { Note(true); }
            catch { Note(false); }
            """
        );

        await Assert.That(output).Contains("} catch (e$) {\n      this.note(true);\n    }");
        await Assert.That(output).DoesNotContain("else");
        await Assert.That(output).DoesNotContain("this.note(false)");
    }

    [Test]
    public async Task CaughtValueName_AvoidsANameAClauseReferences()
    {
        var result = TranspileHelper.Transpile(
            Exceptions
                + """
                [Transpile, NoContainer]
                public static class Helpers
                {
                    [Name("e$")]
                    public static string Helper() => "helper";
                }

                [Transpile]
                public class Handler
                {
                    public string Run()
                    {
                        try { return "ok"; }
                        catch (AppException) { return Helpers.Helper(); }
                    }
                }
                """
        );

        var handler = result["handler.ts"];

        await Assert.That(handler).Contains("} catch (e$$) {");
        await Assert.That(handler).Contains("if (e$$ instanceof AppException) {");
        await Assert.That(handler).Contains("return e$();");
        await Assert.That(handler).Contains("throw e$$;");
    }

    [Test]
    public async Task ClauseOnACrossPackageException_ImportsItsClass()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("errors-lib")]

            namespace Errors;

            public class QuotaException : Exception
            {
                public QuotaException(string message) : base(message) { }
            }
            """,
            """
            using Errors;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("consumer")]

            namespace Consumer;

            public class Client
            {
                public int Calls;

                public void Call()
                {
                    try { Calls++; }
                    catch (QuotaException) { Calls = 0; }
                    catch (InvalidOperationException) { Calls = -1; }
                }
            }
            """
        );

        var client = result["consumer/client.ts"];

        await Assert.That(client).Contains("import { QuotaException } from \"errors-lib");
        await Assert.That(client).Contains("if (e$ instanceof QuotaException) {");
        await Assert.That(client).Contains("} else if (e$ instanceof Error) {");
    }

    [Test]
    public async Task JsxComponentBody_RewritesPropsInFilters_AndRethrows()
    {
        var result = TranspileHelper.TranspileJsx(
            """
            using System;
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;
            using Metano.TypeScript.SolidJs;
            using Metano.TypeScript.SolidJs.Web;
            using Metano.TypeScript.DOM;

            [Transpile]
            public class LoadException : Exception
            {
                public LoadException(string message) : base(message) { }
            }

            [Transpile]
            public sealed record Panel : JsxComponent
            {
                public bool Quiet { get; init; }

                public override JsxElement Render()
                {
                    try
                    {
                        return new Html.Div { Children = [Text("ready")] };
                    }
                    catch (LoadException) when (Quiet)
                    {
                        return new Html.Div { Children = [Text("failed")] };
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
            """
        );

        var panel = result["panel.tsx"];

        await Assert.That(panel).Contains("if (e$ instanceof LoadException && props$quiet) {");
        await Assert.That(panel).Contains("throw e$;");
    }

    [Test]
    public async Task DartTarget_KeepsItsNativeClauses_AndRethrows()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            "using System;\nusing Metano.Annotations;\n"
                + Exceptions
                + """
                [Transpile]
                public class Handler
                {
                    public void Run()
                    {
                        try { Work(); }
                        catch (InvalidOperationException) { Work(); }
                        catch (NotFoundException e) { Work(); throw; }
                        catch (AppException) { throw; }
                    }

                    public void Work() { }
                }
                """
        );

        var handler = files["handler.dart"];

        await Assert.That(handler).Contains("} on InvalidOperationException catch (e) {");
        await Assert.That(handler).Contains("} on NotFoundException catch (e) {");
        await Assert.That(handler).Contains("} on AppException catch (e) {");
        await Assert.That(handler).Contains("rethrow;");
        await Assert.That(handler).DoesNotContain("throw null");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task DartTarget_ReportsAFilter_AndImportsNothingForIt()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            "using System;\nusing Metano.Annotations;\n"
                + Exceptions
                + """
                [Transpile]
                public class Handler
                {
                    public void Run()
                    {
                        try { Work(); }
                        catch (Exception e) when (e is NotFoundException) { Work(); }
                    }

                    public void Work() { }
                }
                """
        );

        var warning = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert
            .That(warning.Message)
            .IsEqualTo(
                "Exception filter 'when (e is NotFoundException)' cannot be lowered: exception "
                    + "filters are not lowered for Dart; the clause would run unconditionally."
            );

        await Assert.That(files["handler.dart"]).Contains("} on Exception catch (e) {");
        await Assert.That(files["handler.dart"]).DoesNotContain("not_found_exception.dart");
    }

    /// <summary>
    /// The emitted <c>handler.ts</c> of a class whose <c>Run</c> method holds
    /// <paramref name="body"/>.
    /// </summary>
    private static string TranspileHandler(string body) =>
        TranspileHelper.Transpile(
            Exceptions
                + $$"""
                [Transpile]
                public class Handler
                {
                    public bool Ready { get; set; }

                    public void Run()
                    {
                        {{body}}
                    }

                    public void Work() { }

                    public void Note(bool value) { }
                }
                """
        )["handler.ts"];
}
