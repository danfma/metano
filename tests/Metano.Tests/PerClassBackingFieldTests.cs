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
    public async Task VirtualProperty_NoOneOverrides_KeepsItsPlainField()
    {
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

        await Assert.That(result["app/lonely.ts"]).Contains("  readonly z: number = 9;");
        await Assert.That(result["app/lonely.ts"]).DoesNotMatch(BackingField);
        await Assert.That(result["app/lonely.ts"]).DoesNotContain("toJSON");
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
