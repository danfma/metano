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
                public static void Wait() { throw new TimeoutException("late"); }

                public static void Load(string path) { throw new System.IO.FileNotFoundException(null, path); }
            }
            """
        );

        var output = files["guard.ts"];

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert
            .That(output)
            .Contains("throw new Exception(\"System.TimeoutException: late\");");

        await Assert
            .That(output)
            .Contains(
                "throw new Exception(\"System.IO.FileNotFoundException: Unable to find the specified file.\");"
            );

        await Assert
            .That(warnings.Select(ReportedText))
            .IsEquivalentTo([
                "new TimeoutException(\"late\")",
                "new System.IO.FileNotFoundException(null, path)",
            ]);

        await Assert.That(warnings[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(warnings[0].Message)
            .IsEqualTo(
                "'new TimeoutException(\"late\")' constructs 'System.TimeoutException', which has no "
                    + "runtime class in the target: it is emitted as 'Exception' with the type name "
                    + "in its message, so catch clauses and type tests cannot tell it apart (spec 011 "
                    + "FR-014)."
            );
    }

    [Test]
    public async Task UserExceptionDerivingFromAnUnmappedBclException_ExtendsTheRootClass()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class GatewayTimeoutException : TimeoutException
            {
                public GatewayTimeoutException() { }
            }
            """
        );

        var output = files["gateway-timeout-exception.ts"];

        await Assert
            .That(output)
            .Contains("export class GatewayTimeoutException extends Exception {");

        await Assert.That(output).Contains("super(\"The operation has timed out.\");");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    [Arguments(
        "public static bool Test(object o) => o is TimeoutException;",
        "o is TimeoutException"
    )]
    [Arguments(
        "public static TimeoutException? Test(object o) => o as TimeoutException;",
        "o as TimeoutException"
    )]
    [Arguments(
        "public static bool Test(object o) => o is TimeoutException t && t.Message.Length > 0;",
        "TimeoutException t"
    )]
    [Arguments(
        "public static int Test(object o) => o switch { TimeoutException => 1, _ => 0 };",
        "TimeoutException"
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
            .StartsWith($"'{site}' cannot be lowered: 'System.TimeoutException'");
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
