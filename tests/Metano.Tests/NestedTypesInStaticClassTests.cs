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
        await Assert.That(output).Contains("private readonly _item: Holder.Item;");
        await Assert.That(output).Contains("this._item = new Holder.Item(value);");
        await Assert.That(output).Contains("export namespace Holder {");
        await Assert.That(output).Contains("export class Item {");
    }

    [Test]
    public async Task ModuleShapedStaticClass_KeepsPrivateOnlyCompanionModuleLocal()
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
        await Assert.That(output).Contains("const pair = new Pairs.Pair(a, b);");
        await Assert.That(output).Contains("\nnamespace Pairs {");
        await Assert.That(output).DoesNotContain("export namespace Pairs");
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
        await Assert.That(output).Contains("export enum Tone {");
        await Assert.That(output).Contains("return Palette.Tone.Dark;");
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
        await Assert.That(output).Contains("export class Kept {");
        await Assert.That(output).DoesNotContain("NativeOnly");
    }
}
