using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A record's or plain struct's synthesized value-equality members join the user
/// overload group spelled like them (spec 013 FR-038–FR-045, DECIDED-33): the
/// dispatcher keeps the name the runtime calls (<c>equals</c>, <c>hashCode</c>,
/// <c>with</c>), the synthesized member takes the pinned <c>ByValue</c> fast path,
/// derived types anchor on it, and C# calls bind statically to the fast paths.
/// </summary>
public class ValueEqualityJoinTests
{
    private const string Collisions = """
        [Transpile]
        public record RTol(double V) { public bool Equals(RTol other, double tol) => System.Math.Abs(other.V - V) <= tol; }

        [Transpile]
        public record QWith(int A, string B) { public QWith With(int a) => new QWith(a, B); }

        [Transpile]
        public struct QHashM { public int V; public QHashM(int v) { V = v; } public int HashCode(int seed) => V + seed; }

        [Transpile]
        public struct KEntry { public int HashCode; public string Key; public KEntry(int h, string k) { HashCode = h; Key = k; } }

        [Transpile]
        public static class Use
        {
            public static bool Near(RTol a, RTol b) => a.Equals(b, 0.1) && a.Equals(b);
            public static QWith Copy(QWith a) => a with { A = 5 };
            public static QWith Rebuild(QWith a) => a.With(7);
            public static int Hash(QHashM a) => a.HashCode(2) + a.GetHashCode();
        }
        """;

    /// <summary>
    /// The synthesized member joins the user group as one more overload: the
    /// dispatcher tries it first for an argument of the receiver's own type, the
    /// user branches next, and it again for any other one-argument call.
    /// </summary>
    [Test]
    public async Task UserGroup_TakesTheSynthesizedMemberAsAnOverload()
    {
        var tol = TranspileHelper.Transpile(Collisions)["r-tol.ts"];

        await Assert.That(tol).Contains("equalsOtherTol(other: RTol, tol: number): boolean {");
        await Assert.That(tol).Contains("equalsByValue(other: any): boolean {");
        await Assert.That(tol).Contains("equals(other: RTol, tol: number): boolean;");
        await Assert.That(tol).Contains("equals(other: unknown): boolean;");

        await Assert
            .That(tol)
            .Contains(
                "if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {\n      return this.equalsByValue(args[0]);"
            );

        await Assert
            .That(tol.IndexOf("this.equalsOtherTol(", StringComparison.Ordinal))
            .IsLessThan(
                tol.LastIndexOf("return this.equalsByValue(args[0]);", StringComparison.Ordinal)
            );
    }

    /// <summary>
    /// <c>hashCode()</c> and <c>with</c> join <c>HashCode(int)</c> and a record's
    /// <c>With(int)</c> the same way; a field <c>HashCode</c> keeps its name and
    /// no <c>hashCode()</c> is synthesized.
    /// </summary>
    [Test]
    public async Task HashCodeAndCopy_JoinTheirGroups()
    {
        var files = TranspileHelper.Transpile(Collisions);

        await Assert.That(files["q-with.ts"]).Contains("withA(a: number): QWith {");
        await Assert
            .That(files["q-with.ts"])
            .Contains("withByValue(overrides?: Partial<QWith>): QWith {");
        await Assert.That(files["q-with.ts"]).Contains("with(overrides?: Partial<QWith>): QWith;");
        await Assert.That(files["q-hash-m.ts"]).Contains("hashCodeSeed(seed: number): number {");
        await Assert.That(files["q-hash-m.ts"]).Contains("hashCodeByValue(): number {");
        await Assert.That(files["q-hash-m.ts"]).Contains("hashCode(...args: unknown[]): number {");
        await Assert.That(files["k-entry.ts"]).DoesNotContain("hashCode(): number");
        await Assert.That(files["k-entry.ts"]).DoesNotContain("HashCode");
    }

    /// <summary>
    /// C# calls bind statically: a user overload to its fast path, the implicit
    /// <c>Equals</c> / <c>GetHashCode</c> and a <c>with</c> expression to the
    /// synthesized member's (FR-043).
    /// </summary>
    [Test]
    public async Task CallSites_BindToTheFastPaths()
    {
        var use = TranspileHelper.Transpile(Collisions)["use.ts"];

        await Assert.That(use).Contains("a.equalsOtherTol(b, 0.1) && a.equalsByValue(b)");
        await Assert.That(use).Contains("a.withByValue({ a: 5 })");
        await Assert.That(use).Contains("a.withA(7)");
        await Assert.That(use).Contains("a.hashCodeSeed(2) + a.hashCodeByValue()");
    }

    /// <summary>A type no user member collides with keeps today's members (NFR-001).</summary>
    [Test]
    public async Task NoCollision_KeepsTheSynthesizedMembers()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Plain(int A);

