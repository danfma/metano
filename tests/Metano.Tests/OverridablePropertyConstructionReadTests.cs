using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A constructor body that reads a property a derived class overrides observes construction order the generated
/// code does not reproduce (#366): MS0001 is reported at the read.
/// </summary>
public class OverridablePropertyConstructionReadTests
{
    [Test]
    public async Task ConstructorReadingAbstractProperty_ReportsLocatedWarning()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public string Tag { get; }

                public abstract int Id { get; init; }

                protected Ent() { Tag = "e" + Id; }
            }
            """
        );

        var report = ConstructionReads(diagnostics).Single();

        await Assert.That(report.Code).IsEqualTo("MS0001");
        await Assert.That(report.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(report.Message).Contains("'Ent.Id'");
        await Assert.That(report.Message).Contains("TS2715");

        var span = report.Location!.GetLineSpan();

        await Assert
            .That(
                report.Location!.SourceTree!.GetText().Lines[span.StartLinePosition.Line].ToString()
            )
            .Contains("protected Ent()");
        await Assert
            .That(report.Location.SourceTree!.GetText().ToString(report.Location.SourceSpan))
            .IsEqualTo("Id");
    }

    [Test]
    public async Task ConstructorReadingVirtualPropertyThroughExplicitThis_Reports()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ent
            {
                public string Tag { get; }

                public virtual int Id { get; } = 1;

                public Ent() { Tag = "e" + this.Id; }
            }

            [Transpile]
            public class Derived : Ent
            {
                public override int Id => 2;
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics).Count).IsEqualTo(1);
    }

    [Test]
    public async Task VirtualPropertyWithoutOverrideInCompilation_DoesNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ent
            {
                public string Tag { get; }

                public virtual int Id { get; } = 1;

                public Ent() { Tag = "e" + Id; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task UnsealedOverrideRead_Reports()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public abstract int Id { get; }
            }

            [Transpile]
            public class Mid : Ent
            {
                public string Tag { get; }

                public override int Id => 1;

                public Mid() { Tag = "m" + Id; }
            }

            [Transpile]
            public class Last : Mid
            {
                public override int Id => 3;
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SealedOverrideNonVirtualPropertySealedClassAndBaseAccess_DoNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public abstract int Id { get; }

                public virtual int Weight { get; } = 1;

                public int Plain { get; } = 2;
            }

            [Transpile]
            public class Mid : Ent
            {
                public string Tag { get; }

                public sealed override int Id => 1;

                public Mid() { Tag = "m" + Id + Plain + base.Weight; }
            }

            [Transpile]
            public sealed class Leaf : Ent
            {
                public string Tag { get; }

                public override int Id => 1;

                public Leaf() { Tag = "l" + Id + Weight; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ReadsInLambdasLocalFunctionsAndMethods_DoNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public abstract class Ent
            {
                public Func<int> Reader { get; }

                public abstract int Id { get; }

                protected Ent()
                {
                    Reader = () => Id;

                    int Local() => Id;
                }

                public int Later() => Id;
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task WriteToOverridableProperty_DoesNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ent
            {
                public virtual int Id { get; set; }

                public Ent() { Id = 5; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task WriteThroughOverridableReceiver_ReportsTheReceiverRead()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Box
            {
                public int Value { get; set; }
            }

            [Transpile]
            public abstract class Ent
            {
                public abstract Box Inner { get; }

                protected Ent() { Inner.Value = 1; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics).Count).IsEqualTo(1);
    }

    [Test]
    public async Task InheritedVirtualPropertyReadInDerivedConstructor_Reports()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public abstract int Id { get; }
            }

            [Transpile]
            public abstract class Mid : Ent
            {
                public string Tag { get; }

                protected Mid() { Tag = "m" + Id; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics).Count).IsEqualTo(1);
    }

    [Test]
    public async Task NameofOfOverridableProperty_DoesNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public string Tag { get; }

                public abstract int Id { get; }

                protected Ent() { Tag = nameof(Id); }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task CompoundAssignmentToOverridableProperty_Reports()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ent
            {
                public virtual int Id { get; set; }

                public Ent() { Id += 1; }
            }

            [Transpile]
            public class Derived : Ent
            {
                public override int Id { get; set; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics).Count).IsEqualTo(1);
    }

    [Test]
    public async Task DeconstructionAssignmentToOverridableProperty_DoesNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public abstract int Id { get; set; }

                public int Other { get; set; }

                protected Ent() { (Id, Other) = (1, 2); }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task IgnoredAbstractProperty_DoesNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public string Tag { get; }

                [Ignore]
                public abstract int Id { get; }

                protected Ent() { Tag = "e" + Id; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ConstructorCallingVirtualMethod_DoesNotReport()
    {
        var (_, diagnostics) = TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Ent
            {
                public int Tag { get; }

                protected abstract int Compute();

                protected Ent() { Tag = Compute(); }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task DartTarget_ReportsTheSameRead()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Ent
            {
                public string Tag { get; }

                public abstract int Id { get; }

                protected Ent() { Tag = "e" + Id; }
            }
            """
        );

        await Assert.That(ConstructionReads(diagnostics).Count).IsEqualTo(1);
    }

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) TranspileWithDiagnostics(string source) => TranspileHelper.TranspileWithDiagnostics(source);

    private static List<MetanoDiagnostic> ConstructionReads(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Message.Contains("while the object is still being constructed")
            )
            .ToList();
}
