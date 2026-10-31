using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Lowering of explicit interface implementations (#278). The targets have no
/// explicit implementations, so a member such as <c>string IPlain.Name</c> is declared
/// under the interface member's name (the name every call site through the interface
/// emits); it is dropped when its interface is <c>[Ignore]</c> (erased, #247); and it
/// raises <c>MS0031</c> when another member already claims that name.
/// </summary>
public class ExplicitInterfaceImplementationTests
{
    private const string Contracts = """
        using Metano.Annotations;

        [Transpile]
        public interface IPlain
        {
            string Name { get; }

            int Calc(int x);

            [Name("shout")]
            string Loud();
        }

        [Transpile]
        public interface IBox<T>
        {
            T Value { get; }

            T Get();
        }

        [Ignore]
        public interface IDotNetOnly
        {
            string Tag { get; }

            void Run();
        }
        """;

    private const string Person = """
        [Transpile]
        public class Person : IPlain, IBox<string>, IDotNetOnly
        {
            string IPlain.Name => "x";

            int IPlain.Calc(int x) => x + 1;

            string IPlain.Loud() => "X";

            string IBox<string>.Value => "v";

            string IBox<string>.Get() => "g";

            string IDotNetOnly.Tag => "t";

            void IDotNetOnly.Run() { }

            public string Describe(IPlain other) =>
                other.Name + ((IPlain)this).Calc(2) + other.Loud();
        }
        """;

    [Test]
    public async Task KeptInterface_DeclaresMembersUnderInterfaceNames()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Contracts + Person);

        await Assert.That(diagnostics).IsEmpty();
        await Assert
            .That(files["person.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("explicit-interface-implementation.ts"));
    }

    [Test]
    public async Task IgnoredInterface_DropsExplicitMembers()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(Contracts + Person);

        var output = files["person.ts"];

        await Assert.That(output).Contains("get name(): string");
        await Assert.That(output).DoesNotContain("get tag()");
        await Assert.That(output).DoesNotContain("run(): void");
        await Assert.That(output).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task ImplicitMemberWithSameName_ReportsConflict()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Contracts
                + """
                [Transpile]
                public class Clash : IPlain
                {
                    public string Name => "implicit";

                    string IPlain.Name => "explicit";

                    public int Calc(int x) => x;

                    public string Loud() => "";
                }
                """
        );

        var conflict = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ExplicitImplementationConflict
        );

        await Assert.That(conflict.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(conflict.Message).Contains("'IPlain.Name'");
        await Assert.That(files["clash.ts"]).DoesNotContain("\"explicit\"");
    }

    [Test]
    public async Task SameMemberNameFromTwoInterfaces_ReportsConflictForEach()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile]
            public interface ILeft { string Name { get; } }

            [Transpile]
            public interface IRight { string Name { get; } }

            [Transpile]
            public class Both : ILeft, IRight
            {
                string ILeft.Name => "left";

                string IRight.Name => "right";
            }
            """
        );

        var conflicts = diagnostics.Count(d =>
            d.Code == DiagnosticCodes.ExplicitImplementationConflict
        );

        await Assert.That(conflicts).IsEqualTo(2);
    }

    [Test]
    public async Task BaseClassMemberWithSameName_ReportsConflict()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Contracts
                + """
                [Transpile]
                public class Named
                {
                    public string Name => "base";
                }

                [Transpile]
                public class Derived : Named, IPlain
                {
                    string IPlain.Name => "explicit";

                    public int Calc(int x) => x;

                    public string Loud() => "";
                }
                """
        );

        var conflict = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ExplicitImplementationConflict
        );

        await Assert.That(conflict.Message).Contains("'Named.Name'");
    }

    [Test]
    public async Task OverloadsOfOneInterface_DoNotConflict()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile]
            public interface IParser
            {
                int Parse(int value);

                int Parse(string value);
            }

            [Transpile]
            public class Parser : IParser
            {
                int IParser.Parse(int value) => value;

                int IParser.Parse(string value) => value.Length;
            }
            """
        );

        var output = files["parser.ts"];

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(output).Contains("parse(...args: unknown[]): number");
        await Assert.That(output).DoesNotContain("iParser");
    }

    [Test]
    public async Task StaticMemberWithSameName_DoesNotConflict()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Contracts
                + """
                [Transpile]
                public class Registry : IPlain
                {
                    public static string Name => "static";

                    string IPlain.Name => "instance";

                    public int Calc(int x) => x;

                    public string Loud() => "";
                }
                """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(files["registry.ts"]).Contains("static get name(): string");
        await Assert.That(files["registry.ts"]).Contains("\"instance\"");
    }

    [Test]
    public async Task IgnoredExplicitMember_SilencesConflict()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Contracts
                + """
                [Transpile]
                public class Quiet : IPlain
                {
                    public string Name => "implicit";

                    [Ignore]
                    string IPlain.Name => "explicit";

                    public int Calc(int x) => x;

                    public string Loud() => "";
                }
                """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(files["quiet.ts"]).Contains("\"implicit\"");
    }

    [Test]
    public async Task BclBridgeNextToImplicitMember_IsDroppedSilently()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections;
            using System.Collections.Generic;
            using Metano.Annotations;

            [Transpile]
            public class Bag : IEnumerable<int>
            {
                public IEnumerator<int> GetEnumerator() => new List<int>().GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """
        );

        var output = files["bag.ts"];

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(output.Split("\n  getEnumerator(").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task NameOverrideCollidingCaseInsensitively_ReportsConflict()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Contracts
                + """
                [Transpile]
                public class Renamed : IPlain
                {
                    [Name("name")]
                    public string Label => "label";

                    string IPlain.Name => "explicit";

                    public int Calc(int x) => x;

                    public string Loud() => "";
                }
                """
        );

        var conflict = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ExplicitImplementationConflict
        );

        await Assert.That(conflict.Message).Contains("'Renamed.Label'");
    }

    [Test]
    public async Task ReimplementationInDerivedClass_DoesNotConflict()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile]
            public interface ITitled { string Title { get; } }

            [Transpile]
            public class Page : ITitled
            {
                string ITitled.Title => "page";
            }

            [Transpile]
            public class Cover : Page, ITitled
            {
                string ITitled.Title => "cover";
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(files["cover.ts"]).Contains("get title(): string");
    }

    [Test]
    public async Task ExplicitEvent_IsDeclaredUnderInterfaceName()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using Metano.Annotations;

            [Transpile]
            public interface INotifier { event Action Changed; }

            [Transpile]
            public class Notifier : INotifier
            {
                event Action INotifier.Changed
                {
                    add { }
                    remove { }
                }
            }
            """
        );

        var output = files["notifier.ts"];

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(output).Contains("changed");
        await Assert.That(output).DoesNotContain("iNotifier");
    }

    [Test]
    public async Task ExplicitOperator_IsNotDeclared()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile]
            public interface IAddable<T>
                where T : IAddable<T>
            {
                static abstract T operator +(T left, T right);
            }

            [Transpile]
            public class Money : IAddable<Money>
            {
                public int Cents { get; init; }

                static Money IAddable<Money>.operator +(Money left, Money right) =>
                    new() { Cents = left.Cents + right.Cents };
            }
            """
        );

        var output = files["money.ts"];

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.ExplicitImplementationConflict))
            .IsFalse();
        await Assert.That(output).Contains("readonly cents: number");
        await Assert.That(output).DoesNotContain("op_Addition");
        await Assert.That(output).DoesNotContain("iAddable");
    }

    [Test]
    public async Task SourceGeneratedExplicitImplementation_IsNotPoliced()
    {
        // The System.Text.Json generator adds `IJsonTypeInfoResolver.GetTypeInfo`
        // next to the context's `GetTypeInfo(Type)` override: a conflict, but not
        // user-authored code.
        var (_, diagnostics) = TranspileHelper.TranspileWithJsonSourceGenerator(
            """
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]

            namespace Studio;

            public sealed class Document
            {
                public string Title { get; set; } = "";
            }

            [JsonSerializable(typeof(Document))]
            public partial class StudioJsonContext : JsonSerializerContext;
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.ExplicitImplementationConflict))
            .IsFalse();
    }

    [Test]
    public async Task Dart_DeclaresKeptMembersAndDropsIgnoredOnes()
    {
        var (files, _) = TranspileHelper.TranspileDart(Contracts + Person);

        var output = files["person.dart"];

        await Assert.That(output).Contains("String get name => 'x';");
        await Assert.That(output).Contains("int calc(int x) => x + 1;");
        await Assert.That(output).Contains("String get value => 'v';");
        await Assert.That(output).DoesNotContain("String get tag");
        await Assert.That(output).DoesNotContain("void run(");
    }
}
