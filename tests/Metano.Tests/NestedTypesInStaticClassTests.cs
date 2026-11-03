namespace Metano.Tests;

/// <summary>
/// Pins the companion-namespace emission of nested types whatever their C#
/// accessibility (#264). A static class holding a <c>private</c> nested record used
/// to emit references (<c>new ShotSchedule.ShotEntry(…)</c>) to a type that was never
/// declared (TS2702 / TS2339); unqualified value-position references inside the
/// container (<c>Kind.B</c>) and explicit ones from outside
/// (<c>ShotSchedule.Slot.Create()</c>) also lowered to unresolvable names.
/// </summary>
public class NestedTypesInStaticClassTests
{
    private const string ShotScheduleSource = """
        [assembly: TranspileAssembly]

        public static class ShotSchedule
        {
            private sealed record ShotEntry(string Name, int Frames)
            {
                public static ShotEntry Empty() => new ShotEntry("", 0);

                public ShotEntry Twice() => Empty() with { Frames = Frames * 2 };
            }

            internal enum Kind { A, B }

            public sealed record Slot(int Index)
            {
                public static Slot Create() => new Slot(7);
            }

            public sealed class Box<T>
            {
                public Box(T value) { Value = value; }

                public T Value { get; }
            }

            private static class Deep
            {
                internal sealed record Leaf(int N);

                public static Leaf Make() => new Leaf(5);
            }

            public static int Total(string name)
            {
                var entry = new ShotEntry(name, 3);
                var kind = Kind.B;
                var box = new Box<ShotEntry>(entry);
                object boxed = entry;
                var isEntry = boxed is ShotEntry ? 1 : 0;

                return box.Value.Frames
                    + (kind == Kind.B ? 1 : 0)
                    + ShotEntry.Empty().Frames
                    + isEntry
                    + Slot.Create().Index
                    + Deep.Make().N
                    + new Deep.Leaf(1).N
                    + entry.Twice().Frames;
            }

            internal static int KindValue(Kind kind = Kind.B) => (int)kind;

            public static Slot First() => new Slot(0);
        }

        public static class ShotConsumer
        {
            public static ShotSchedule.Slot Make() => ShotSchedule.Slot.Create();

            public static int FirstKind() => ShotSchedule.KindValue(ShotSchedule.Kind.A);

            public static bool IsSlot(object value) => value is ShotSchedule.Slot;
        }
        """;

