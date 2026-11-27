using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A static call on a generic base binds the base's reified type parameter to the
/// receiver's type argument (<c>CardH.Use(cb)</c> on <c>Component&lt;TSelf&gt;</c>):
/// that argument's descriptor is read at the call, so the reachability phase reaches
/// it (spec 011 FR-025, #834).
/// </summary>
public class ReifiedStaticCallReachabilityTests
{
    private const string Component = """
        using System;

        namespace App;

        [Transpile]
        public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
        {
            public string Title { get; set; } = "";

            public static TSelf Use(Action<TSelf> build)
            {
                var c = new TSelf();
                build(c);
                return c;
            }
        }

        """;

    [Test]
    public async Task StaticCallOnGenericBase_ReachesTheTypeArgumentDescriptor()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            Component
                + """
                [Transpile]
                public sealed class CardH : Component<CardH> { }

                [Transpile]
                public static class Program
                {
                    public static string Run() => CardH.Use(c => { c.Title = "x"; }).Title;
                }
                """
        );

        await Assert.That(files["app/program.ts"]).Contains("}, CardH.$runtimeType).title;");
        await Assert.That(files["app/program.ts"]).DoesNotContain("unsupportedType");
        await Assert.That(files["app/card-h.ts"]).Contains("create: () => new CardH(),");

        await Assert
            .That(files["app/component.ts"])
            .Contains("static get $runtimeType(): ClassType<Component<any>> {");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task StaticCallOnGenericBase_WithAnUndescribableArgument_ReportsMs0001Once()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Component
                + """
                public sealed class Hidden : Component<Hidden> { }

                [Transpile]
                public static class Program
                {
                    public static string Run() => Hidden.Use(c => { c.Title = "x"; }).Title;
                }
                """
        );

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(files["app/program.ts"]).Contains("unsupportedType(\"Hidden\")");

        await Assert
            .That(messages)
            .IsEquivalentTo([
                "Type 'App.Hidden' has no runtime type descriptor and cannot be tested at "
                    + "runtime; passing it to a reified type parameter will throw "
                    + "NotSupportedException if tested.",
            ]);
    }

    [Test]
    public async Task DescriptorType_WritesAnyOnlyForABoundOverTypeParameters()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public interface IShape { }

            public class Holder<T> where T : IShape { }

            public abstract class Node<TSelf> where TSelf : Node<TSelf> { }
            """
        );

        await Assert
            .That(files["lib/holder.ts"])
            .Contains("static get $runtimeType(): ClassType<Holder<unknown>> {");

        await Assert
            .That(files["lib/node.ts"])
            .Contains("static get $runtimeType(): ClassType<Node<any>> {");
    }
}
