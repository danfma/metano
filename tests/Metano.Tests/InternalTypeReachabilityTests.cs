using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #770 (decision W9-9, baseline FR-101): under <c>[assembly: TranspileAssembly]</c> an
/// internal type that emitted code reaches is transpiled into its own module and imported
/// where used, but no barrel re-exports it; a type that still cannot be emitted reports
/// <c>MS0002</c> at the reference site.
/// </summary>
public class InternalTypeReachabilityTests
{
    private const string Signals = """
        using System.Collections.Generic;

        [assembly: TranspileAssembly]

        namespace Reactive;

        public abstract class SignalNode { }

        internal static class FlushLoop
        {
            private static readonly List<SignalNode> Queue = [];

            internal static void Enqueue(SignalNode node)
            {
                Queue.Add(node);
                FlushStats.Count++;
            }
        }

        internal static class FlushStats
        {
            internal static int Count;
        }

        internal static class NeverUsed
        {
            internal static int Value;
        }

        public sealed class Signal<T> : SignalNode
        {
            private T _value = default!;

            public T Value
            {
                get => _value;
                set
                {
                    _value = value;
                    FlushLoop.Enqueue(this);
                }
            }
        }
        """;

    [Test]
    public async Task InternalTypeReachedFromASetter_IsEmittedAndImported()
    {
        var files = TranspileHelper.Transpile(Signals);

        await Assert
            .That(files["reactive/signal.ts"])
            .Contains("import { FlushLoop } from \"./flush-loop.js\";");

        await Assert
            .That(files["reactive/flush-loop.ts"])
            .Contains("import { FlushStats } from \"./flush-stats.js\";");

        await Assert.That(files).ContainsKey("reactive/flush-stats.ts");
        await Assert.That(files).DoesNotContainKey("reactive/never-used.ts");
    }

    [Test]
    public async Task ReachedInternalTypes_StayOutOfTheBarrel()
    {
        var files = TranspileHelper.Transpile(Signals);

        await Assert
            .That(files["reactive/index.ts"])
            .IsEqualTo(
                """
                export { SignalNode } from "./signal-node.js";
                export { Signal } from "./signal.js";

                """
            );
    }

    [Test]
    public async Task ReachedInternalTypes_TypeCheckStrictly()
    {
        var files = TranspileHelper.Transpile(Signals);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task EnumsNestedTypesAndExtensionClasses_AreReached()
    {
        var files = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            namespace App;

            internal enum Phase { Idle, Busy }

            internal class Holder
            {
                internal class Inner
                {
                    public int X = 1;
                }
            }

            internal static class IntExtensions
            {
                public static int Twice(this int value) => value * 2;
            }

            public sealed class Worker
            {
                private Phase _phase = Phase.Idle;

                public int Run() => new Holder.Inner().X + 2.Twice() + (int)_phase;
            }
            """
        );

        var worker = files["app/worker.ts"];

        await Assert.That(worker).Contains("import { Phase } from \"./phase.js\";");
        await Assert.That(worker).Contains("import { Holder } from \"./holder.js\";");
        await Assert.That(worker).Contains("import { twice } from \"./int-extensions.js\";");

        await Assert
            .That(files["app/index.ts"])
            .IsEqualTo("export { Worker } from \"./worker.js\";\n");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task IgnoredInternalType_KeepsMS0013AndIsNotEmitted()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            namespace App;

            [Ignore]
            internal static class DotNetOnly
            {
                internal static int Value = 1;
            }

            public sealed class Reader
            {
                public int Read() => DotNetOnly.Value;
            }
            """
        );

        await Assert.That(files).DoesNotContainKey("app/dot-net-only.ts");

        await Assert
            .That(diagnostics.Select(diagnostic => diagnostic.Code))
            .Contains(DiagnosticCodes.IgnoreReferencedByTranspiledCode);
    }

    [Test]
    public async Task FileLocalType_ReportsMS0002AtTheReference()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            namespace App;

            file static class Hidden
            {
                internal static int Value = 3;
            }

            public sealed class Reader
            {
                public int Read() => Hidden.Value;
            }
            """
        );

        var unresolved = diagnostics.Single(diagnostic =>
            diagnostic.Code == DiagnosticCodes.UnresolvedType
        );

        await Assert.That(unresolved.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(unresolved.Message).Contains("'Hidden'");
    }

    [Test]
    public async Task ExplicitlyTranspiledInternalType_KeepsItsBarrelEntry()
    {
        var files = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            namespace App;

            [Transpile]
            internal sealed class Pinned
            {
                public int X => 1;
            }
            """
        );

        await Assert
            .That(files["app/index.ts"])
            .Contains("export { Pinned } from \"./pinned.js\";");
    }

    [Test]
    public async Task ReachedInternalType_NeverLeaksIntoAConsumer()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib;

            internal static class Codec
            {
                internal static string Encode(int value) => "#" + value;
            }

            public sealed class Store
            {
                public string Put(int value) => Codec.Encode(value);
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public sealed class Client
            {
                public string Save() => new MyLib.Store().Put(1);
            }
            """;

        var libraryFiles = TranspileHelper.Transpile(library);
        var consumerFiles = TranspileHelper.TranspileWithLibrary(library, consumer);

        await Assert.That(libraryFiles).ContainsKey("my-lib/codec.ts");
        await Assert.That(libraryFiles["my-lib/index.ts"]).DoesNotContain("Codec");
        await Assert.That(consumerFiles["app/client.ts"]).DoesNotContain("Codec");

        await Assert
            .That(consumerFiles["app/client.ts"])
            .Contains("import { Store } from \"@scope/lib/my-lib\";");
    }

    [Test]
    public async Task FriendConsumerReferencingAPackageInternal_ReportsMS0002()
    {
        // The producer emits Codec but exports it nowhere, so a consumer granted
        // [InternalsVisibleTo] cannot import it: the reference is reported, never left bare.
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestConsumer")]

            namespace MyLib;

            internal static class Codec
            {
                internal static string Encode(int value) => "#" + value;
            }

            public sealed class Store
            {
                public string Put(int value) => Codec.Encode(value);
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public sealed class Client
            {
                public string Save() => MyLib.Codec.Encode(1);
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            library,
            consumer
        );

        var unresolved = diagnostics.Single(diagnostic =>
            diagnostic.Code == DiagnosticCodes.UnresolvedType
        );

        await Assert.That(unresolved.Message).Contains("'Codec'");
    }
}
