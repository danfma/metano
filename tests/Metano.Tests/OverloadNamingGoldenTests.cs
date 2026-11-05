namespace Metano.Tests;

/// <summary>
/// The fast-path names of spec 013 slice 013a (FR-001–FR-003,
/// contracts/naming.md § Worked examples), pinned as goldens that compile
/// under <c>tsgo --strict --noImplicitOverride</c>. Before 013a the
/// <c>uint</c> / <c>int</c>, <c>List&lt;int&gt;</c> / <c>int[]</c> and
/// same-named-type families emitted duplicate fast paths (TS2393) and the
/// zero-argument <c>Now()</c> took the dispatcher's own name (TS2393 + TS2385,
/// research R7).
/// </summary>
public class OverloadNamingGoldenTests
{
    [Test]
    public async Task TypeSpellings_UseExactClrNames()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Point
            {
                public int X;
            }

            [Transpile]
            public class Names
            {
                public int Add(int x, int y) => x + y;

                public int Add(Point p) => p.X;

                public string F(int a) => "int";

                public string F(string a) => "string";

                public string G(int v) => "int";

                public string G(long v) => "long";

                public string U(int a) => "int";

                public string U(uint a) => "uint";

                public string W(long a) => "long";

                public string W(ulong a) => "ulong";

                public int L(List<int> xs) => 1;

                public int L(int[] xs) => 2;

                public int Sum(int[] xs) => 1;

                public int Sum(double[] xs) => 2;

                public string H(int? maybe) => "int";

                public string H(string? maybe) => "string";

                public string Format(string fmt, int count) => fmt;

                public string Format(string fmt, params object[] args) => fmt;

                public T Identity<T>(T value) => value;

                public string Identity(string value) => value;
            }
            """
        );

        await Assert
            .That(result["names.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-names-types.ts"));
    }

    [Test]
    public async Task CollidingTypeSpellings_ClimbTheirQualifiers()
    {
        // The two project `Decimal` types emit one shared `Decimal` identifier,
        // and this file binds a single intra-project import for it — the
        // project-vs-project alias emission is tracked in #504.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace Numerics
            {
                [Transpile]
                public class Decimal
                {
                    public int V;
                }
            }

            namespace Other
            {
                [Transpile]
                public class Decimal
                {
                    public int V;
                }
            }

            [Transpile]
            public class A
            {
                [Transpile]
                public class Id
                {
                    public int V;
                }
            }

            [Transpile]
            public class B
            {
                [Transpile]
                public class Id
                {
                    public int V;
                }
            }

            [Transpile]
            public class Climbs
            {
                public int K(Numerics.Decimal d) => 1;

                public int K(Other.Decimal d) => 2;

                public int J(Numerics.Decimal d, int n) => 1;

                public int J(Other.Decimal d, int n) => 2;

                public int M(List<A.Id> ids) => 1;

                public int M(List<B.Id> ids) => 2;
            }
            """
        );

        await Assert
            .That(result["climbs.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-names-climb.ts"));
    }

    [Test]
    public async Task ParameterlessOverload_IsNamedDefault()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Clock
            {
                public int Now() => 0;

                public int Now(int tick) => tick;
            }
            """
        );

        await Assert
            .That(result["clock.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-names-default.ts"));
    }

    [Test]
    public async Task OperatorOverloads_UseTheTypeSpellings()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public static Money operator +(Money a, int b) => a;

                public static Money operator +(Money a, uint b) => a;

                public static Money operator +(Money a, Money b) => a;
            }
            """
        );

        await Assert
            .That(result["money.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-names-operators.ts"));
    }

    [Test]
    public async Task DeadEnd_EmitsPositionalMarkers_AndReportsMS0003()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Generic
            {
                public int M<T>(int x) => 1;

                public int M(int x) => 2;
            }
            """
        );

        var deadEnd = diagnostics.Single(d => d.Code == "MS0003");

        await Assert
            .That(files["generic.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-names-dead-end.ts"));
        await Assert.That(deadEnd.Message).Contains("'Int32$1' and 'Int32$2'");
    }
}
