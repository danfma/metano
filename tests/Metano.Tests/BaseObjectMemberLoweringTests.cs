namespace Metano.Tests;

/// <summary>
/// A <c>base.Equals(...)</c> / <c>base.GetHashCode()</c> / <c>base.ToString()</c>
/// whose member no emitted base class declares — bound to <c>object</c> itself,
/// or to a base the toolchain does not emit — lowers to <c>object</c>'s default
/// semantics instead of <c>super.<member></c>, which has no member to reach
/// (tsgo: TS2339 on the missing member; runtime: TypeError). An emitted base
/// declaring the member keeps <c>super.</c>, as do the record
/// synthesizers (#506).
/// </summary>
public class BaseObjectMemberLoweringTests
{
    [Test]
    public async Task ObjectBoundBaseMembers_LowerToObjectDefaults()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public int V;

                public override bool Equals(object? other) => base.Equals(other) && other is Box b && b.V == V;

                public override int GetHashCode() => base.GetHashCode() ^ V.GetHashCode();

                public override string ToString() => base.ToString() + "!";
            }
            """
        );

        await Assert
            .That(result["box.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("base-object-members.ts"));
    }

    [Test]
    public async Task BaseMembersOnANonTranspiledBase_LowerToObjectDefaults()
    {
        // The emitted class has no `extends` for a non-transpiled base, so
        // `super.<member>` has no member to reach; `object`'s defaults are the
        // only semantics every target can honor (the base's own override is
        // unreachable — a documented divergence from .NET).
        var result = TranspileHelper.Transpile(
            """
            public class Foreign
            {
                public override string ToString() => "foreign";
            }

            [Transpile]
            public class Kept : Foreign
            {
                public override string ToString() => base.ToString() + "!";
            }
            """
        );

        var kept = result["kept.ts"];

        await Assert.That(kept).DoesNotContain("extends");
        await Assert.That(kept).DoesNotContain("super.");
        await Assert.That(kept).Contains("return \"Kept\" + \"!\";");
    }

    [Test]
    public async Task EmittedBaseDeclaringTheMember_KeepsSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public int Cents;

                public virtual bool Equals(object? other) => other is Money m && m.Cents == Cents;

                public virtual int GetHashCode() => Cents;

                public virtual string ToString() => "money";
            }

            [Transpile]
            public class Coins : Money
            {
                public override bool Equals(object? other) => base.Equals(other) && other is Coins c && c.Cents == Cents;

                public override int GetHashCode() => base.GetHashCode() + 1;

                public override string ToString() => base.ToString() + "!";
            }
            """
        );

        var coins = result["coins.ts"];

        await Assert.That(coins).Contains("return super.equals(other) && other instanceof Coins");
        await Assert.That(coins).Contains("return super.hashCode() + 1;");
        await Assert.That(coins).Contains("return super.toString() + \"!\";");
    }

    [Test]
    public async Task BaseObjectMembers_InANamespacedClass_CarryTheQualifiedName()
    {
        // .NET's `base.ToString()` prints `Type.ToString()`: the
        // namespace-qualified metadata name, `+` for a nested type.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public class Tagged
                {
                    public override string ToString() => base.ToString();
                }

                [Transpile]
                public class Container
                {
                    [Transpile]
                    public class Nested
                    {
                        public override string ToString() => base.ToString();
                    }
                }
            }
            """
        );

        await Assert.That(result["app/tagged.ts"]).Contains("return \"App.Tagged\";");
        await Assert.That(result["app/container.ts"]).Contains("return \"App.Container+Nested\";");
    }

    [Test]
    public async Task DartTarget_BaseObjectMembers_LowerToObjectDefaults()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public int V;

                public override bool Equals(object? other) => base.Equals(other) && other is Box b && b.V == V;

                public override int GetHashCode() => base.GetHashCode() ^ V.GetHashCode();

                public override string ToString() => base.ToString() + "!";
            }
            """
        );

        var dart = files["box.dart"];

        await Assert.That(dart).DoesNotContain("super.equals");
        await Assert.That(dart).Contains("identical(this, other)");
        await Assert.That(dart).Contains("super.hashCode");
        await Assert.That(dart).Contains("'Box'");
    }

    [Test]
    public async Task DartTarget_EmittedBaseDeclaringTheMember_KeepsSuper()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public int Cents;

                public virtual string ToString() => "money";
            }

            [Transpile]
            public class Coins : Money
            {
                public override string ToString() => base.ToString() + "!";
            }
            """
        );

        await Assert.That(files["coins.dart"]).Contains("super.toString()");
    }
}
