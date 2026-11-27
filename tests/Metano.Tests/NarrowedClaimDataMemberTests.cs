using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #822: a pure property or event forwarder of a public interface member to a less
/// accessible inherited member is published on the implementing class, and the claiming
/// member keeps its tag, so a sibling subclass exposes nothing. A shape the class cannot
/// publish keeps the 4.0.0 path with an MS0001 warning. Every published shape is
/// type-checked with <c>tsgo --strict</c> (<c>noImplicitOverride</c>).
/// </summary>
public class NarrowedClaimDataMemberTests
{
    private const string NoteLibrary = """
        public interface INote { event System.Action Changed; }
        public interface ISeeded { int Seed { get; init; } }
        public interface IIndexed { int this[int index] { get; } }
        """;

    [Test]
    public async Task GetOnlyPropertyOverAccessors_IsPublishedThroughSuper_AndTheSiblingExposesNothing()
    {
        var files = await StrictFiles(
            """
            using System.ComponentModel;

            [assembly: TranspileAssembly]

            public abstract class Base
            {
                internal bool IsChanged => true;
                internal void AcceptChanges() { }
            }
            public sealed class Tracker : Base, IChangeTracking
            {
                bool IChangeTracking.IsChanged => IsChanged;
                void IChangeTracking.AcceptChanges() => AcceptChanges();
            }
            public sealed class Sibling : Base { }
            """
        );

        await Assert
            .That(files["base.ts"])
            .Contains("/** @internal */\n  get isChanged(): boolean {");

        await Assert
            .That(files["tracker.ts"])
            .Contains("  override get isChanged(): boolean {\n    return super.isChanged;\n  }");

        await Assert.That(files["sibling.ts"]).DoesNotContain("isChanged");
    }

    [Test]
    public async Task GetSetAutoProperty_IsRedeclaredPublic_WithoutStorageOfItsOwn()
    {
        var files = await StrictFiles(
            """
            using System.Runtime.CompilerServices;

            [assembly: TranspileAssembly]

            public abstract class Base { internal object? Value { get; set; } }
            public sealed class Box : Base, IStrongBox
            {
                object? IStrongBox.Value { get => Value; set => Value = value; }
            }
            public sealed class Sibling : Base { }
            """
        );

        await Assert
            .That(files["base.ts"])
            .Contains("/** @internal */\n  declare value: Object | null;");

        await Assert.That(files["box.ts"]).Contains("\n  declare value: Object | null;\n");

        await Assert
            .That(files["box.ts"])
            .DoesNotContain("override")
            .And.DoesNotContain("super.value");

        await Assert.That(files["sibling.ts"]).DoesNotContain("value");
    }

    [Test]
    public async Task GetSetPropertyOverAccessors_OverridesBothAccessorsThroughSuper()
    {
        var files = await StrictFiles(
            """
            using System.Runtime.CompilerServices;

            [assembly: TranspileAssembly]

            public abstract class Base
            {
                private object? _value;
                internal object? Value { get => _value; set => _value = value; }
            }
            public sealed class Box : Base, IStrongBox
            {
                object? IStrongBox.Value { get => Value; set => Value = value; }
            }
            """
        );

        await Assert.That(files["base.ts"]).Contains("/** @internal */\n  set value(");

        await Assert
            .That(files["box.ts"])
            .Contains("  override get value(): Object | null {\n    return super.value;\n  }")
            .And.Contains(
                "  override set value(value: Object | null) {\n    super.value = value;\n  }"
            );
    }

    [Test]
    public async Task GetOnlyForwarderOverWritableAccessors_KeepsTheInheritedSetterInternal()
    {
        // A JavaScript accessor declaration replaces the base's pair, so the setter the
        // interface does not publish is forwarded too, under the inherited visibility.
        var files = await StrictFiles(
            """
            using System.ComponentModel;

            [assembly: TranspileAssembly]

            public abstract class Base
            {
                private bool _changed;
                internal bool IsChanged { get => _changed; set => _changed = value; }
                internal void AcceptChanges() => IsChanged = false;
            }
            public sealed class Tracker : Base, IChangeTracking
            {
                bool IChangeTracking.IsChanged => IsChanged;
                void IChangeTracking.AcceptChanges() => AcceptChanges();
            }
            """
        );

        await Assert
            .That(files["tracker.ts"])
            .Contains("  override get isChanged(): boolean {")
            .And.Contains("  /** @internal */\n  override set isChanged(value: boolean) {");
    }

