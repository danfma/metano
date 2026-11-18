namespace Metano.Tests;

/// <summary>
/// Spec 011 FR-023 (stage 011b B2): a descriptor read of a closed generic
/// class passes the open descriptor through <c>specialize(open, [args])</c>,
/// at <c>typeof</c> and at the folded <c>GetType()</c> of a closed generic
/// struct (an open one asks the value), an argument with no
/// descriptor specializing to <c>UnknownType</c>. A class cast passes the
/// constructor (D29) and an <c>as</c> renders <c>instanceof</c>, so neither
/// reads a descriptor. Collections stay with stage 011c.
/// </summary>
public class RuntimeTypeClosedGenericTests
{
    internal const string ClosedGenericSource = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Boxes;

        public class Box<T>
        {
            public T? Value { get; set; }
        }

        public class Pair<TFirst, TSecond>
        {
            public TFirst? First { get; set; }

            public TSecond? Second { get; set; }
        }

        public readonly struct Cell<T>
        {
            public T Value { get; init; }
        }

        public class Gift { }

        public static class Probe
        {
            public static Type OfInt() => typeof(Box<int>);

            public static Type OfNested() => typeof(Box<Box<string>>);

            public static Type OfPair() => typeof(Pair<Gift, decimal>);

            public static string NameOf() => typeof(Box<int>).Name;

            public static Type OfCell(Cell<int> c) => c.GetType();

            public static Type OfOpenCell<T>(Cell<T> c) => c.GetType();

            public static Box<int> ToBox(object o) => (Box<int>)o;

            public static Box<int>? AsBox(object o) => o as Box<int>;
        }
        """;

    [Test]
    public async Task TheSource_ReportsNoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ClosedGenericSource);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task ClosedGenerics_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden(
            "closed-generic-descriptors.ts",
            RuntimeTypeDescriptorTests.TranspileAll(ClosedGenericSource)
        );

    [Test]
    public async Task ClosedGenerics_TypeOfSpecializesTheOpenDescriptor()
    {
        var probe = TranspileHelper.Transpile(ClosedGenericSource)["boxes/probe.ts"];

        await Assert.That(probe).Contains("specialize(Box.$runtimeType, [Int32Type])");

        await Assert
            .That(probe)
            .Contains("specialize(Box.$runtimeType, [specialize(Box.$runtimeType, [StringType])])");

        await Assert.That(probe).Contains("specialize(Cell.$runtimeType, [Int32Type])");
    }

    [Test]
    public async Task ClosedGenerics_AnOpenStructReceiverAsksTheValue()
    {
        var probe = TranspileHelper.Transpile(ClosedGenericSource)["boxes/probe.ts"];

        await Assert.That(probe).Contains("return getRuntimeType(c);");
    }

    [Test]
    public async Task ClosedGenerics_CastAndAsReadNoDescriptor()
    {
        var probe = TranspileHelper.Transpile(ClosedGenericSource)["boxes/probe.ts"];

        await Assert.That(probe).Contains("castTo<Box<number>>(o, Box)");
        await Assert.That(probe).Contains("o instanceof Box ? o : null");
    }
}