    [Test]
    public async Task StaticClass_EmitsNestedTypesOfEveryAccessibility()
    {
        var result = TranspileHelper.Transpile(ShotScheduleSource);

        var expected = TranspileHelper.ReadExpected("nested-types-static-class.ts");
        await Assert.That(result["shot-schedule.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task ExplicitNestedQualification_KeepsTypeSegmentsAndImportsContainer()
    {
        var result = TranspileHelper.Transpile(ShotScheduleSource);

        var output = result["shot-consumer.ts"];
        await Assert.That(output).Contains("import { ShotSchedule } from \"./shot-schedule\"");
        await Assert.That(output).Contains("return ShotSchedule.Slot.create();");
        await Assert.That(output).Contains("ShotSchedule.kindValue(ShotSchedule.Kind.A)");
        await Assert.That(output).Contains("value instanceof ShotSchedule.Slot");
    }

    [Test]
    public async Task NonPublicNestedTypes_GetNoBarrelEntry()
    {
        var result = TranspileHelper.Transpile(ShotScheduleSource);

        var barrel = result["index.ts"];
        await Assert.That(barrel).Contains("export { ShotSchedule } from \"./shot-schedule\"");
        await Assert.That(barrel).DoesNotContain("ShotEntry }");
        await Assert.That(barrel).DoesNotContain("ShotSchedule.ShotEntry");
        await Assert.That(barrel).DoesNotContain("export { Kind");
        await Assert.That(barrel).DoesNotContain("ShotSchedule.Kind");
    }

    [Test]
    public async Task InstanceClass_EmitsPrivateNestedClassUsedAsFieldType()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public sealed class Holder
            {
                private readonly Item _item;

                public Holder(int value) { _item = new Item(value); }

                public int Value => _item.V;

                private sealed class Item
                {
                    public Item(int v) { V = v; }

                    public int V { get; }
                }
            }
            """
        );

        var output = result["holder.ts"];
        await Assert.That(output).Contains("private readonly _item: Holder$Item;");
        await Assert.That(output).Contains("this._item = new Holder$Item(value);");
        await Assert.That(output).Contains("class Holder$Item {");
        await Assert.That(output).DoesNotContain("export class Holder$Item");
        await Assert.That(output).DoesNotContain("namespace Holder");
    }

    [Test]
    public async Task ModuleShapedStaticClass_DeclaresPrivateNestedTypeModuleLocal()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Pairs
            {
                private sealed record Pair(int A, int B);

                public static int Sum(int a, int b)
                {
                    var pair = new Pair(a, b);
                    return pair.A + pair.B;
                }
            }

            public sealed record Marker(int Id);
            """
        );

        var output = result["pairs.ts"];
        await Assert.That(output).Contains("const pair = new Pairs$Pair(a, b);");
        await Assert.That(output).Contains("class Pairs$Pair {");
        await Assert.That(output).DoesNotContain("export class Pairs$Pair");
        await Assert.That(output).DoesNotContain("namespace Pairs");
        var barrel = result["index.ts"];
        await Assert.That(barrel).Contains("export { Marker } from \"./marker\"");
        await Assert.That(barrel).DoesNotContain("Pairs");
    }

    [Test]
    public async Task ModuleShapedStaticClass_ExportsCompanionWithInternalNestedType()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Pairs
            {
                internal sealed record Pair(int A, int B);

                public static int Sum(int a, int b)
                {
                    var pair = new Pair(a, b);
                    return pair.A + pair.B;
                }
            }

            public static class PairUser
            {
                public static int First() => new Pairs.Pair(1, 2).A;
            }
            """
        );

        await Assert.That(result["pairs.ts"]).Contains("export namespace Pairs {");
        var user = result["pair-user.ts"];
        await Assert.That(user).Contains("import { Pairs } from \"./pairs\"");
        await Assert.That(user).Contains("new Pairs.Pair(1, 2).a");
    }

    [Test]
    public async Task RenamedNestedType_ValueReferenceHonorsNameOverride()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Palette
            {
                [Name("Tone")]
                private enum Shade { Light, Dark }

                public static int Darkest() => (int)Shade.Dark;
            }
            """
        );

