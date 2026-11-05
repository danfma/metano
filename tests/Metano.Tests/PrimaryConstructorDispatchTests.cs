using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A C# 12 primary-constructor class with secondary constructors lowers to the
/// constructor dispatcher (#432, #435). Every secondary constructor chains to
/// the primary one, so each branch inlines it: the captured parameters'
/// backing fields, the promoted members (declared as fields, not as
/// self-reading getters) and the moved initializers run in every branch.
/// Each golden was checked with <c>tsgo --strict</c> and executed under Node.
/// </summary>
public class PrimaryConstructorDispatchTests
{
    [Test]
    public async Task CapturedParameter_IsAssignedInTheChainedBranch()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Probe;

            [Transpile]
            public class Cap(string name)
            {
                public string Greet() => "hi " + name;

                public Cap() : this("d") { }
            }
            """
        );

        await Assert
            .That(files["probe/cap.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("primary-ctor-dispatch-captured.ts"));
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task PropertyInitializerReadingAParameter_RunsInEveryBranch()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Probe;

            [Transpile]
            public class Init(string name)
            {
                public string Name { get; } = name;

                public Init() : this("d") { }
            }
            """
        );

        await Assert
            .That(files["probe/init.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("primary-ctor-dispatch-initializer.ts"));
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ComputedPropertyAndFieldInitializer_DeclareFieldsAndMoveTheInitializer()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Probe;

            [Transpile]
            public class Deps(string name)
            {
                public string Name => name;

                private readonly int _len = name.Length;

                public int Len => _len;

                public Deps() : this("default") { }
            }
            """
        );

        await Assert
            .That(files["probe/deps.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("primary-ctor-dispatch-computed.ts"));
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ChainForwardingAParameter_AssignsEveryCapturedField()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Probe;

            [Transpile]
            public class Svc(string name, int n)
            {
                public string Label => name + n;

                public Svc(string name) : this(name, 1) { }
            }
            """
        );

        await Assert
            .That(files["probe/svc.ts"])
            .Contains(
                "        const name = __chain0Arg0;\n"
                    + "        const n = __chain0Arg1;\n"
                    + "        this._name = name;\n"
                    + "        this._n = n;\n"
            );
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task PositionalRecordWithASecondaryConstructor_DeclaresItsMembersAsFields()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Probe;

            [Transpile]
            public record Pair(int A, int B)
            {
                public Pair(int both) : this(both, both) { }
            }
            """
        );

        var output = files["probe/pair.ts"];

        await Assert
            .That(output)
            .Contains(
                "  protected static readonly byPairBoth: unique symbol = Symbol(\"Pair(int both)\");\n"
                    + "\n"
                    + "  readonly a: number;\n"
                    + "\n"
                    + "  readonly b: number;\n"
            );
        await Assert
            .That(output)
            .Contains("  constructor(a: number, b: number);\n  constructor(both: number);\n");
        await Assert
            .That(output)
            .Contains(
                "        const a = __chain0Arg0;\n"
                    + "        const b = __chain0Arg1;\n"
                    + "        this.a = a;\n"
                    + "        this.b = b;\n"
            );
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature);
}
