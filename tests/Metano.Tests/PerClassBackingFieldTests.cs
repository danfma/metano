using System.Text.RegularExpressions;

namespace Metano.Tests;

/// <summary>
/// Overridable auto-properties (#341, #363): a C# auto-property is an accessor plus a
/// private backing field per declaring class, so the TypeScript output keeps one private
/// field per class (<c>p$Shape_hash</c>) behind a getter (and setter), never one shared
/// own field that would hide a derived accessor or an unreachable <c>super.x</c>.
/// </summary>
public class PerClassBackingFieldTests
{
    private static readonly Regex BackingField = new(
        @"(?<name>\w+)\$(?<owner>\w+?)_(?<hash>[0-9a-z]{6})\b"
    );

    [Test]
    public async Task ComputedGetterOverride_OfVirtualAutoProperty_StaysAGetterOverTheBaseAccessor()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class VBase
            {
                public virtual int P { get; } = 1;

                public int Describe() => P;
            }

            [Transpile]
            public class VDer : VBase
            {
                public override int P => 2;
            }
            """
        );

        var baseOutput = result["app/v-base.ts"];
        var field = FieldOf(baseOutput, "p", "VBase");

        await Assert.That(baseOutput).Contains($"  private readonly {field}: number = 1;");
        await Assert.That(baseOutput).Contains("  get p(): number {");
        await Assert.That(baseOutput).Contains($"    return this.{field};");
        await Assert.That(baseOutput).DoesNotContain("  readonly p: number");
        await Assert.That(result["app/v-der.ts"]).Contains("  get p(): number {\n    return 2;");
        await Assert.That(result["app/v-der.ts"]).DoesNotMatch(BackingField);
    }

    [Test]
    public async Task OverriddenDelegateAutoProperty_BaseCallReadsTheBaseAccessor()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class DBase
            {
                public virtual Func<int> Get { get; } = () => 1;
            }

            [Transpile]
            public class DDer : DBase
            {
                public override Func<int> Get { get; } = () => 2;

                public int Both() => base.Get() + Get();
            }
            """
        );

        var baseField = FieldOf(result["app/d-base.ts"], "get", "DBase");
        var derivedField = FieldOf(result["app/d-der.ts"], "get", "DDer");
        var derived = result["app/d-der.ts"];

        await Assert
            .That(result["app/d-base.ts"])
            .Contains($"  private readonly {baseField}: () => number = () => 1;");
        await Assert
            .That(derived)
            .Contains($"  private readonly {derivedField}: () => number = () => 2;");
        await Assert.That(derived).Contains($"    return this.{derivedField};");
        await Assert.That(derived).Contains("    return super.get() + this.get();");
        await Assert.That(baseField).IsNotEqualTo(derivedField);
    }

    [Test]
    public async Task BaseRead_AndWrite_OfOverriddenProperty_GoThroughSuperAccessors()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class RBase
            {
                public virtual int P { get; set; } = 10;
            }

            [Transpile]
            public class RDer : RBase
            {
                public override int P
                {
                    get => base.P + 1;
                    set => base.P = value;
                }
            }
            """
        );

        var baseOutput = result["app/r-base.ts"];
        var field = FieldOf(baseOutput, "p", "RBase");

        await Assert.That(baseOutput).Contains($"  private {field}: number = 10;");
        await Assert
            .That(baseOutput)
            .Contains($"  set p(value: number) {{\n    this.{field} = value;\n  }}");
        await Assert.That(result["app/r-der.ts"]).Contains("    return super.p + 1;");
        await Assert.That(result["app/r-der.ts"]).Contains("    super.p = value;");
    }

    [Test]
    public async Task InitProperty_GetsASetter_SoWithAndObjectInitializersAssignIt()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public record IBase
            {
                public virtual int Q { get; init; } = 5;
            }

            [Transpile]
            public record IDer : IBase
            {
                public override int Q { get; init; } = 7;

                public int Sum() => Q + base.Q;
            }
            """
        );

        var baseField = FieldOf(result["app/i-base.ts"], "q", "IBase");
        var derivedField = FieldOf(result["app/i-der.ts"], "q", "IDer");
        var derived = result["app/i-der.ts"];

        await Assert.That(derived).Contains($"  private {derivedField}: number = 7;");
        await Assert
            .That(derived)
            .Contains($"  set q(value: number) {{\n    this.{derivedField} = value;\n  }}");
        await Assert.That(derived).Contains("    return this.q + super.q;");
        await Assert.That(derived).Contains("return copyRecordWith<IDer>(this, overrides);");
        await Assert.That(result["app/i-base.ts"]).Contains($"  private {baseField}: number = 5;");
        // equals / hashCode keep reading through the public accessor.
        await Assert.That(derived).Contains("this.q === other.q");
        await Assert.That(derived).Contains("hc.add(this.q);");
    }

    [Test]
    public async Task VirtualProperty_NoOneOverrides_StillOwnsItsBackingField()
    {
        // The decision is local to the property, so a package that never sees an override
        // (a library) agrees with the consumer that adds one.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Lonely
            {
                public virtual int Z { get; } = 9;
            }
            """
        );

        var field = FieldOf(result["app/lonely.ts"], "z", "Lonely");

        await Assert
            .That(result["app/lonely.ts"])
            .Contains($"  private readonly {field}: number = 9;");
        await Assert.That(result["app/lonely.ts"]).Contains("  get z(): number {");
    }

    [Test]
    public async Task NonVirtualProperty_KeepsItsPlainField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Plain
            {
                public int Z { get; } = 9;
            }
            """
        );

        await Assert.That(result["app/plain.ts"]).Contains("  readonly z: number = 9;");
        await Assert.That(result["app/plain.ts"]).DoesNotMatch(BackingField);
        await Assert.That(result["app/plain.ts"]).DoesNotContain("toJSON");
    }

    [Test]
    public async Task SealedOverride_FollowsTheBaseItOverrides()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class SBase
            {
                public virtual int S { get; } = 1;
            }

            [Transpile]
            public class SMid : SBase
            {
                public sealed override int S { get; } = 2;
            }
            """
        );

        var field = FieldOf(result["app/s-mid.ts"], "s", "SMid");

        await Assert
            .That(result["app/s-mid.ts"])
            .Contains($"  private readonly {field}: number = 2;");
        await Assert.That(result["app/s-mid.ts"]).Contains("  get s(): number {");
    }

    [Test]
    public async Task GetOnlyProperty_AssignedInConstructors_WritesTheBackingField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class CBase
            {
                public virtual string Name { get; }

                public CBase(string name) { Name = name; }
            }

            [Transpile]
            public class CDer : CBase
            {
                public override string Name { get; }

                public CDer(string n) : base(n + "!") { Name = n; }

                public string Both() => base.Name + "/" + Name;
            }
            """
        );

        var baseField = FieldOf(result["app/c-base.ts"], "name", "CBase");
        var derivedField = FieldOf(result["app/c-der.ts"], "name", "CDer");

        await Assert.That(result["app/c-base.ts"]).Contains($"    this.{baseField} = name;");
        await Assert.That(result["app/c-der.ts"]).Contains($"    this.{derivedField} = n;");
        await Assert
            .That(result["app/c-der.ts"])
            .Contains("return super.name + \"/\" + this.name;");
    }

    [Test]
    public async Task PrimaryConstructor_AliasProperty_IsNotPromotedToASharedField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class PBase(int x)
            {
                public virtual int X { get; } = x;
            }

            [Transpile]
            public class PDer(int x) : PBase(x)
            {
                public override int X { get; } = x * 2;

                public int All() => base.X + X;
            }
            """
        );

        var baseField = FieldOf(result["app/p-base.ts"], "x", "PBase");
        var derivedField = FieldOf(result["app/p-der.ts"], "x", "PDer");

        await Assert.That(result["app/p-base.ts"]).Contains("  constructor(x: number) {");
        await Assert.That(result["app/p-base.ts"]).DoesNotContain("readonly x: number)");
        await Assert.That(result["app/p-base.ts"]).Contains($"    this.{baseField} = x;");
        await Assert.That(result["app/p-der.ts"]).Contains($"    this.{derivedField} = x * 2;");
        await Assert.That(result["app/p-der.ts"]).Contains("return super.x + this.x;");
    }

    [Test]
    public async Task PositionalRecord_KeepsItsParameterProperty_AndBacksItsOverridableMember()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public record Pos(int Id)
            {
                public virtual int V { get; init; } = 3;
            }

            [Transpile]
            public record PosDer(int Id) : Pos(Id)
            {
                public override int V { get; init; } = 4;
            }
            """
        );

        var field = FieldOf(result["app/pos.ts"], "v", "Pos");
        var output = result["app/pos.ts"];

        await Assert.That(output).Contains("  constructor(readonly id: number) { }");
        await Assert.That(output).Contains($"  private {field}: number = 3;");
        await Assert.That(output).Contains("this.id === other.id && this.v === other.v");
        await Assert.That(output).Contains("hc.add(this.v);");
    }

    [Test]
    public async Task SameClassNameInDifferentNamespaces_GetDistinctBackingFields()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.One
            {
                [Transpile]
                public class Shape
                {
                    public virtual int K { get; } = 1;
                }
            }

            namespace App.Two
            {
                [Transpile]
                public class Shape : App.One.Shape
                {
                    public override int K { get; } = 2;
                }
            }
            """
        );

        var first = FieldOf(result["app/one/shape.ts"], "k", "Shape");
        var second = FieldOf(result["app/two/shape.ts"], "k", "Shape");

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(first.Split('_')[0]).IsEqualTo(second.Split('_')[0]);
    }

    [Test]
    public async Task BackingFieldName_IsStableAcrossRuns()
    {
        const string source = """
            namespace App;

            [Transpile]
            public class Shape
            {
                public virtual int K { get; } = 1;
            }

            [Transpile]
            public class Child : Shape
            {
                public override int K { get; } = 2;
            }
            """;

        var first = FieldOf(TranspileHelper.Transpile(source)["app/shape.ts"], "k", "Shape");
        var second = FieldOf(TranspileHelper.Transpile(source)["app/shape.ts"], "k", "Shape");

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first).IsEqualTo("k$Shape_ygoxq3");
    }

    [Test]
    public async Task BackingFieldName_MatchesTheRuntimePublicJsonShapePattern()
    {
        // The runtime `publicJsonShape` (targets/js/metano-runtime/src/system/
        // json/public-json-shape.ts) recognizes an emitted backing field by
        // /^([^$]+)\$[^$]+_[0-9a-z]{6}$/ — keep this test and that pattern in
        // sync: the group must be the public camelCase member name.
        const string source = """
            namespace App;

            [Transpile]
            public class Shape
            {
                public virtual int K { get; } = 1;
            }
            """;

        var field = FieldOf(TranspileHelper.Transpile(source)["app/shape.ts"], "k", "Shape");
        var match = Regex.Match(field, "^([^$]+)\\$[^$]+_[0-9a-z]{6}$");

        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Groups[1].Value).IsEqualTo("k");
    }

    [Test]
    public async Task PrivateProtectedAndIgnoredVirtualProperties_GetNoBackingField()
    {
        // The core predicate (OverrideChains.EmitsPerClassStorage) drops
        // `private protected` and `[Ignore]`d overridable auto-properties;
        // only the emitted members declare a backing field.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class FBase
            {
                public virtual int P { get; set; } = 1;

                private protected virtual int Q { get; set; } = 2;

                [Ignore]
                public virtual int R { get; set; } = 3;
            }
            """
        );

        var field = FieldOf(result["app/f-base.ts"], "p", "FBase");

        await Assert.That(result["app/f-base.ts"]).Contains($"private {field}: number = 1;");
        await Assert.That(result["app/f-base.ts"]).DoesNotContain("q$");
        await Assert.That(result["app/f-base.ts"]).DoesNotContain("r$");
    }

    [Test]
    public async Task LambdaWrite_InConstructor_ToWritableProperty_StaysOnTheVirtualSetter()
    {
        // C# rejects assigning a get-only auto-property inside a lambda
        // (CS0200), so the only write a lambda in a constructor can make to a
        // per-class-backed property goes through its setter — the virtual
        // call C# itself would make. Pin that the lowering keeps such a write
        // off the backing field.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class LBase
            {
                public virtual int P { get; set; } = 1;

                public LBase()
                {
                    System.Action assign = () => P = 5;
                    assign();
                }
            }
            """
        );

        var field = FieldOf(result["app/l-base.ts"], "p", "LBase");

        await Assert.That(result["app/l-base.ts"]).Contains("this.p = 5;");
        await Assert.That(result["app/l-base.ts"]).DoesNotContain($"this.{field} = 5");
    }

    [Test]
    public async Task ClassWithBackingFields_EmitsToJsonOverThePublicShape()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class JBase
            {
                public virtual int A { get; set; } = 1;
            }

            [Transpile]
            public class JDer : JBase
            {
                public override int A { get; set; } = 2;
            }
            """
        );

        await Assert
            .That(result["app/j-base.ts"])
            .Contains("  toJSON(): unknown {\n    return publicJsonShape(this);\n  }");
        await Assert
            .That(result["app/j-base.ts"])
            .Contains("import { publicJsonShape } from \"metano-runtime\";");
        await Assert
            .That(result["app/j-der.ts"])
            .Contains("  override toJSON(): unknown {\n    return publicJsonShape(this);\n  }");
    }

    [Test]
    public async Task ProtectedProperty_KeepsItsAccessibilityOnTheAccessors()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class PrBase
            {
                protected virtual int Pr { get; } = 1;

                public int ReadPr() => Pr;
            }

            [Transpile]
            public class PrDer : PrBase
            {
                protected override int Pr { get; } = 2;
            }
            """
        );

        await Assert.That(result["app/pr-base.ts"]).Contains("  protected get pr(): number {");
        await Assert.That(result["app/pr-der.ts"]).Contains("  protected get pr(): number {");
    }

    [Test]
    public async Task PrimaryConstructorInitializer_OnWritableProperty_StoresToTheDeclaringClassField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class WBase(int x)
            {
                public virtual int P { get; set; } = x;
            }

            [Transpile]
            public class WDer(int y) : WBase(y)
            {
                public override int P { get; set; } = 9;

                public int FromBase() => base.P;
            }
            """
        );

        var field = FieldOf(result["app/w-base.ts"], "p", "WBase");

        await Assert.That(result["app/w-base.ts"]).Contains($"    this.{field} = x;");
        await Assert.That(result["app/w-base.ts"]).DoesNotContain("this.p = x");
    }

    [Test]
    public async Task GetterOnlyOverride_OfWritableProperty_ForwardsTheInheritedSetter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class FBase
            {
                public virtual int P { get; set; } = 1;
            }

            [Transpile]
            public class FDer : FBase
            {
                public override int P => 5;
            }
            """
        );

        await Assert.That(result["app/f-der.ts"]).Contains("  get p(): number {\n    return 5;");
        await Assert
            .That(result["app/f-der.ts"])
            .Contains("  set p(value: number) {\n    super.p = value;\n  }");
    }

    [Test]
    public async Task SetterOnlyOverride_ForwardsTheInheritedGetter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class GBase
            {
                public virtual int P { get; set; } = 1;
            }

            [Transpile]
            public class GDer : GBase
            {
                public override int P
                {
                    set { }
                }
            }
            """
        );

        await Assert
            .That(result["app/g-der.ts"])
            .Contains("  get p(): number {\n    return super.p;\n  }");
    }

    [Test]
    public async Task ToJson_OverridesOnlyAnAncestorThatEmitsOne()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class HBase
            {
                private protected virtual int Hidden { get; } = 1;

                [Ignore]
                public virtual int Skipped { get; } = 1;
            }

            [Transpile]
            public class HDer : HBase
            {
                private protected override int Hidden { get; } = 2;

                [Ignore]
                public override int Skipped { get; } = 2;

                public virtual int Own { get; } = 1;
            }

            [Transpile]
            public class HDerDer : HDer
            {
                public override int Own { get; } = 2;
            }
            """
        );

        await Assert.That(result["app/h-der.ts"]).Contains("  toJSON(): unknown {");
        await Assert.That(result["app/h-der.ts"]).DoesNotContain("override toJSON");
        await Assert.That(result["app/h-der-der.ts"]).Contains("  override toJSON(): unknown {");
    }

    [Test]
    public async Task UserToJson_IsNotDuplicated()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class UBase
            {
                public virtual int P { get; } = 1;

                public string ToJSON() => "x";
            }

            [Transpile]
            public class UDer : UBase
            {
                public override int P { get; } = 2;
            }
            """
        );

        await Assert.That(Regex.Matches(result["app/u-base.ts"], "toJSON\\(").Count).IsEqualTo(1);
        await Assert.That(result["app/u-base.ts"]).DoesNotContain("publicJsonShape");
    }

    [Test]
    public async Task InheritedSetter_IsForwardedThroughSeveralOverrides()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class LBase
            {
                public virtual int Z { get; set; } = 1;
            }

            [Transpile]
            public class LMid : LBase
            {
                public override int Z => 3;
            }

            [Transpile]
            public class LLeaf : LMid
            {
                public override int Z => 7;
            }
            """
        );

        await Assert
            .That(result["app/l-leaf.ts"])
            .Contains("  set z(value: number) {\n    super.z = value;\n  }");
    }

    [Test]
    public async Task AncestorToJson_IsNotReplacedBySynthesizedOne()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class KRoot
            {
                public object ToJSON() => "root";
            }

            [Transpile]
            public class KMid : KRoot
            {
                public virtual int M { get; set; } = 1;
            }

            [Transpile]
            public class KLeaf : KMid
            {
                public override int M { get; set; } = 2;
            }
            """
        );

        await Assert.That(result["app/k-root.ts"]).Contains("toJSON(");
        await Assert.That(FieldOf(result["app/k-mid.ts"], "m", "KMid")).IsNotEmpty();
        await Assert.That(result["app/k-mid.ts"]).DoesNotContain("publicJsonShape");
        await Assert.That(result["app/k-leaf.ts"]).DoesNotContain("publicJsonShape");
    }

    [Test]
    public async Task RenamedToJson_CountsAsTheUsersToJson()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class NBase
            {
                public virtual int P { get; } = 1;

                [Name("toJSON")]
                public object Serialize() => "x";
            }

            [Transpile]
            public class NDer : NBase
            {
                public override int P { get; } = 2;
            }
            """
        );

        await Assert.That(FieldOf(result["app/n-base.ts"], "p", "NBase")).IsNotEmpty();
        await Assert.That(result["app/n-base.ts"]).Contains("toJSON(");
        await Assert.That(result["app/n-base.ts"]).DoesNotContain("publicJsonShape");
    }

    [Test]
    public async Task IgnoredOrUnrelatedToJson_DoesNotSuppressTheSynthesizedOne()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class QBase
            {
                public virtual int P { get; } = 1;

                [Ignore]
                public object ToJSON() => "ignored";

                public static object ToJSON(int value) => value;

                public object ToJson() => "other casing";
            }

            [Transpile]
            public class QDer : QBase
            {
                public override int P { get; } = 2;
            }
            """
        );

        await Assert.That(result["app/q-base.ts"]).Contains("return publicJsonShape(this);");
    }

    [Test]
    public async Task ConsumerOverride_OfLibraryVirtualProperty_OwnsStorageAndOverridesToJson()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public class LibBase
            {
                public virtual int P { get; set; } = 1;
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Consumer : Lib.LibBase
            {
                public override int P { get; set; } = 2;
            }

            public class PlainConsumer : Lib.LibBase
            {
                public int Other { get; set; }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var field = FieldOf(result["app/consumer.ts"], "p", "Consumer");

        await Assert.That(result["app/consumer.ts"]).Contains($"  private {field}: number = 2;");
        await Assert.That(result["app/consumer.ts"]).Contains("  get p(): number {");
        await Assert.That(result["app/consumer.ts"]).Contains("  override toJSON(): unknown {");
        // No storage of its own: it inherits the library's toJSON.
        await Assert.That(result["app/plain-consumer.ts"]).DoesNotContain("toJSON");
    }

    [Test]
    public async Task CompoundAndIncrementWrites_ToGetOnlyProperty_StoreToTheBackingField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Counter
            {
                public virtual int N { get; }
                public virtual string? Label { get; }
                public virtual int M { get; set; }

                public Counter()
                {
                    N += 2;
                    N++;
                    --N;
                    Label ??= "none";
                    M += 1;
                }
            }
            """
        );

        var output = result["app/counter.ts"];
        var n = FieldOf(output, "n", "Counter");
        var label = FieldOf(output, "label", "Counter");

        await Assert.That(output).Contains($"    this.{n} += 2;");
        await Assert.That(output).Contains($"    this.{n}++;");
        await Assert.That(output).Contains($"    --this.{n};");
        await Assert.That(output).Contains($"    this.{label} ??= \"none\";");
        // A writable property keeps its (virtual) setter call.
        await Assert.That(output).Contains("    this.m += 1;");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConsumerOverride_OfLibraryImage_OwnsStorageAndOverridesToJson(
        bool referenceAssembly
    )
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public class LibBase
            {
                public virtual int P { get; set; } = 1;

                public virtual int Computed => 3;
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Consumer : Lib.LibBase
            {
                public override int P { get; set; } = 2;
            }

            public class ComputedConsumer : Lib.LibBase
            {
                public override int Computed => 4;
            }
            """;

        var result = TranspileHelper.TranspileWithLibraryImage(
            library,
            consumer,
            referenceAssembly
        );

        await Assert.That(result["app/consumer.ts"]).Contains("  override toJSON(): unknown {");
        await Assert.That(result["app/consumer.ts"]).Contains("  get p(): number {");
        // A computed virtual property is not auto, so no storage and no toJSON of its own.
        await Assert.That(result["app/computed-consumer.ts"]).DoesNotContain("toJSON");
    }

    [Test]
    public async Task HidingProperty_OfPerClassProperty_AlsoOwnsStorage()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class HidBase
            {
                public virtual int X { get; set; } = 1;
            }

            [Transpile]
            public class Hider : HidBase
            {
                public new int X { get; set; } = 2;
            }

            [Transpile]
            public class VirtualHider : HidBase
            {
                public new virtual int X { get; set; } = 3;
            }
            """
        );

        var hider = FieldOf(result["app/hider.ts"], "x", "Hider");
        var virtualHider = FieldOf(result["app/virtual-hider.ts"], "x", "VirtualHider");

        await Assert.That(result["app/hider.ts"]).Contains($"  private {hider}: number = 2;");
        await Assert.That(result["app/hider.ts"]).Contains("  get x(): number {");
        await Assert
            .That(result["app/virtual-hider.ts"])
            .Contains($"  private {virtualHider}: number = 3;");
    }

    private static string FieldOf(string output, string property, string owner)
    {
        var match = BackingField
            .Matches(output)
            .FirstOrDefault(m =>
                m.Groups["name"].Value == property && m.Groups["owner"].Value == owner
            );

        return match?.Value
            ?? throw new InvalidOperationException(
                $"No backing field '{property}${owner}_<hash>' in:\n{output}"
            );
    }
}
