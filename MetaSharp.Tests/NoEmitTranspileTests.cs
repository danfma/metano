namespace MetaSharp.Tests;

/// <summary>
/// Tests for the <c>[NoEmit]</c> attribute. The contract:
/// <list type="bullet">
///   <item>Type with <c>[NoEmit]</c> generates NO .ts file</item>
///   <item>Other transpiled code can reference it (compiles in C#) but its name does
///   NOT appear as an import anywhere</item>
///   <item>When a lambda parameter's type is <c>[NoEmit]</c>, the lambda is emitted
///   without a parameter type annotation, letting TypeScript infer from context</item>
/// </list>
/// </summary>
public class NoEmitTranspileTests
{
    [Test]
    public async Task NoEmitType_DoesNotProduceFile()
    {
        var result = TranspileHelper.Transpile(
            """
            [NoEmit]
            public interface IAmbient
            {
                IAmbient Text(string text);
            }

            [Transpile]
            public class Holder
            {
                public int Value { get; set; }
            }
            """
        );

        await Assert.That(result).DoesNotContainKey("i-ambient.ts");
        await Assert.That(result).ContainsKey("holder.ts");
    }

    [Test]
    public async Task NoEmitType_NotImportedFromConsumer()
    {
        // Even when a transpiled type references an [Import]'d external class whose
        // method takes a callback over a [NoEmit] interface, the .ts output must
        // not try to import the [NoEmit] type from anywhere.
        var result = TranspileHelper.Transpile(
            """
            using System;

            [assembly: TranspileAssembly]

            [Import(name: "External", from: "external-lib")]
            public class External
            {
                public void Subscribe(Action<IExternalContext> handler) => throw new NotSupportedException();
            }

            [NoEmit]
            public interface IExternalContext
            {
                IExternalContext Send(string text);
            }

            public class Wiring
            {
                public void Setup()
                {
                    var ext = new External();
                    ext.Subscribe(c => c.Send("hi"));
                }
            }
            """
        );

        var output = result["wiring.ts"];
        // The External class is imported (it has [Import]); the IExternalContext one is not.
        await Assert.That(output).Contains("import { External } from \"external-lib\"");
        await Assert.That(output).DoesNotContain("IExternalContext");
    }

    [Test]
    public async Task NoEmitLambdaParameter_OmitsTypeAnnotation()
    {
        // The lambda parameter c has C# type IExternalContext (which is [NoEmit]).
        // The generated arrow function should have no `: IExternalContext` annotation —
        // so TypeScript infers the type from the External.subscribe signature in the
        // real .d.ts of "external-lib".
        var result = TranspileHelper.Transpile(
            """
            using System;

            [assembly: TranspileAssembly]

            [Import(name: "External", from: "external-lib")]
            public class External
            {
                public void Subscribe(Action<IExternalContext> handler) => throw new NotSupportedException();
            }

            [NoEmit]
            public interface IExternalContext
            {
                IExternalContext Send(string text);
            }

            public class Wiring
            {
                public void Setup()
                {
                    var ext = new External();
                    ext.Subscribe(c => c.Send("hi"));
                }
            }
            """
        );

        var output = result["wiring.ts"];
        // Parameter `c` is bare (no type annotation), and the inner call is lowered.
        await Assert.That(output).Contains("(c) => c.send(\"hi\")");
        // Negative: no `c: ...` form anywhere.
        await Assert.That(output).DoesNotContain("c: I");
    }
}
