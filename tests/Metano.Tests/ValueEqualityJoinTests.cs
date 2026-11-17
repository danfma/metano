using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Tests.IR;

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
            public record NEq(double V) { [Name("equals")] public bool Same(NEq o, double tol) => System.Math.Abs(o.V - V) <= tol; }

            [Transpile, EmitInFile("value-equality-join")]
            public record NHash(int V) { [Name("hashCode")] public int Seeded(int seed) => V + seed; }

            [Transpile, EmitInFile("value-equality-join")]
            public record NWith(int A, int B) { [Name("with")] public NWith Replace(int a) => new NWith(a, B); }

            [Transpile, EmitInFile("value-equality-join")]
            public struct SEqOnly : System.IEquatable<SEqOnly> { public int X; public SEqOnly(int x) { X = x; } public bool Equals(SEqOnly o) => o.X == X; public override int GetHashCode() => X; }

            [Transpile, EmitInFile("value-equality-join")]
            public struct SEqJoin : System.IEquatable<SEqJoin> { public int X; public SEqJoin(int x) { X = x; } public bool Equals(SEqJoin o) => o.X == X; public bool Equals(SEqJoin o, int slack) => System.Math.Abs(o.X - X) <= slack; }

            [Transpile, EmitInFile("value-equality-join")]
            public struct SExpl : System.IEquatable<SExpl> { public int X; public SExpl(int x) { X = x; } bool System.IEquatable<SExpl>.Equals(SExpl o) => o.X == X; }

            [Transpile, EmitInFile("value-equality-join")]
            public record HZero(int V) { public int HashCode() => 99; }

            [Transpile, EmitInFile("value-equality-join")]
            public struct Pt : System.IFormattable { public int X; public Pt(int x) { X = x; } string System.IFormattable.ToString(string? f, System.IFormatProvider? p) => "P"; }

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

    /// <summary>
    /// A lone method whose <c>[Name]</c> spells a synthesized member's name joins
    /// it like a method whose C# name does (FR-039): the dispatcher keeps the
    /// shared name, and the <c>[Name]</c> that named the group pins no suffix.
    /// </summary>
    [Test]
    public async Task LoneMethodNamedLikeASynthesizedMember_Joins()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record NEq(double V) { [Name("equals")] public bool Same(NEq o, double tol) => System.Math.Abs(o.V - V) <= tol; }

            [Transpile]
            public record NEq2(double V) { [Name("equals")] public bool Equals(NEq2 o, double tol) => System.Math.Abs(o.V - V) <= tol; }

            [Transpile]
            public record NHash(int V) { [Name("hashCode")] public int Seeded(int seed) => V + seed; }

            [Transpile]
            public record NWith(int A, int B) { [Name("with")] public NWith Replace(int a) => new NWith(a, B); }

            [Transpile]
            public static class Use
            {
                public static bool S(NEq a) => a.Same(a, 0.1);

                public static int H(NHash a) => a.Seeded(1);
                public static NWith W(NWith a) => a.Replace(2) with { B = 3 };
            }
            """
        );

        await Assert.That(files["n-eq.ts"]).Contains("equalsOTol(o: NEq, tol: number): boolean {");
        await Assert.That(files["n-eq.ts"]).Contains("equals(...args: unknown[]): boolean {");
        await Assert.That(files["n-eq2.ts"]).Contains("equalsByValue(other: any): boolean {");
        await Assert.That(files["n-hash.ts"]).Contains("hashCodeSeed(seed: number): number {");
        await Assert.That(files["n-hash.ts"]).Contains("hashCode(...args: unknown[]): number {");
        await Assert.That(files["n-with.ts"]).Contains("withA(a: number): NWith {");

        await Assert
            .That(files["n-with.ts"])
            .Contains("withByValue(overrides?: Partial<NWith>): NWith {");

        await Assert.That(files["use.ts"]).Contains("a.equalsOTol(a, 0.1)");
        await Assert.That(files["use.ts"]).Contains("a.hashCodeSeed(1)");
        await Assert.That(files["use.ts"]).Contains("a.withA(2).withByValue({ b: 3 })");
    }

    /// <summary>
    /// A struct's <c>Equals(S)</c> that implements <c>IEquatable&lt;S&gt;</c>
    /// (implicitly or explicitly) or a transpiled interface's <c>Equals(S)</c>
    /// replaces the synthesized equality (FR-045): alone it stays the type's
    /// <c>equals</c>, which <c>EqualityComparer&lt;S&gt;.Default</c> calls; beside
    /// another overload the synthesized member routes to it. Only a transpiled
    /// interface's replacement without an <c>override Equals(object)</c> warns.
    /// </summary>
    [Test]
    public async Task InterfaceImplementingStructEquals_ReplacesTheSynthesizedEquality()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IEq<T> { bool Equals(T o); }

            [Transpile]
            public struct SEqOnly : System.IEquatable<SEqOnly> { public int X; public SEqOnly(int x) { X = x; } public bool Equals(SEqOnly o) => o.X == X; public override int GetHashCode() => X; }

            [Transpile]
            public struct SEqJoin : System.IEquatable<SEqJoin> { public int X; public SEqJoin(int x) { X = x; } public bool Equals(SEqJoin o) => o.X == X; public bool Equals(SEqJoin o, int slack) => System.Math.Abs(o.X - X) <= slack; }

            [Transpile]
            public struct SExpl : System.IEquatable<SExpl> { public int X; public SExpl(int x) { X = x; } bool System.IEquatable<SExpl>.Equals(SExpl o) => o.X == X; }

            [Transpile]
            public struct SE : IEq<SE> { public int X; public SE(int x) { X = x; } public bool Equals(SE o) => o.X == X; }

            [Transpile]
            public struct SEO : IEq<SEO> { public int X; public SEO(int x) { X = x; } public bool Equals(SEO o) => o.X == X; public override bool Equals(object? o) => o is SEO s && Equals(s); public override int GetHashCode() => X; }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        // Only SE, whose equality a transpiled interface alone replaces, is warned.
        var warning = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch);

        await Assert.That(warning.Message).Contains("'SE.Equals");

        await Assert.That(files["s-eq-only.ts"]).Contains("equals(o: SEqOnly): boolean {");
        await Assert.That(files["s-eq-only.ts"]).DoesNotContain("ByValue");

        await Assert
            .That(files["s-eq-join.ts"])
            .Contains("return other instanceof SEqJoin ? this.equalsO(other) : false;");

        await Assert.That(files["s-expl.ts"]).Contains("equals(o: SExpl): boolean {");
        await Assert.That(files["se.ts"]).Contains("equals(o: SE): boolean {");
        await Assert.That(files["se.ts"]).DoesNotContain("ByValue");
        await Assert.That(files["seo.ts"]).Contains("equalsSEO(o: SEO): boolean {");
        await Assert.That(files["seo.ts"]).DoesNotContain("ByValue");
    }

    /// <summary>
    /// A struct's <c>Equals(S)</c> that implements nothing replaces nothing
    /// (<c>ValueType.Equals</c> never calls it): it is shadowed and reports
    /// <c>MS0063</c>.
    /// </summary>
    [Test]
    public async Task StructEqualsWithoutInterface_IsShadowed()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct SEqNoIface { public int X; public SEqNoIface(int x) { X = x; } public bool Equals(SEqNoIface o) => o.X == X; }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch))
            .IsEqualTo(1);

        await Assert
            .That(files["s-eq-no-iface.ts"])
            .Contains("equalsByValue(other: any): boolean {");
    }

    /// <summary>
    /// An overload that overlaps the synthesized <c>hashCode()</c> at zero
    /// arguments is shadowed (FR-042): <c>MS0063</c>, no duplicate signature and no
    /// unreachable branch; one that implements a transpiled interface member is
    /// an error.
    /// </summary>
    [Test]
    public async Task ZeroArgumentOverlap_IsShadowed()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record HZero(int V) { public int HashCode() => 99; }

            [Transpile]
            public record HOpt(int V) { public int HashCode(int seed = 0) => V + seed; }
            """
        );

        var zero = files["h-zero.ts"];

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch))
            .IsEqualTo(2);

        await Assert.That(zero.Split("hashCode(): number;").Length).IsEqualTo(2);
        await Assert.That(zero).DoesNotContain("this.hashCodeDefault()");

        var (_, errors) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IHasHash { int HashCode(); }

            [Transpile]
            public record F6(int V) : IHasHash { public int HashCode() => 99; }
            """
        );

        var error = errors.Single(d => d.Severity == MetanoDiagnosticSeverity.Error);

        await Assert.That(error.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(error.Message).Contains("implements 'IHasHash.HashCode'");
    }

    /// <summary>
    /// The synthesized fast path keeps its name (FR-042): a user overload whose
    /// fast path meets it moves (<c>MS0067</c>); a user member holding it is an
    /// error (<c>MS0068</c>).
    /// </summary>
    [Test]
    public async Task FastPathClashes_MoveTheUserMember()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record F7(int V) { public bool Equals(int byValue) => byValue == V; }
            """
        );

        var renamed = diagnostics.Single(d => d.Code == DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(renamed.Message).Contains("it is emitted as 'equalsByValue$'");
        await Assert.That(files["f7.ts"]).Contains("equalsByValue(other: any): boolean {");

        var (_, hijacks) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record F1(double V) { public bool Equals(F1 o, double tol) => System.Math.Abs(o.V - V) <= tol; public bool EqualsByValue(F1 o) => true; }
            """
        );

        await Assert
            .That(hijacks.Single().Code)
            .IsEqualTo(DiagnosticCodes.OverloadFastPathHijacked);
    }

    /// <summary>
    /// An override of a joined base's overload is anchored on the base's group,
    /// so a shadowed root reports <c>MS0063</c> once, at the base, and nothing at
    /// the override (contracts/value-equality-members.md § 3).
    /// </summary>
    [Test]
    public async Task OverrideOfAShadowedRoot_ReportsOnlyAtTheRoot()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IK { }

            [Transpile]
            public record R0(int A) : IK { public virtual bool Equals(IK o) => false; }

            [Transpile]
            public record R1(int A, int B) : R0(A) { public override bool Equals(IK o) => true; }
            """
        );

        var warning = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch);

        await Assert.That(warning.Message).Contains("of 'R0'");

        await Assert
            .That(diagnostics.Any(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsFalse();
    }

    /// <summary>The Dart target supplies no synthesized member names (DECIDED-35): nothing joins.</summary>
    [Test]
    public async Task Dart_JoinsNothing()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public record RTol(double V) { public bool Equals(RTol other, double tol) => System.Math.Abs(other.V - V) <= tol; }

            [Transpile]
            public struct E4 : System.IEquatable<int>, System.IEquatable<string> { public int X; bool System.IEquatable<int>.Equals(int o) => o == X; bool System.IEquatable<string>.Equals(string? o) => o == "s"; public override int GetHashCode() => X; }
            """
        );

        await Assert.That(files.Count).IsGreaterThan(0);
        await Assert.That(string.Concat(files.Values)).DoesNotContain("ByValue");

        // The value-equality warnings are TypeScript's: Dart names no synthesized member.

        await Assert
            .That(diagnostics.Where(d => d.Message.Contains("is not emitted: its name")))
            .IsEmpty();
    }

    /// <summary>
    /// The value-equality shape is read from the symbols (FR-045): which members
    /// C# synthesizes and which declarations replace them.
    /// </summary>
    [Test]
    public async Task ValueEqualityShape_IsReadFromTheSymbols()
    {
        const string source = """
            using Metano.Annotations;

            public record Plain(int A);
            public record OwnEq(int A) { public virtual bool Equals(OwnEq? o) => o is not null; }
            public struct NoIface { public int X; public bool Equals(NoIface o) => true; }
            public struct WithIface : System.IEquatable<WithIface> { public int X; public bool Equals(WithIface o) => true; public override int GetHashCode() => 1; }
            public class Klass { }
            [PlainObject] public record Dto(int A);
            """;

        await Assert
            .That(ShapeOf(source, "Plain"))
            .IsEqualTo(new IrValueEqualityShape(IrValueEqualityKind.Record));

        await Assert
            .That(ShapeOf(source, "OwnEq"))
            .IsEqualTo(new IrValueEqualityShape(IrValueEqualityKind.Record, ReplacesEquals: true));

        await Assert
            .That(ShapeOf(source, "NoIface"))
            .IsEqualTo(new IrValueEqualityShape(IrValueEqualityKind.Struct));

        await Assert
            .That(ShapeOf(source, "WithIface"))
            .IsEqualTo(new IrValueEqualityShape(IrValueEqualityKind.Struct, true, true));

        await Assert.That(ShapeOf(source, "Klass")).IsEqualTo(IrValueEqualityShape.None);
        await Assert.That(ShapeOf(source, "Dto")).IsEqualTo(IrValueEqualityShape.None);
    }

    private static IrValueEqualityShape ShapeOf(string source, string typeName)
    {
        var compilation = IrTestHelper.Compile(source);
        var type = compilation.GetTypeByMetadataName(typeName)!;

        return IrClassExtractor
            .Extract(type, new IrExtractionContext(Compilation: compilation))
            .Semantics.ValueEquality;
    }

    /// <summary>
    /// Only the <c>Equals</c> name is freed for a struct's explicit
    /// <c>IEquatable&lt;S&gt;.Equals</c>, and only for a target that names the
    /// synthesized member: an explicit <c>IFormattable.ToString</c> stays dropped
    /// behind <c>System.ValueType</c>'s member, and an explicit
    /// <c>GetHashCode</c> keeps its <c>MS0031</c> conflict.
    /// </summary>
    [Test]
    public async Task ExplicitValueTypeMembers_KeepTheirFate()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Pt : System.IFormattable { public int X; public Pt(int x) { X = x; } string System.IFormattable.ToString(string? f, System.IFormatProvider? p) => "P"; }
            """
        );

        await Assert.That(files["pt.ts"]).DoesNotContain("IFormatProvider");
        await Assert.That(files["pt.ts"]).DoesNotContain("toString(");

        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IHashy { int GetHashCode(); }

            [Transpile]
            public struct HExpl : IHashy { public int X; int IHashy.GetHashCode() => 42; }
            """
        );

        var conflict = diagnostics.Single(d => d.Severity == MetanoDiagnosticSeverity.Error);

        await Assert.That(conflict.Code).IsEqualTo(DiagnosticCodes.ExplicitImplementationConflict);
        await Assert.That(conflict.Message).Contains("'ValueType.GetHashCode' already uses");
    }

    /// <summary>
    /// The Dart target supplies no synthesized member names: the struct shapes —
    /// an explicit <c>IEquatable&lt;S&gt;.Equals</c>, an explicit
    /// <c>IFormattable.ToString</c>, an interface-implementing <c>Equals(S)</c>, an
    /// <c>override Equals(object)</c> — keep the output they had before the
    /// value-equality join (DECIDED-35).
    /// </summary>
    [Test]
    public async Task DartStructShapes_MatchTheGolden()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public interface IEq<T> { bool Equals(T o); }

            [Transpile]
            public struct SExpl : System.IEquatable<SExpl> { public int X; public SExpl(int x) { X = x; } bool System.IEquatable<SExpl>.Equals(SExpl o) => o.X == X; }

            [Transpile]
            public struct Pt : System.IFormattable { public int X; public Pt(int x) { X = x; } string System.IFormattable.ToString(string? f, System.IFormatProvider? p) => "P"; }

            [Transpile]
            public struct SE : IEq<SE> { public int X; public SE(int x) { X = x; } public bool Equals(SE o) => o.X == X; }

            [Transpile]
            public struct SObj { public int X; public SObj(int x) { X = x; } public override bool Equals(object? o) => o is SObj s && s.X == X; public override int GetHashCode() => X; }
            """
        );

        var output = string.Join(
            "\n// ----\n",
            files
                .OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => $"// {file.Key}\n{file.Value}")
        );

        await TranspileHelper.AssertGolden("dart-value-equality-structs.dart", output);
    }

    /// <summary>
    /// A consumer of a library record that joined calls its stable fast paths
    /// (FR-043, contracts/value-equality-members.md § 8): the implicit
    /// <c>Equals</c>, a folded <c>override GetHashCode()</c> and a <c>with</c>.
    /// </summary>
    [Test]
    public async Task CrossPackage_CallsTheStableFastPaths()
    {
        const string library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]
            namespace Lib;
            public record LTol(double V) { public bool Equals(LTol o, double tol) => System.Math.Abs(o.V - V) <= tol; public LTol With(double v) => new LTol(v); }
            public record LHash(int V) { public override int GetHashCode() => V; public int HashCode(int seed) => V + seed; }
            """;

        const string consumer = """
            [assembly: TranspileAssembly]
            namespace App;
            public static class Use
            {
                public static bool Same(Lib.LTol a, Lib.LTol b) => a.Equals(b);
                public static Lib.LTol Copy(Lib.LTol a) => a with { V = 2 };
                public static int Hash(Lib.LHash a) => a.GetHashCode();
            }
            """;

        var files = TranspileHelper.TranspileWithLibraryImage(library, consumer);
        var use = files["app/use.ts"];

        await Assert.That(use).Contains("a.equalsByValue(b)");
        await Assert.That(use).Contains("a.withByValue({ v: 2 })");
        await Assert.That(use).Contains("a.hashCodeByValue()");
    }

    /// <summary>
    /// A group takes one synthesized member under the spelling that matched it:
    /// <c>[Name("equals")] With(int)</c> joins <c>Equals</c> and not the copy. Two
    /// methods both spelled <c>equals</c> by <c>[Name]</c> report <c>MS0001</c>.
    /// </summary>
    [Test]
    public async Task JoinedGroup_KeepsTheSpellingThatMatched()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record W2(int A) { [Name("equals")] public W2 With(int a) => new W2(a); }
            """
        );

        var w2 = files["w2.ts"];

        await Assert.That(w2.Split("equalsByValue(other: any)").Length).IsEqualTo(2);
        await Assert.That(w2).DoesNotContain("withByValue");

        await Assert
            .That(diagnostics.Any(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsFalse();

        var (_, twice) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record N9(int V) { [Name("equals")] public bool SameA(N9 o, int t) => true; [Name("equals")] public bool SameB(N9 o, string t) => true; }
            """
        );

        var error = twice.Single(d => d.Severity == MetanoDiagnosticSeverity.Error);

        await Assert.That(error.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(error.Message).Contains("'N9.SameB' is emitted as 'equals'");
    }

    /// <summary>
    /// A struct's explicit <c>System.IEquatable&lt;T&gt;.Equals(T)</c> that
    /// <c>ValueType.Equals</c> holds the name of is kept for a target naming the
    /// synthesized <c>Equals</c>: when <c>T</c> is another type it joins as an
    /// ordinary checked overload (FR-045).
    /// </summary>
    [Test]
    public async Task ExplicitEquatableOfAnotherType_JoinsAsACheckedOverload()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct SOtherX : System.IEquatable<int> { public int X; public SOtherX(int x) { X = x; } bool System.IEquatable<int>.Equals(int o) => o == X; public override int GetHashCode() => X; }

            [Transpile]
            public struct SP { public int X; }

            [Transpile]
            public struct SOS : System.IEquatable<SP> { public int X; bool System.IEquatable<SP>.Equals(SP o) => o.X == X; }

            [Transpile]
            public record struct RSX(int V, int N) : System.IEquatable<int> { bool System.IEquatable<int>.Equals(int o) => o == V; }
            """
        );

        await Assert.That(files["s-other-x.ts"]).Contains("equalsO(o: number): boolean {");

        await Assert
            .That(files["s-other-x.ts"])
            .Contains("if (args.length === 1 && isInt32(args[0])) {");

        await Assert.That(files["sos.ts"]).Contains("equalsO(o: SP): boolean {");
        await Assert.That(files["rsx.ts"]).Contains("equalsO(o: number): boolean {");
    }

    /// <summary>
    /// A struct's explicit <c>IEquatable&lt;S&gt;.Equals</c> beside a public
    /// <c>Equals</c> overload with other parameters joins that overload's group,
    /// and the synthesized member routes to it.
    /// </summary>
    [Test]
    public async Task ExplicitEquatableBesideAnOverload_IsRouted()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct SD : System.IEquatable<SD> { public int X; public int Noise; public bool Equals(SD o, int k) => k > 0; bool System.IEquatable<SD>.Equals(SD o) => o.Noise == Noise; public override int GetHashCode() => 1; }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(files["sd.ts"]).Contains("equalsO(o: SD): boolean {");

        await Assert
            .That(files["sd.ts"])
            .Contains("return other instanceof SD ? this.equalsO(other) : false;");
    }

    /// <summary>
    /// Only <c>System.IEquatable&lt;S&gt;</c> or a transpiled interface's
    /// <c>Equals(S)</c> replaces a struct's equality (FR-045): an <c>[Ignore]</c>d or
    /// <c>[External]</c> interface's does not, so the overload is shadowed. A
    /// transpiled interface's replaces it with an <c>MS0063</c> warning, since
    /// sets, dictionaries and record fields reach it where .NET compares fields.
    /// </summary>
    [Test]
    public async Task StructEqualsReplacement_FollowsTheInterface()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Ignore]
            public interface IEqI<T> { bool Equals(T o); }

            [Metano.Annotations.TypeScript.External]
            public interface IEqE<T> { bool Equals(T o); }

            [Transpile]
            public interface IEqT<T> { bool Equals(T o); }

            [Transpile]
            public struct SIgn : IEqI<SIgn> { public int X; public bool Equals(SIgn o) => o.X == X; }

            [Transpile]
            public struct SExt : IEqE<SExt> { public int X; public bool Equals(SExt o) => o.X == X; }

            [Transpile]
            public struct STr : IEqT<STr> { public int X; public bool Equals(STr o) => o.X == X; }
            """
        );

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(warnings.Count(m => m.Contains("cannot be told apart"))).IsEqualTo(2);

        await Assert
            .That(warnings.Single(m => m.Contains("replaces the value equality")))
            .Contains("'STr.Equals");
    }

    /// <summary>
    /// <see cref="IrTypeRefEquivalence"/> compares type arguments, tuple elements
    /// and array elements structurally, which record equality does not.
    /// </summary>
    [Test]
    public async Task TypeRefEquivalence_IsStructural()
    {
        IrTypeRef Generic() =>
            new IrNamedTypeRef("Box", TypeArguments: [new IrPrimitiveTypeRef(IrPrimitive.Int32)]);

        IrTypeRef Array() => new IrArrayTypeRef(Generic());

        await Assert.That(Generic().Equals(Generic())).IsFalse();
        await Assert.That(IrTypeRefEquivalence.AreEquivalent(Generic(), Generic())).IsTrue();
        await Assert.That(IrTypeRefEquivalence.AreEquivalent(Array(), Array())).IsTrue();

        await Assert
            .That(IrTypeRefEquivalence.AreEquivalent(Generic(), new IrNamedTypeRef("Box")))
            .IsFalse();
    }

    /// <summary>
    /// A kept explicit <c>IEquatable&lt;S&gt;.Equals</c> beside a method whose
    /// <c>[Name]</c> also spells <c>equals</c> would keep a plain <c>equals</c> beside
    /// the joined dispatcher, whose synthesized member routes back to it: it
    /// reports <c>MS0001</c> instead of emitting a recursive pair (FR-039).
    /// </summary>
    [Test]
    public async Task ExplicitEquatableBesideANamedEquals_ReportsAnError()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct G3 : System.IEquatable<G3> { public int X; bool System.IEquatable<G3>.Equals(G3 o) => o.X == X; [Name("equals")] public bool Same(string s) => s == "g" + X; public override int GetHashCode() => X; }

            [Transpile]
            public struct E12 : System.IEquatable<E12> { public int X; bool System.IEquatable<E12>.Equals(E12 o) => o.X == X; [Name("equals")] public bool Same(E12 o, int s) => System.Math.Abs(o.X - X) <= s; public override int GetHashCode() => X; }
            """
        );

        var errors = diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error).ToList();

        await Assert.That(errors.Count).IsEqualTo(2);
        await Assert.That(errors.All(e => e.Code == DiagnosticCodes.UnsupportedFeature)).IsTrue();
        await Assert.That(errors.All(e => e.Message.Contains("already declare"))).IsTrue();
    }

    /// <summary>
    /// Two explicit <c>IEquatable&lt;T&gt;.Equals</c> of one struct claim one name and
    /// are both dropped: each reports an <c>MS0001</c> warning instead of silently.
    /// </summary>
    [Test]
    public async Task TwoExplicitEquatables_WarnWhenDropped()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct E4 : System.IEquatable<int>, System.IEquatable<string> { public int X; bool System.IEquatable<int>.Equals(int o) => o == X; bool System.IEquatable<string>.Equals(string? o) => o == "s"; public override int GetHashCode() => X; }
            """
        );

        var warnings = diagnostics
            .Where(d =>
                d.Severity == MetanoDiagnosticSeverity.Warning
                && d.Code == DiagnosticCodes.UnsupportedFeature
            )
            .ToList();

        await Assert.That(warnings.Count).IsEqualTo(2);

        await Assert
            .That(
                warnings.All(w =>
                    w.Message.Contains("held by the synthesized 'Equals(object)' of 'E4'")
                )
            )
            .IsTrue();
    }

    /// <summary>
    /// Function parameters, callable signatures and tuple elements are compared
    /// structurally too.
    /// </summary>
    [Test]
    public async Task TypeRefEquivalence_CoversNestedLists()
    {
        IrTypeRef Function() =>
            new IrFunctionTypeRef(
                [
                    new IrParameter(
                        "x",
                        new IrTupleTypeRef([new IrPrimitiveTypeRef(IrPrimitive.Int32)])
                    ),
                ],
                new IrPrimitiveTypeRef(IrPrimitive.Boolean)
            );

        await Assert.That(IrTypeRefEquivalence.AreEquivalent(Function(), Function())).IsTrue();

        await Assert
            .That(
                IrTypeRefEquivalence.AreEquivalent(
                    new IrCallableTypeRef([(IrFunctionTypeRef)Function()]),
                    new IrCallableTypeRef([(IrFunctionTypeRef)Function()])
                )
            )
            .IsTrue();

        await Assert
            .That(
                IrTypeRefEquivalence.AreEquivalent(
                    Function(),
                    new IrFunctionTypeRef([], new IrPrimitiveTypeRef(IrPrimitive.Boolean))
                )
            )
            .IsFalse();
    }

    /// <summary>
    /// A typed replacing <c>Equals</c> renamed by its own <c>[Name]</c> beside a
    /// method whose <c>[Name]</c> spells <c>equals</c> sits outside the joined group,
    /// so the synthesized member could not reach it: <c>MS0001</c>, never a
    /// dispatcher that loops (FR-039).
    /// </summary>
    [Test]
    public async Task RenamedReplacingEqualsBesideANamedEquals_ReportsAnError()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct C6 : System.IEquatable<C6> { public int X; [Name("equalsValue")] public bool Equals(C6 o) => o.X == X; public override int GetHashCode() => X; [Name("equals")] public bool Same(string s) => s == "c" + X; }

            [Transpile]
            public record C7(int X) { [Name("sameX")] public virtual bool Equals(C7? o) => o is not null && o.X == X; public override int GetHashCode() => X; [Name("equals")] public bool Same(string s) => s == "c" + X; }
            """
        );

        var errors = diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error).ToList();

        await Assert.That(errors.Count).IsEqualTo(2);

        await Assert
            .That(errors.All(e => e.Message.Contains("replaces the synthesized equality")))
            .IsTrue();
    }

    /// <summary>
    /// The spelled-alike error covers every replacing declaration spelled
    /// <c>equals</c> beside a <c>[Name("equals")]</c> method: a struct's implicit
    /// <c>IEquatable&lt;S&gt;.Equals(S)</c> and a record's own <c>Equals(R)</c>.
    /// </summary>
    [Test]
    public async Task ReplacingEqualsBesideANamedEquals_ReportsSpelledAlike()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct C2 : System.IEquatable<C2> { public int X; public bool Equals(C2 o) => o.X == X; public override int GetHashCode() => X; [Name("equals")] public bool Same(string s) => s == "c" + X; }

            [Transpile]
            public record C3(int X) { public virtual bool Equals(C3? o) => o is not null && o.X == X; public override int GetHashCode() => X; [Name("equals")] public bool Same(string s) => s == "c" + X; }
            """
        );

        var errors = diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error).ToList();

        await Assert.That(errors.Count).IsEqualTo(2);
        await Assert.That(errors.All(e => e.Message.Contains("already declare"))).IsTrue();
    }

    /// <summary>
    /// The dropped-IEquatable warning covers record structs, and names a user
    /// method with the implementation's parameters when that method holds the name.
    /// </summary>
    [Test]
    public async Task DroppedEquatableWarning_CoversRecordStructsAndUserClaimants()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record struct W1(int V) : System.IEquatable<int>, System.IEquatable<string> { bool System.IEquatable<int>.Equals(int o) => o == V; bool System.IEquatable<string>.Equals(string? o) => o == "s"; }

            [Transpile]
            public struct W2 : System.IEquatable<W2> { public int X; public int Noise; public bool Equals(W2 o) => o.X == X; bool System.IEquatable<W2>.Equals(W2 o) => o.Noise == Noise; public override int GetHashCode() => 1; }
            """
        );

        var warnings = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Severity == MetanoDiagnosticSeverity.Warning
            )
            .Select(d => d.Message)
            .ToList();

        await Assert.That(warnings.Count(m => m.Contains("of 'W1'"))).IsEqualTo(2);

        await Assert
            .That(warnings.Single(m => m.StartsWith("'W2.")).Contains("held by 'W2.Equals("))
            .IsTrue();
    }

    /// <summary>Two unknown type refs are conservatively not equivalent.</summary>
    [Test]
    public async Task TypeRefEquivalence_TreatsUnknownTypesAsDifferent()
    {
        await Assert
            .That(
                IrTypeRefEquivalence.AreEquivalent(new IrUnknownTypeRef(), new IrUnknownTypeRef())
            )
            .IsFalse();

        await Assert
            .That(
                IrTypeRefEquivalence.AreEquivalent(
                    new IrGroupingTypeRef(
                        new IrTypeParameterRef("K"),
                        new IrPrimitiveTypeRef(IrPrimitive.String)
                    ),
                    new IrGroupingTypeRef(
                        new IrTypeParameterRef("K"),
                        new IrPrimitiveTypeRef(IrPrimitive.String)
                    )
                )
            )
            .IsTrue();
    }
}
