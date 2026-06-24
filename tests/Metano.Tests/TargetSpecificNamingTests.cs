namespace Metano.Tests;

/// <summary>
/// Tests that the target-specific <c>[Name]</c> override resolves consistently
/// across every TypeScript emitter — not just type-name lookups. Legacy
/// handlers (ModuleTransformer, InvocationHandler, TypeGuardBuilder,
/// TypeCheckGenerator, MemberAccessHandler, etc.) used to call
/// <c>SymbolHelper.GetNameOverride(symbol)</c> without a target, which
/// silently dropped <c>[Name(TargetLanguage.TypeScript, "…")]</c>. Every such
/// call site now threads <see cref="TargetLanguage.TypeScript"/> explicitly;
/// these tests pin the expected behavior on the call paths the review
/// flagged.
/// </summary>
public class TargetSpecificNamingTests
{
    [Test]
    public async Task TargetSpecificNameOnMethod_AppliesAtDeclarationAndCallSite()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                [Name(TargetLanguage.TypeScript, "render")]
                public void Draw() { }

                public void DrawTwice() { Draw(); Draw(); }
            }
            """
        )["widget.ts"];

        // The method declaration picks up the TS-specific rename.
        await Assert.That(output).Contains("render()");
        // So does the call site (InvocationHandler routed through the
        // target-aware override).
        await Assert.That(output).Contains("this.render();");
        await Assert.That(output).DoesNotContain("this.draw(");
    }

    [Test]
    public async Task TargetSpecificNameOnProperty_AppliesAcrossRecordEmission()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Money
            {
                [Name(TargetLanguage.TypeScript, "cents")]
                public int Amount { get; init; }
            }
            """
        )["money.ts"];

        // The class-emitter pipeline reaches GetNameOverride at property emission.
        await Assert.That(output).Contains("cents:");
        await Assert.That(output).DoesNotContain("amount:");
    }

    [Test]
    public async Task TargetSpecificNameOnEnumMember_AppliesToStringEnum()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum Status
            {
                [Name(TargetLanguage.TypeScript, "in-progress")]
                InProgress,
                Done,
            }
            """
        )["status.ts"];

        await Assert.That(output).Contains("in-progress");
    }

    [Test]
    public async Task UntargetedName_AppliesWhenNoTypeScriptOverride()
    {
        // Untargeted [Name] keeps working as a global default when the TS-
        // specific one isn't set.
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                [Name("renderItem")]
                public void Draw() { }
            }
            """
        )["widget.ts"];

        await Assert.That(output).Contains("renderItem()");
    }

    [Test]
    public async Task NumericEnumMemberRename_IsConsistentBetweenDeclarationAndReference()
    {
        // Numeric enums honor [Name] on the member KEY — both the declaration
        // and every EnumType.Member reference must pick up the rename so the
        // generated TS actually type-checks.
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Status
            {
                [Name(TargetLanguage.TypeScript, "InProgress")]
                WorkInProgress,
                Done,
            }

            [Transpile]
            public class Ticket
            {
                public Status Current { get; set; } = Status.WorkInProgress;
            }
            """
        );
        var enumOutput = output["status.ts"];
        var consumerOutput = output["ticket.ts"];

        // Declaration renames the key.
        await Assert.That(enumOutput).Contains("InProgress");
        // Reference must point at the same key.
        await Assert.That(consumerOutput).Contains("Status.InProgress");
        await Assert.That(consumerOutput).DoesNotContain("Status.WorkInProgress");
    }

    [Test]
    public async Task StringEnumMemberRename_AffectsValueNotKey()
    {
        // [StringEnum] emits a const object whose KEYS stay on the CLR name
        // while the [Name] override only changes the string VALUE. References
        // (including default initializers) must keep using the CLR key so the
        // property access stays valid JS.
        var output = TranspileHelper.Transpile(
            """
            [Transpile, StringEnum]
            public enum Status
            {
                [Name(TargetLanguage.TypeScript, "in-progress")]
                InProgress,
                Done,
            }

            [Transpile]
            public class Ticket
            {
                public Status Current { get; set; }
            }
            """
        );
        var enumOutput = output["status.ts"];
        var consumerOutput = output["ticket.ts"];

        // The string VALUE carries the override …
        await Assert.That(enumOutput).Contains("\"in-progress\"");
        // … but the KEY stays on the C# member name.
        await Assert.That(enumOutput).Contains("InProgress:");
        // Default initializer (`= default(Status)`) picks the first member's
        // KEY, which must be the CLR name to stay a valid property access.
        await Assert.That(consumerOutput).Contains("Status.InProgress");
        await Assert.That(consumerOutput).DoesNotContain("Status.in-progress");
    }

    [Test]
    public async Task EnumTypeRename_FlowsIntoImplicitDefaultInitializer()
    {
        // When the enum TYPE itself carries a [Name] override for TS, every
        // reference (including the implicit `default(E)` synthesized for an
        // uninitialized auto-property) must use the renamed type name —
        // otherwise the field initializer points at an identifier no one
        // ever emits.
        var output = TranspileHelper.Transpile(
            """
            [Transpile, Name(TargetLanguage.TypeScript, "Renamed")]
            public enum MyEnum { Zero, One }

            [Transpile]
            public class Holder { public MyEnum Value { get; set; } }
            """
        );
        var holder = output["holder.ts"];
        await Assert.That(holder).Contains("Renamed.Zero");
        await Assert.That(holder).DoesNotContain("MyEnum.Zero");
    }

    [Test]
    public async Task TargetSpecificName_PropagatesThroughMethodReturnType()
    {
        // #170: a class renamed via [Name(TargetLanguage.TypeScript, "MyColumn")]
        // must surface the new name at every type-reference position — not just
        // at construction sites. Method return types, parameter types, generic
        // arguments, and constraints all flow through IrTypeRefMapper, which
        // must thread the active target so the rename hits BuildQualifiedName.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, Name(TargetLanguage.TypeScript, "MyColumn")]
                public class Column { }

                [Transpile]
                public class Caller
                {
                    public Column Make() => new Column();
                    public void Take(Column c) { }
                }
            }
            """
        );

        var caller = result["app/caller.ts"];
        // Return type position
        await Assert.That(caller).Contains("make(): MyColumn");
        await Assert.That(caller).DoesNotContain("make(): Column");
        // Parameter type position
        await Assert.That(caller).Contains("c: MyColumn");
        await Assert.That(caller).DoesNotContain("c: Column");
        // Construction (already worked, regression guard)
        await Assert.That(caller).Contains("new MyColumn()");
        // Import line carries the renamed identifier
        await Assert.That(caller).Contains("import { MyColumn }");
    }

    [Test]
    public async Task TargetSpecificName_PropagatesThroughGenericTypeArgument()
    {
        // Same fix scope: a renamed class used as the type argument of a
        // generic container must carry the rename. Without this, an
        // `IList<Column>` return type lowers to `Array<Column>` while the
        // import line resolves only `MyColumn`.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App
            {
                [Transpile, Name(TargetLanguage.TypeScript, "MyColumn")]
                public class Column { }

                [Transpile]
                public class Caller
                {
                    public Dictionary<string, Column> All() => new Dictionary<string, Column>();
                }
            }
            """
        );

        var caller = result["app/caller.ts"];
        await Assert.That(caller).Contains("Map<string, MyColumn>");
        await Assert.That(caller).DoesNotContain("Map<string, Column>");
    }

    [Test]
    public async Task TargetSpecificName_PropagatesThroughInterfaceMember()
    {
        // Interfaces use IrInterfaceExtractor (separate from IrMethodExtractor);
        // the rename must propagate through both extractor families.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, Name(TargetLanguage.TypeScript, "MyColumn")]
                public class Column { }

                [Transpile]
                public interface IFactory
                {
                    Column Build();
                    void Receive(Column c);
                }
            }
            """
        );

        var iface = result["app/i-factory.ts"];
        await Assert.That(iface).Contains("build(): MyColumn");
        await Assert.That(iface).Contains("c: MyColumn");
        await Assert.That(iface).DoesNotContain(": Column");
    }

    [Test]
    public async Task TargetSpecificIgnore_SkipsMemberOnMatchingTargetOnly()
    {
        // `[Ignore(TargetLanguage.Dart)]` on a member must not affect the TS
        // output — the method still emits for TS, only Dart drops it.
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                [Ignore(TargetLanguage.Dart)]
                public void DartUnfriendly() { }

                public void Regular() { }
            }
            """
        )["widget.ts"];

        await Assert.That(output).Contains("dartUnfriendly()");
        await Assert.That(output).Contains("regular()");
    }

    [Test]
    public async Task UntargetedIgnore_SkipsMemberEverywhere()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                [Ignore]
                public void Skipped() { }

                public void Kept() { }
            }
            """
        )["widget.ts"];

        await Assert.That(output).DoesNotContain("skipped(");
        await Assert.That(output).Contains("kept()");
    }

    [Test]
    public async Task IgnorePerTarget_DoesNotSuppressOtherTargets()
    {
        // Codex P1 regression: [Ignore(TargetLanguage.Dart)] on a type must
        // NOT filter the type out of TS discovery. Otherwise foo.ts is never
        // emitted and related checks (nested/base types, imports) also treat
        // the type as non-transpilable.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, Ignore(TargetLanguage.Dart)]
            public class Foo
            {
                public int Value { get; }
                public Foo(int value) { Value = value; }
            }
            """
        );

        await Assert.That(result.Keys).Contains("foo.ts");
        await Assert.That(result["foo.ts"]).Contains("class Foo");
    }

    [Test]
    public async Task DartOverride_DoesNotAffectTypeScriptOutput()
    {
        // A Dart-specific rename on a TS-emitted symbol must NOT leak into
        // the TS output — it should fall back to the C# name's camelCase form.
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Widget
            {
                [Name(TargetLanguage.Dart, "drawFromDart")]
                public void Draw() { }
            }
            """
        )["widget.ts"];

        await Assert.That(output).Contains("draw()");
        await Assert.That(output).DoesNotContain("drawFromDart");
    }
}
