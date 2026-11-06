using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The 64-bit dispatch and guard surface of spec 012 slice 012f (FR-048,
/// FR-049) in the staging <see cref="Int64Representation.BigInt"/> mode: the
/// dispatcher checks a 64-bit argument with the <c>bigint</c> range checks and
/// tries <c>long</c>, <c>ulong</c>, <c>BigInteger</c> in that order (OQ-6),
/// and the synthesized guards test a 64-bit member with
/// <c>typeof === "bigint"</c>. The Number mode keeps the shipped output
/// (pinned by <see cref="Int64OverloadNamingTests"/> and
/// <see cref="Int64StagingSwitchTests"/>).
/// </summary>
public class Int64DispatchGuardTests
{
    /// <summary>
    /// Same-arity overloads that differ only among the 64-bit integer kinds,
    /// with an <c>int</c> overload beside them: the dispatcher's guards
    /// overlap only among <c>long</c>, <c>ulong</c> and <c>BigInteger</c>, so
    /// the golden pins both the OQ-6 order and the distinct fast-path
    /// suffixes of spec 013 (the exact CLR spellings).
    /// </summary>
    private const string DispatchSource = """
        using System.Numerics;

        [Transpile]
        public class Gates
        {
            public string Pick(int x) => "int";
            public string Pick(long x) => "long";
            public string Pick(ulong x) => "ulong";
            public string Pick(BigInteger x) => "bigInteger";
        }
        """;

    private const string GuardSource = """
        [Transpile, GenerateGuard]
        public record Snowflake(long Id, ulong Mask, long? Parent);
        """;

    [Test]
    public async Task Dispatch_BigIntMode_Golden()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            DispatchSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["gates.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-guards-dispatch.ts"));
    }

    /// <summary>
    /// A <c>bigint</c> argument reaches the first guard of the OQ-6 order: a
    /// value that fits <c>long</c> picks the <c>long</c> overload even when it
    /// also fits <c>ulong</c>, and one beyond it walks down the chain.
    /// </summary>
    [Test]
    public async Task Dispatch_BigIntMode_TriesLongThenUlongThenBigInteger()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            DispatchSource,
            Int64Representation.BigInt
        );

        var output = files["gates.ts"];

        await Assert.That(output).Contains("isInt64Value(args[0])");
        await Assert.That(output).Contains("isUInt64Value(args[0])");
        await Assert.That(output).Contains("isBigInt(args[0])");

        var intBranch = output.IndexOf("isInt32(args[0])", StringComparison.Ordinal);
        var longBranch = output.IndexOf("isInt64Value(args[0])", StringComparison.Ordinal);
        var ulongBranch = output.IndexOf("isUInt64Value(args[0])", StringComparison.Ordinal);
        var bigIntegerBranch = output.IndexOf("isBigInt(args[0])", StringComparison.Ordinal);

        await Assert.That(longBranch).IsGreaterThan(intBranch);
        await Assert.That(ulongBranch).IsGreaterThan(longBranch);
        await Assert.That(bigIntegerBranch).IsGreaterThan(ulongBranch);
    }

    [Test]
    public async Task Guard_BigIntMode_TestsBigintMembers()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            GuardSource,
            Int64Representation.BigInt
        );

        var output = files["snowflake.ts"];

        await Assert.That(output).Contains("typeof v.id === \"bigint\"");
        await Assert.That(output).Contains("typeof v.mask === \"bigint\"");
        // A nullable member admits null before the bigint test.
        await Assert.That(output).Contains("v.parent == null || typeof v.parent === \"bigint\"");
    }

    /// <summary>
    /// The Number mode keeps the shipped <c>number</c> checks and guard
    /// spellings, byte for byte (NFR-001).
    /// </summary>
    [Test]
    public async Task Dispatch_Guard_NumberMode_KeepsTheShippedChecks()
    {
        var (dispatchFiles, _) = TranspileHelper.TranspileWithDiagnostics(
            DispatchSource,
            Int64Representation.Number
        );
        var (guardFiles, _) = TranspileHelper.TranspileWithDiagnostics(
            GuardSource,
            Int64Representation.Number
        );

        await Assert
            .That(dispatchFiles["gates.ts"])
            .Contains("import { isBigInt, isInt32, isInt64, isUInt64 } from \"metano-runtime\";");
        await Assert.That(dispatchFiles["gates.ts"]).DoesNotContain("isInt64Value");
        await Assert.That(guardFiles["snowflake.ts"]).Contains("typeof v.id === \"number\"");
        await Assert.That(guardFiles["snowflake.ts"]).DoesNotContain("\"bigint\"");
    }
}