    [Test]
    public async Task AutoPropertyWithAPrivateSetter_IsRedeclaredReadonly()
    {
        var files = await StrictFiles(
            """
            using System.ComponentModel;

            [assembly: TranspileAssembly]

            public abstract class Base
            {
                internal bool IsChanged { get; private set; }
                internal void AcceptChanges() => IsChanged = false;
            }
            public sealed class Tracker : Base, IChangeTracking
            {
                bool IChangeTracking.IsChanged => IsChanged;
                void IChangeTracking.AcceptChanges() => AcceptChanges();
            }
            """
        );

        await Assert
            .That(files["tracker.ts"])
            .Contains("\n  declare readonly isChanged: boolean;\n");
    }

    [Test]
    public async Task InitProperty_IsRedeclaredReadonly()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryImageAndDiagnostics(
            NoteLibrary,
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal int Seed { get; init; } }
            public sealed class Seeded : Base, ISeeded
            {
                int ISeeded.Seed { get => Seed; init => Seed = value; }
            }
            public sealed class Sibling : Base { }
            """
        );

        await Assert.That(diagnostics.Select(d => d.Code)).DoesNotContain("MS0001");

        await Assert
            .That(files["base.ts"])
            .Contains("/** @internal */\n  declare readonly seed: number;");

        await Assert.That(files["seeded.ts"]).Contains("\n  declare readonly seed: number;\n");
        await Assert.That(files["sibling.ts"]).DoesNotContain("seed");
        await AssertStrict(files);
    }

    [Test]
    public async Task Event_IsPublishedThroughSuper_WithNoStorageAndNoCustomAccessorError()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryImageAndDiagnostics(
            NoteLibrary,
            """
            [assembly: TranspileAssembly]

