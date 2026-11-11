using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The runtime identity of the BCL exceptions (spec 011 FR-014, #651): a
/// mapped BCL exception constructs, extends, names and tests its
/// <c>metano-runtime</c> class, imported from its own module; an unmapped one
/// reports MS0001 at a construction (emitted as the runtime <c>Exception</c>
/// with its type name in the message) and MS0045 at a type test.
/// </summary>
public class BclExceptionIdentityTests
{
    [Test]
    public async Task ConstructDeriveTestAndCatch_MatchesTheGolden()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using System.Collections.Generic;

            namespace Billing;

            [Transpile, EmitInFile("bcl-exception-identity")]
            public class InvalidAmountException : ArgumentOutOfRangeException
            {
                public InvalidAmountException(decimal amount)
                    : base("amount", amount, "The amount must be positive.") { }
            }

            [Transpile, EmitInFile("bcl-exception-identity")]
            public class LedgerException : Exception
            {
                public LedgerException() { }

                public LedgerException(string message, Exception inner)
                    : base(message, inner) { }
            }

            [Transpile, EmitInFile("bcl-exception-identity")]
            public class StaleLedgerException : InvalidOperationException { }

            [Transpile, EmitInFile("bcl-exception-identity")]
            public static class Ledger
            {
                public static void Post(string? account, decimal amount, int attempts)
                {
                    if (account is null)
                        throw new ArgumentNullException(nameof(account));

                    if (amount <= 0)
                        throw new InvalidAmountException(amount);

                    if (attempts > 3)
                        throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Too many attempts.");

                    if (account.Length == 0)
                        throw new ArgumentException("The account is empty.", nameof(account));

                    throw new KeyNotFoundException($"No account '{account}'.");
                }

                public static Exception Wrap(Exception inner) =>
                    new InvalidOperationException("Posting failed.", inner);

                public static Exception Closed() => new ObjectDisposedException("Ledger");

                public static string Classify(Exception error) =>
                    error switch
                    {
                        ArgumentNullException => "null",
                        ArgumentException argument => "argument:" + argument.ParamName,
                        StaleLedgerException => "stale",
                        InvalidOperationException => "state",
                        _ => "other",
                    };

                public static bool IsArithmetic(object value) => value is ArithmeticException;

                public static FormatException? AsFormat(object value) => value as FormatException;

                public static string Recover(string? account, decimal amount)
                {
                    try
                    {
                        Post(account, amount, 0);

                        return "posted";
                    }
                    catch (KeyNotFoundException e)
                    {
                        return "missing:" + e.Message;
                    }
                    catch (ArgumentOutOfRangeException e) when (e.ParamName == "amount")
                    {
                        return "amount";
                    }
                    catch (ArgumentException)
                    {
                        return "argument";
                    }
                    catch (Exception e)
                    {
                        return "other:" + e.Message;
                    }
                }
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "bcl-exception-identity.ts",
            files["billing/bcl-exception-identity.ts"]
        );

        await Assert
            .That(
                diagnostics.Where(d =>
                    d.Code
                        is DiagnosticCodes.UnsupportedFeature
                            or DiagnosticCodes.UnanswerableRuntimeTypeTest
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task EachMappedClass_IsImportedFromItsOwnModule()
    {
        var output = TranspileGuard(
            """
            public static Exception Missing() => new System.Collections.Generic.KeyNotFoundException();

            public static Exception Canceled() => new System.Threading.Tasks.TaskCanceledException();

            public static Exception BadJson() => new System.Text.Json.JsonException("bad");

            public static Exception Todo() => new NotImplementedException();
            """
        );

        await Assert
            .That(output)
            .Contains(
                "import { KeyNotFoundException } from \"metano-runtime/system/exceptions/key-not-found-exception\";"
            );

        await Assert
            .That(output)
            .Contains(
                "import { TaskCanceledException } from \"metano-runtime/system/exceptions/task-canceled-exception\";"
            );

        await Assert
            .That(output)
            .Contains(
                "import { JsonException } from \"metano-runtime/system/exceptions/json-exception\";"
            );

        await Assert.That(output).Contains("return new KeyNotFoundException();");
        await Assert.That(output).Contains("return new TaskCanceledException();");
        await Assert.That(output).Contains("return new JsonException(\"bad\");");
        await Assert.That(output).Contains("return new NotImplementedException();");
        await Assert.That(output).Contains("static missing(): Exception {");
    }

    [Test]
    public async Task ConstructionOfAnUnmappedBclException_ReportsMs0001AndThrowsException()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public static class Guard
            {
                public static void Wait() { throw new System.IO.IOException("late"); }

                public static void Load(string path) { throw new System.IO.FileNotFoundException(null, path); }
            }
            """
        );

        var output = files["guard.ts"];

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        // The message stays .NET's; the type name is the exception's identity.
        await Assert
            .That(output)
            .Contains("throw namedException(\"System.IO\", \"IOException\", \"late\");");

        await Assert
            .That(output)
            .Contains(
                "throw namedException(\"System.IO\", \"FileNotFoundException\", \"Unable to find the specified file.\");"
            );

        await Assert.That(output).Contains("import { namedException } from \"metano-runtime\";");

        await Assert
            .That(warnings.Select(ReportedText))
            .IsEquivalentTo([
                "new System.IO.IOException(\"late\")",
                "new System.IO.FileNotFoundException(null, path)",
            ]);

        await Assert.That(warnings[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(warnings[0].Message)
            .IsEqualTo(
                "'new System.IO.IOException(\"late\")' constructs 'System.IO.IOException', which has no "
                    + "runtime class in the target: the exception loses its type, so catch clauses "
                    + "and type tests cannot tell it apart from other exceptions (spec 011 FR-014)."
            );
    }

    [Test]
    public async Task UserExceptionDerivingFromAnUnmappedBclException_ExtendsTheRootClass()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class GatewayTimeoutException : System.IO.IOException
            {
                public GatewayTimeoutException() { }
            }
            """
        );

        var output = files["gateway-timeout-exception.ts"];

        await Assert
            .That(output)
            .Contains("export class GatewayTimeoutException extends Exception {");

        await Assert.That(output).Contains("super(\"I/O error occurred.\");");

        // It no longer is a SystemException, so the derivation is reported at the base list.
        var warning = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert.That(ReportedText(warning)).IsEqualTo("System.IO.IOException");
    }

    [Test]
    [Arguments(
        "public static bool Test(object o) => o is System.IO.IOException;",
        "o is System.IO.IOException"
    )]
    [Arguments(
        "public static System.IO.IOException? Test(object o) => o as System.IO.IOException;",
        "o as System.IO.IOException"
    )]
    [Arguments(
        "public static bool Test(object o) => o is System.IO.IOException t && t.Message.Length > 0;",
        "System.IO.IOException t"
    )]
    [Arguments(
        "public static int Test(object o) => o switch { System.IO.IOException => 1, _ => 0 };",
        "System.IO.IOException"
    )]
    public async Task TypeTestOnAnUnmappedBclException_IsAnMs0045Error(string member, string site)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System;

            [Transpile]
            public static class Guard
            {
                {{member}}
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnanswerableRuntimeTypeTest);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(ReportedText(error)).IsEqualTo(site);

        await Assert
            .That(error.Message)
            .StartsWith($"'{site}' cannot be lowered: 'System.IO.IOException'");
    }

    [Test]
    public async Task ExportFromBclOnAUserSide_MapsAnotherExceptionToItsClass()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [assembly: ExportFromBcl(typeof(TimeoutException), ExportedName = "TimeoutError", FromPackage = "timeout-errors")]

            [Transpile]
            public static class Guard
            {
                public static void Wait() { throw new TimeoutException("late"); }

                public static bool IsTimeout(object o) => o is TimeoutException;
            }
            """
        );

        var output = files["guard.ts"];

        await Assert.That(output).Contains("import { TimeoutError } from \"timeout-errors\";");
        await Assert.That(output).Contains("throw new TimeoutError(\"late\");");
        await Assert.That(output).Contains("return o instanceof TimeoutError;");

        await Assert
            .That(
                diagnostics.Where(d =>
                    d.Code
                        is DiagnosticCodes.UnsupportedFeature
                            or DiagnosticCodes.UnanswerableRuntimeTypeTest
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task DartTarget_KeepsItsNativeExceptions_AndReportsNothing()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using System;
            using Metano.Annotations;

            [Transpile]
            public static class Guard
            {
                public static void Wait() { throw new TimeoutException("late"); }

                public static bool IsTimeout(object o) => o is TimeoutException;
            }
            """
        );

        await Assert.That(files["guard.dart"]).DoesNotContain("metano-runtime");

        await Assert
            .That(
                diagnostics.Where(d =>
                    d.Code
                        is DiagnosticCodes.UnsupportedFeature
                            or DiagnosticCodes.UnanswerableRuntimeTypeTest
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task DispatcherOnBclExceptions_TestsTheirRuntimeClasses()
    {
        var output = TranspileGuard(
            """
            public static string Handle(ArgumentException e) => "argument";

            public static string Handle(InvalidOperationException e) => "state";

            public static string Wait(System.IO.IOException e) => "io";

            public static string Wait(string reason) => reason;
            """
        );

        await Assert
            .That(output)
            .Contains("if (args.length === 1 && args[0] instanceof ArgumentException) {");

        await Assert
            .That(output)
            .Contains("if (args.length === 1 && args[0] instanceof InvalidOperationException) {");

        // An unmapped exception has no runtime class: its arm keeps the object check.
        await Assert
            .That(output)
            .Contains("if (args.length === 1 && typeof args[0] === \"object\") {");
    }

    [Test]
    public async Task NestedExceptionMappedByTheProject_ResolvesTheSameClassEverywhere()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [assembly: ExportFromBcl(typeof(Legacy.Errors.StaleException), ExportedName = "StaleError", FromPackage = "legacy-errors")]

            namespace Legacy
            {
                public static class Errors
                {
                    public class StaleException : Exception
                    {
                        public StaleException(string message) : base(message) { }
                    }
                }
            }

            [Transpile]
            public class RetryException : Legacy.Errors.StaleException
            {
                public RetryException() : base("retry") { }
            }

            [Transpile]
            public static class Guard
            {
                public static Legacy.Errors.StaleException Make() => new Legacy.Errors.StaleException("stale");

                public static bool IsStale(object o) => o is Legacy.Errors.StaleException;
            }
            """
        );

        var guard = files["guard.ts"];

        await Assert.That(guard).Contains("import { StaleError } from \"legacy-errors\";");
        await Assert.That(guard).Contains("static make(): StaleError {");
        await Assert.That(guard).Contains("return new StaleError(\"stale\");");
        await Assert.That(guard).Contains("return o instanceof StaleError;");
        await Assert.That(files["retry-exception.ts"]).Contains("extends StaleError {");

        await Assert
            .That(
                diagnostics.Where(d =>
                    d.Code
                        is DiagnosticCodes.UnsupportedFeature
                            or DiagnosticCodes.UnanswerableRuntimeTypeTest
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task RoleTheMappedClassDoesNotTake_IsFoldedIntoTheMessage()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [assembly: ExportFromBcl(typeof(ArgumentNullException), ExportedName = "NullArgument", FromPackage = "my-errors")]

            [Transpile]
            public static class Guard
            {
                public static Exception Missing(string name) => new ArgumentNullException(name);

                public static Exception Plain() => new ArgumentNullException();
            }
            """
        );

        var output = files["guard.ts"];

        await Assert
            .That(output)
            .Contains("return new NullArgument(`Value cannot be null. (Parameter '${name}')`);");

        await Assert.That(output).Contains("return new NullArgument();");
    }

    [Test]
    public async Task UserException_DeclaresItsDotNetIdentity()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            namespace Shop
            {
                [Transpile]
                public class UnsupportedCurrencyException : ArgumentException
                {
                    public UnsupportedCurrencyException(string code) : base("Unsupported " + code, "code") { }
                }

                [Transpile, Name("RenamedError")]
                public class RenamedException : Exception { }
            }
            """
        );

        var currency = files["shop/unsupported-currency-exception.ts"];

        await Assert
            .That(currency)
            .Contains("override readonly name: string = \"UnsupportedCurrencyException\";");

        await Assert
            .That(currency)
            .Contains(
                """
                  protected override get typeNamespace(): string {
                    return "Shop";
                  }
                """
            );

        await Assert
            .That(files["shop/renamed-error.ts"])
            .Contains("override readonly name: string = \"RenamedException\";");
    }

    [Test]
    public async Task ArgumentGuards_PassTheCallerArgumentExpression()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public static class Guard
            {
                public static void Check(string? name, string text, int count, int limit)
                {
                    ArgumentNullException.ThrowIfNull(name);
                    ArgumentNullException.ThrowIfNull(name, "custom");
                    ArgumentException.ThrowIfNullOrWhiteSpace(text);
                    ArgumentOutOfRangeException.ThrowIfNegative(count);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(count, limit);
                }
            }
            """
        );

        var output = files["guard.ts"];

        await Assert.That(output).Contains("throwIfNull(name, \"name\");");
        await Assert.That(output).Contains("throwIfNull(name, \"custom\");");
        await Assert.That(output).Contains("throwIfNullOrWhiteSpace(text, \"text\");");
        await Assert.That(output).Contains("throwIfNegative(count, \"count\");");
        await Assert.That(output).Contains("throwIfGreaterThan(count, limit, \"count\");");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task UnmappedStaticMemberOfAMappedException_IsStillReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class Connection
            {
                public bool Closed { get; set; }

                public void Use() => ObjectDisposedException.ThrowIf(Closed, this);
            }
            """
        );

        await Assert
            .That(diagnostics.Select(d => d.Message))
            .Contains(message => message.Contains("System.ObjectDisposedException.ThrowIf"));
    }

    [Test]
    public async Task RootExceptionTests_AcceptAnyJavaScriptError()
    {
        var output = TranspileGuard(
            """
            public static bool IsException(object o) => o is Exception;

            public static string Kind(Exception e) =>
                e switch
                {
                    ArgumentException => "argument",
                    Exception x => "exception:" + x.Message,
                };
            """
        );

        await Assert.That(output).Contains("return o instanceof Error;");
        await Assert.That(output).Contains("if ($s instanceof ArgumentException) {");
        await Assert.That(output).Contains("if ($s instanceof Error) {");
    }

    [Test]
    public async Task InnerParameterNamedInner_IsTheInnerException()
    {
        var output = TranspileGuard(
            """
            public static Exception Todo(Exception cause) => new NotImplementedException("m", cause);

            public static Exception Missing(Exception cause) => new MissingMethodException("m", cause);
            """
        );

        await Assert.That(output).Contains("return new NotImplementedException(\"m\", cause);");
        await Assert.That(output).Contains("return new MissingMethodException(\"m\", cause);");
    }

    [Test]
    public async Task TimeoutException_IsMapped()
    {
        var output = TranspileGuard(
            "public static Exception Late() => new TimeoutException(\"late\");"
        );

        await Assert
            .That(output)
            .Contains(
                "import { TimeoutException } from \"metano-runtime/system/exceptions/timeout-exception\";"
            );

        await Assert.That(output).Contains("return new TimeoutException(\"late\");");
    }

    private static string TranspileGuard(string members) =>
        TranspileHelper.Transpile(
            $$"""
            using System;

            [Transpile]
            public static class Guard
            {
                {{members}}
            }
            """
        )["guard.ts"];

    private static string ReportedText(MetanoDiagnostic diagnostic)
    {
        if (diagnostic.Location is not { SourceTree: not null } location)
            throw new InvalidOperationException($"'{diagnostic.Message}' has no source location.");

        return location.SourceTree.GetText().ToString(location.SourceSpan);
    }
}