            [Transpile]
            public static class Use { public static bool Same(Plain a, Plain b) => a.Equals(b) && (a with { A = 1 }).A == 1; }
            """
        );

        await Assert.That(files["plain.ts"]).Contains("equals(other: any): boolean {");
        await Assert.That(files["plain.ts"]).DoesNotContain("ByValue");
        await Assert.That(files["use.ts"]).Contains("a.equals(b) && a.with({ a: 1 }).a === 1");
    }

    /// <summary>
    /// A derived record anchors on the members its base inserted: it overrides
    /// the stable fast paths with no dispatcher of its own, and chains to the
    /// base's fast path, never through the base dispatcher (FR-041).
    /// </summary>
    [Test]
    public async Task DerivedRecord_OverridesTheBaseFastPaths()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract record ABase(double V) { public virtual bool Equals(ABase other, double tol) => false; public abstract int HashCode(int seed); }

            [Transpile]
            public record ADer(double V, int K) : ABase(V) { public override bool Equals(ABase o, double t) => System.Math.Abs(o.V - V) <= t; public override int HashCode(int s) => s + 1; }

            [Transpile]
            public record AOther(double V) : ABase(V) { public override int HashCode(int seed) => seed; }
            """
        );

        var der = files["a-der.ts"];

        await Assert.That(files["a-base.ts"]).Contains("equals(...args: unknown[]): boolean {");
        await Assert.That(der).Contains("override equalsOtherTol(o: ABase, t: number): boolean {");
        await Assert.That(der).Contains("override equalsByValue(other: any): boolean {");
        await Assert.That(der).Contains("super.equalsByValue(other)");
        await Assert.That(der).Contains("override hashCodeByValue(): number {");
        await Assert.That(der).Contains("hc.add(super.hashCodeByValue());");
        await Assert.That(der).DoesNotContain("...args");
        await Assert
            .That(files["a-other.ts"])
            .Contains("override equalsByValue(other: any): boolean {");
        await Assert.That(files["a-other.ts"]).Contains("override hashCodeByValue(): number {");
    }

    /// <summary>
    /// When only the middle of three records joins, the middle's dispatcher
    /// overrides the top's plain member and its fast path chains to it; the
    /// bottom anchors on the middle.
    /// </summary>
    [Test]
    public async Task MiddleRecordJoins_TopKeepsItsPlainMembers()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record HTop(int A);

            [Transpile]
            public record HMid(int A, int B) : HTop(A) { public bool Equals(HMid o, int slack) => System.Math.Abs(o.B - B) <= slack; }

            [Transpile]
            public record HLow(int A, int B, int C) : HMid(A, B);
            """
        );

        await Assert.That(files["h-top.ts"]).Contains("equals(other: any): boolean {");
        await Assert
            .That(files["h-mid.ts"])
            .Contains("override equals(...args: unknown[]): boolean {");
        await Assert.That(files["h-mid.ts"]).Contains("return super.equals(other) && ");
        await Assert
            .That(files["h-low.ts"])
            .Contains("override equalsByValue(other: any): boolean {");
        await Assert.That(files["h-low.ts"]).Contains("return super.equalsByValue(other) && ");
    }

    /// <summary>
    /// A declaration that replaces a synthesized member supplies its body (C1):
    /// an <c>override GetHashCode()</c> beside <c>HashCode(int)</c> is emitted once,
    /// as the synthesized member's fast path, and a record's <c>Equals(R)</c> is
    /// reached through <c>Equals(object)</c> as C# routes it.
    /// </summary>
    [Test]
    public async Task ReplacingDeclarations_SupplyTheBody()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record UHash(int V) { public override int GetHashCode() => 7; public int HashCode(int seed) => V + seed; }

            [Transpile]
            public record UBoth(double V)
            {
                public virtual bool Equals(UBoth? o) => o is not null && o.V == V;
                public override int GetHashCode() => 1;
                public bool Equals(UBoth o, double tol) => System.Math.Abs(o.V - V) <= tol;
            }
            """
        );

        var hash = files["u-hash.ts"];

        await Assert.That(hash).Contains("hashCodeByValue(): number {\n    return 7;");
        await Assert.That(hash).DoesNotContain("hashCode(): number {");

        await Assert
            .That(files["u-both.ts"])
            .Contains("return this.equalsO(other instanceof UBoth ? other : null);");
    }

    /// <summary>
    /// An overload the dispatcher cannot tell from the synthesized member keeps
    /// its fast path, gets no branch and reports <c>MS0063</c>; a struct's
    /// <c>Equals(S)</c> no longer replaces the field comparison (FR-045).
    /// </summary>
    [Test]
    public async Task IndistinguishableOverload_ReportsAmbiguousDispatch()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct USBoth { public int X; public USBoth(int x) { X = x; } public bool Equals(USBoth o) => o.X == X; public bool Equals(USBoth o, int slack) => System.Math.Abs(o.X - X) <= slack; }

            [Transpile]
            public record GRec<T>(T Value) { public GRec<T> With(T v) => new GRec<T>(v); }
            """
        );

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .ToList();

        await Assert.That(warnings.Count).IsEqualTo(2);
        await Assert
            .That(diagnostics.Any(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsFalse();
        await Assert.That(files["us-both.ts"]).Contains("equalsByValue(other: any): boolean {");
        await Assert.That(files["us-both.ts"]).DoesNotContain("this.equalsO(args[0]");
        await Assert.That(files["g-rec.ts"]).Contains("withV(v: T): GRec<T> {");
    }

    /// <summary>
    /// An indistinguishable overload that implements a transpiled interface
    /// member is an error: callers through the interface would reach the
    /// synthesized member (FR-042).
    /// </summary>
    [Test]
    public async Task IndistinguishableInterfaceImplementation_ReportsAnError()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IShapeEq { bool Equals(IShapeEq other); double V { get; } }

            [Transpile]
            public record Shape(double V) : IShapeEq { public bool Equals(IShapeEq other) => other.V == V; }
            """
        );

        var error = diagnostics.Single(d => d.Severity == MetanoDiagnosticSeverity.Error);

        await Assert.That(error.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(error.Message).Contains("implements 'IShapeEq.Equals'");
    }

    /// <summary>
    /// A consumer record deriving from a library record that joined overrides its
    /// fast path; one whose base joined only through a private overload, which a
    /// reference assembly strips, keeps the plain override of the base's name
    /// (contracts/value-equality-members.md § 8).
    /// </summary>
    [Test]
    public async Task CrossPackage_FollowsTheVisibleGroups()
    {
        const string library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]
            namespace Lib;
            public record PubTol(double V) { public bool Equals(PubTol o, double tol) => System.Math.Abs(o.V - V) <= tol; }
            public record PrivTol(double V) { private bool Equals(PrivTol o, double tol) => System.Math.Abs(o.V - V) <= tol; public bool Near(PrivTol o) => Equals(o, 0.1); }
            """;

        const string consumer = """
            [assembly: TranspileAssembly]
            namespace App;
            public record PubDer(double V, int K) : Lib.PubTol(V);
            public record PrivDer(double V, int K) : Lib.PrivTol(V);
            """;

        var files = TranspileHelper.TranspileWithLibraryImage(
            library,
            consumer,
            referenceAssembly: true
        );

        await Assert
            .That(files["app/pub-der.ts"])
            .Contains("override equalsByValue(other: any): boolean {");
        await Assert
            .That(files["app/priv-der.ts"])
            .Contains("override equals(other: any): boolean {");
        await Assert.That(files["app/priv-der.ts"]).DoesNotContain("ByValue");
    }

    /// <summary>
    /// The joined shapes together — user groups, a hierarchy, a folded and a
    /// routed replacement — as one golden that type-checks under
    /// <c>--strict --noImplicitOverride</c>.
    /// </summary>
    [Test]
    public async Task JoinedShapes_MatchTheGolden()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("value-equality-join")]
            public record RTol(double V) { public bool Equals(RTol other, double tol) => System.Math.Abs(other.V - V) <= tol; }

            [Transpile, EmitInFile("value-equality-join")]
            public record QWith(int A, string B) { public QWith With(int a) => new QWith(a, B); }

            [Transpile, EmitInFile("value-equality-join")]
            public struct QHashM { public int V; public QHashM(int v) { V = v; } public int HashCode(int seed) => V + seed; }

            [Transpile, EmitInFile("value-equality-join")]
            public abstract record ABase(double V) { public virtual bool Equals(ABase other, double tol) => false; public abstract int HashCode(int seed); }

            [Transpile, EmitInFile("value-equality-join")]
            public record ADer(double V, int K) : ABase(V) { public override bool Equals(ABase o, double t) => System.Math.Abs(o.V - V) <= t; public override int HashCode(int s) => s + 1; }

            [Transpile, EmitInFile("value-equality-join")]
            public record HTop(int A);

            [Transpile, EmitInFile("value-equality-join")]
            public record HMid(int A, int B) : HTop(A) { public bool Equals(HMid o, int slack) => System.Math.Abs(o.B - B) <= slack; }

            [Transpile, EmitInFile("value-equality-join")]
            public record HLow(int A, int B, int C) : HMid(A, B);

            [Transpile, EmitInFile("value-equality-join")]
            public struct UObjEq { public int X; public UObjEq(int x) { X = x; } public override bool Equals(object? o) => o is UObjEq u && u.X == X; public override int GetHashCode() => X; public bool Equals(UObjEq o, int slack) => System.Math.Abs(o.X - X) <= slack; }

            [Transpile, EmitInFile("value-equality-join")]
            public record UBoth(double V)
            {
                public virtual bool Equals(UBoth? o) => o is not null && o.V == V;
                public override int GetHashCode() => 1;
                public bool Equals(UBoth o, double tol) => System.Math.Abs(o.V - V) <= tol;
            }

            [Transpile, EmitInFile("value-equality-join")]
            public static class JoinUse
            {
                public static bool Near(RTol a, RTol b) => a.Equals(b, 0.1) && a.Equals(b);
                public static QWith Copy(QWith a) => a with { A = 5 };
                public static int Hash(QHashM a) => a.HashCode(2) + a.GetHashCode();
                public static bool Low(HLow a, HLow b) => a.Equals(b) && a.GetHashCode() == b.GetHashCode();
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "value-equality-join.ts",
            files["value-equality-join.ts"]
        );
    }
}