        var output = result["palette.ts"];
        await Assert.That(output).Contains("enum Palette$Tone {");
        await Assert.That(output).DoesNotContain("export enum Palette$Tone");
        await Assert.That(output).Contains("return Palette$Tone.Dark;");
    }

    [Test]
    public async Task ExplicitTranspileMode_EmitsInternalNestedTypeMarkedTranspile()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Registry
            {
                [Transpile]
                internal enum Level { Low, High }

                internal sealed record Untracked(int Value);

                public static int Top() => (int)Level.High;
            }
            """
        );

        var output = result["registry.ts"];
        await Assert.That(output).Contains("return Registry.Level.High;");
        await Assert.That(output).Contains("export enum Level {");
        // Outside [assembly: TranspileAssembly] a nested type still needs its own
        // [Transpile] opt-in, whatever its accessibility.
        await Assert.That(output).DoesNotContain("Untracked");
    }

    [Test]
    public async Task IgnoredNonPublicNestedType_IsNotEmitted()
    {
        // Assembly-wide mode emits every nested type of an opted-in container, even
        // one no emitted member references; [Ignore] is the documented opt-out.
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public sealed class Svc
            {
                public int Count() => 1;

                [Ignore]
                private static class NativeOnly
                {
                    public static int Sum(int a, int b) => a + b;
                }

                private sealed record Kept(int Value);
            }
            """
        );

        var output = result["svc.ts"];
        await Assert.That(output).Contains("class Svc$Kept {");
        await Assert.That(output).DoesNotContain("export class Svc$Kept");
        await Assert.That(output).DoesNotContain("NativeOnly");
    }

    [Test]
    public async Task PrivateNestedType_UsedByOwnerAndSiblingNestedType_ResolvesToModuleLocalName()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Planner
            {
                private sealed record Step(int Frames);

                private sealed class Track
                {
                    public Step First { get; } = new Step(1);

                    public int Total(Step extra) => First.Frames + extra.Frames;
                }

                public static int Run() => new Track().Total(new Step(2));
            }
            """
        );

        var output = result["planner.ts"];
        await Assert.That(output).Contains("new Planner$Track().total(new Planner$Step(2))");
        await Assert.That(output).Contains("class Planner$Track {");
        await Assert.That(output).Contains("class Planner$Step {");
        await Assert.That(output).Contains("total(extra: Planner$Step): number {");
        await Assert.That(output).DoesNotContain("namespace Planner");
        await Assert.That(output).DoesNotContain("export class Planner$");
        await Assert.That(result["index.ts"]).DoesNotContain("Planner$");
    }

    [Test]
    public async Task PrivateNestedType_NextToPublicOne_LeavesOnlyThePublicOneInTheNamespace()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public sealed class Catalog
            {
                public sealed record Entry(int Id);

                private sealed record Cursor(int Position);

                public int Next() => new Cursor(1).Position + new Entry(2).Id;
            }
            """
        );

        var output = result["catalog.ts"];
        await Assert.That(output).Contains("export namespace Catalog {");
        await Assert.That(output).Contains("export class Entry {");
        await Assert.That(output).Contains("class Catalog$Cursor {");
        await Assert.That(output).DoesNotContain("export class Catalog$Cursor");
        await Assert.That(output).DoesNotContain("Catalog.Cursor");
    }

    [Test]
    public async Task ProtectedNestedTypes_StayExportedForDerivedTypesInOtherFiles()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public abstract class Base
            {
                protected sealed record Token(int Id);

                private protected sealed record Secret(int Id);

                protected static Token Make() => new Token(1);
            }

            public sealed class Derived : Base
            {
                public int Id() => Make().Id;
            }
            """
        );

        var output = result["base.ts"];
        await Assert.That(output).Contains("export namespace Base {");
        await Assert.That(output).Contains("export class Token {");
        await Assert.That(output).Contains("export class Secret {");
        await Assert.That(output).DoesNotContain("Base$");
    }

    [Test]
    public async Task InternalNestedType_StaysExportedForTheRestOfTheAssembly()
    {
        var result = TranspileHelper.Transpile(ShotScheduleSource);

        var output = result["shot-schedule.ts"];
        await Assert.That(output).Contains("export enum Kind {");
        await Assert.That(output).DoesNotContain("ShotSchedule$Kind");
    }

    [Test]
    public async Task PrivateNestedType_IsAbsentFromTheDeclarationSurface()
    {
        var result = TranspileHelper.Transpile(ShotScheduleSource);

        var output = result["shot-schedule.ts"];
        await Assert.That(output).Contains("class ShotSchedule$ShotEntry {");
        await Assert.That(output).Contains("class ShotSchedule$Deep {");
        await Assert.That(output).Contains("namespace ShotSchedule$Deep {");
        await Assert.That(output).DoesNotContain("export class ShotSchedule$");
        await Assert.That(output).DoesNotContain("export namespace ShotSchedule$");
    }

    [Test]
    public async Task PrivateNestedType_IsDeclaredBeforeTheOwnerStaticInitializersThatConstructIt()
    {
        // The `Pub.Zero` initializer runs at load time inside the namespace: the private
        // record must already be declared (TS2449 / ReferenceError otherwise).
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Holder
            {
                private sealed record P(int V);

                public sealed record Pub(int V)
                {
                    public static readonly Pub Zero = new Pub(new P(0).V);
                }
            }
            """
        );

        var output = result["holder.ts"];
        await Assert
            .That(output.IndexOf("class Holder$P {"))
            .IsLessThan(output.IndexOf("export class Holder {"));
        await Assert.That(output).Contains("new Holder.Pub(new Holder$P(0).v)");
    }

    [Test]
    public async Task PrivateNestedType_IsDeclaredBeforeTheOwnerStaticFieldThatConstructsIt()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public sealed class Registry
            {
                private sealed record Entry(int Id);

                private static readonly Entry Default = new Entry(1);

                public int Id() => Default.Id;
            }
            """
        );

        var output = result["registry.ts"];
        await Assert
            .That(output.IndexOf("class Registry$Entry {"))
            .IsLessThan(output.IndexOf("export class Registry {"));
    }

    [Test]
    public async Task PrivateNestedType_IsDeclaredBeforeTheModuleConstantThatConstructsIt()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Grid
            {
                private sealed record Cell(int X);

                private static readonly Cell Origin = new Cell(0);

                public static int OriginX() => Origin.X;
            }
            """
        );

        var output = result["grid.ts"];
        await Assert
            .That(output.IndexOf("class Grid$Cell {"))
            .IsLessThan(output.IndexOf("const origin: Grid$Cell = new Grid$Cell(0);"));
    }

    [Test]
    public async Task PrivateNestedType_ExtendingAMemberOfTheOwnerNamespace_FollowsIt()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Outer
            {
                public abstract class PublicBase
                {
                    public int V => 1;
                }

                private sealed class Impl : PublicBase { }

                public static int Run() => new Impl().V;
            }
            """
        );

        var output = result["outer.ts"];
        await Assert
            .That(output.IndexOf("class Outer$Impl extends Outer.PublicBase"))
            .IsGreaterThan(output.IndexOf("export class PublicBase"));
    }

    [Test]
    public async Task PrivateNestedType_WithNameOverride_FlattensTheOverriddenName()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Palette
            {
                [Name("Tone")]
                private sealed record Shade(int V);

                public static int Go() => new Shade(1).V;
            }
            """
        );

        var output = result["palette.ts"];
        await Assert.That(output).Contains("class Palette$Tone {");
        await Assert.That(output).Contains("new Palette$Tone(1)");
        await Assert.That(output).DoesNotContain("Shade");
    }

    [Test]
    public async Task PrivateNestedType_InsideAPublicNestedType_IsDeclaredModuleLocal()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Outer
            {
                public sealed class Mid
                {
                    private sealed record Leaf(int N);

                    public int Run() => new Leaf(1).N;
                }

                public static int Go() => new Mid().Run();
            }
            """
        );

        var output = result["outer.ts"];
        await Assert.That(output).Contains("\nclass Outer$Mid$Leaf {");
        await Assert.That(output).DoesNotContain("export class Outer$Mid$Leaf");
        await Assert.That(output).Contains("return new Outer$Mid$Leaf(1).n;");
        await Assert.That(output).Contains("export class Mid {");
    }

    [Test]
    public async Task PrivateNestedJsonContext_DeclaresAndReferencesTheSameModuleLocalName()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]

            public partial class Api
            {
                private record Payload(int Id);

                [JsonSerializable(typeof(Payload))]
                private partial class Ctx : JsonSerializerContext
                {
                    public Ctx() : base(new System.Text.Json.JsonSerializerOptions()) { }

                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;

                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
                }

                public object Make() => new Ctx();
            }
            """
        );

        var output = result["api.ts"];
        await Assert.That(output).Contains("class Api$Ctx extends SerializerContext {");
        await Assert.That(output).DoesNotContain("export class Api$Ctx");
        await Assert
            .That(output)
            .Contains("private static readonly _default: Api$Ctx = new Api$Ctx();");
        await Assert.That(output).Contains("get payload(): TypeSpec<Api$Payload>");
        await Assert.That(output).DoesNotContain("api$Payload");
    }
}