            public abstract class Base
            {
                internal event System.Action Changed = () => { };
            }
            public sealed class Note : Base, INote
            {
                event System.Action INote.Changed { add => Changed += value; remove => Changed -= value; }
            }
            public sealed class Sibling : Base { }
            """
        );

        await Assert.That(diagnostics.Select(d => d.Code)).DoesNotContain("MS0001");
        await Assert.That(files["base.ts"]).Contains("/** @internal */\n  changed$add(");

        await Assert
            .That(files["note.ts"])
            .Contains(
                "  override changed$add(handler: () => void): void {\n"
                    + "    super.changed$add(handler);\n  }"
            )
            .And.Contains("    super.changed$remove(handler);")
            .And.DoesNotContain("delegateAdd")
            .And.DoesNotContain("private changed");

        await Assert.That(files["sibling.ts"]).DoesNotContain("changed");
        await AssertStrict(files);
    }

    [Test]
    public async Task IndexerForwarder_IsLeftToTheIndexerDiagnostic()
    {
        // No target emits an indexer (IndexerDiagnostics reports it), so an indexer
        // forwarder claims nothing and publishes nothing.
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryImageAndDiagnostics(
            NoteLibrary,
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal int this[int index] => index; }
            public sealed class Items : Base, IIndexed
            {
                int IIndexed.this[int index] => this[index];
            }
            """
        );

        await Assert.That(files["items.ts"]).DoesNotContain("super.");
        await Assert.That(UnpublishedWarnings(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task AbstractPropertyClaimer_WarnsAndKeepsThe4_0_0Path()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.ComponentModel;

            [assembly: TranspileAssembly]

            public abstract class Base
            {
                internal abstract bool IsChanged { get; }
                internal void AcceptChanges() { }
            }
            public abstract class Tracker : Base, IChangeTracking
            {
                bool IChangeTracking.IsChanged => IsChanged;
                void IChangeTracking.AcceptChanges() => AcceptChanges();
            }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "IsChanged", "is abstract");
        await Assert.That(files["base.ts"]).Contains("  abstract readonly isChanged: boolean;");
        await Assert.That(files["tracker.ts"]).DoesNotContain("isChanged");
    }

    [Test]
    public async Task PropertyOfAnotherType_Warns()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Runtime.CompilerServices;

            [assembly: TranspileAssembly]

            public abstract class Base { internal string? Value { get; set; } }
            public sealed class Box : Base, IStrongBox
            {
                object? IStrongBox.Value { get => Value; set => Value = (string?)value; }
            }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "Value", "its type is not the type");
    }

    [Test]
    public async Task PropertyForwarderThatDoesMore_Warns()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.ComponentModel;

            [assembly: TranspileAssembly]

            public abstract class Base
            {
                internal bool IsChanged => true;
                internal void AcceptChanges() { }
            }
            public sealed class Tracker : Base, IChangeTracking
            {
                bool IChangeTracking.IsChanged => !IsChanged;
                void IChangeTracking.AcceptChanges() => AcceptChanges();
            }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "IsChanged", "does more than forward");
    }

    [Test]
    public async Task InitOnlyAccessorClaimer_Warns()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryImageAndDiagnostics(
            NoteLibrary,
            """
            [assembly: TranspileAssembly]

            public abstract class Base
            {
                private int _seed;
                internal int Seed { get => _seed; init => _seed = value; }
            }
            public sealed class Seeded : Base, ISeeded
            {
                int ISeeded.Seed { get => Seed; init => Seed = value; }
            }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "Seed", "'init' or private setter");
    }

    [Test]
    public async Task ReturnTypeMismatchOfAMethodForwarder_Warns()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            public class Base { internal Base Clone() => this; }
            public sealed class Derived : Base, ICloneable
            {
                object ICloneable.Clone() => Clone();
            }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "Clone", "its type is not the type");
    }

    [Test]
    public async Task GenericMethodForwarder_Warns()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryImageAndDiagnostics(
            """
            public interface IMaker { T Make<T>(); }
            """,
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal T Make<T>() => default!; }
            public sealed class Impl : Base, IMaker
            {
                T IMaker.Make<T>() => Make<T>();
            }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "Make", "generic method");
    }

    [Test]
    public async Task AbstractMethodClaimer_Warns()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            public abstract class ABase { internal abstract void Dispose(); }
            public abstract class AEffect : ABase, IDisposable
            {
                void IDisposable.Dispose() => Dispose();
            }
            public sealed class ALeaf : AEffect { internal override void Dispose() { } }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "Dispose", "is abstract");
    }

    [Test]
    public async Task OverloadedMethodClaimer_Warns()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            public abstract class Base
            {
                internal void Dispose() { }
                internal void Dispose(bool disposing) { }
            }
            public sealed class Effect : Base, IDisposable
            {
                void IDisposable.Dispose() => Dispose();
            }
            """
        );

        await AssertSingleUnpublishedWarning(diagnostics, "Dispose", "has overloads");
    }

    [Test]
    public async Task ForwardersOfDifferentInterfacesClaimingEachOther_WarnOnEach()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal int CompareTo(object? other) => 0; }
            public sealed class Derived : Base, IComparable, IComparable<Derived>
            {
                int IComparable.CompareTo(object? other) => CompareTo(other);
                int IComparable<Derived>.CompareTo(Derived? other) => CompareTo(other);
            }
            """
        );

        var warnings = UnpublishedWarnings(diagnostics);

        await Assert.That(warnings.Count).IsEqualTo(2);
        await Assert.That(warnings).All(d => d.Message.Contains("claims the same name"));
    }

    [Test]
    public async Task PublicClaimer_KeepsTheFr111DropWithoutWarning()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.ComponentModel;

            [assembly: TranspileAssembly]

            public abstract class Base
            {
                public bool IsChanged => true;
                public void AcceptChanges() { }
            }
            public sealed class Tracker : Base, IChangeTracking
            {
                bool IChangeTracking.IsChanged => IsChanged;
                void IChangeTracking.AcceptChanges() => AcceptChanges();
            }
            """
        );

        await Assert.That(UnpublishedWarnings(diagnostics)).IsEmpty();
        await Assert.That(files["tracker.ts"]).DoesNotContain("isChanged");
    }

    [Test]
    public async Task DartKeepsThePropertyAndEventForwardersDropped()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using System.ComponentModel;
            using Metano.Annotations;

            [Transpile]
            public abstract class Base
            {
                internal bool IsChanged => true;
                internal void AcceptChanges() { }
            }

            [Transpile]
            public sealed class Tracker : Base, IChangeTracking
            {
                bool IChangeTracking.IsChanged => IsChanged;
                void IChangeTracking.AcceptChanges() => AcceptChanges();
            }
            """
        );

        await Assert.That(UnpublishedWarnings(diagnostics)).IsEmpty();

        await Assert
            .That(files.Single(file => file.Key.Contains("tracker")).Value)
            .DoesNotContain("isChanged");
    }

    private static async Task<Dictionary<string, string>> StrictFiles(string source)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        await Assert.That(UnpublishedWarnings(diagnostics)).IsEmpty();
        await AssertStrict(files);

        return files;
    }

    private static async Task AssertStrict(Dictionary<string, string> files)
    {
        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    private static async Task AssertSingleUnpublishedWarning(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string member,
        string reason
    )
    {
        var warning = await Assert.That(UnpublishedWarnings(diagnostics)).HasSingleItem();

        await Assert.That(warning!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(warning.Message).Contains(member).And.Contains(reason);
    }

    private static List<MetanoDiagnostic> UnpublishedWarnings(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Where(d => d.Message.Contains("cannot be published on"))
            .ToList();
}
