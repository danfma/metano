using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// One test per syntactic position a transpilable member can reference an
/// <c>[Ignore]</c> (.NET-only) type from (#277): every type name in a body or a
/// member initializer raises <c>MS0013</c>, while positions that never reach the
/// output (<c>nameof</c>, attribute applications, <c>[Ignore]</c> members) stay
/// silent. The signature positions are covered by <see cref="IgnoreDotNetOnlyTests"/>.
/// </summary>
public class IgnoreReferencePositionTests
{
    private const string IgnoredDeclarations = """
        [Ignore]
        public class Marker
        {
            public int Value => 1;
        }

        [Ignore]
        public class MarkerException : Exception {}

        [Ignore]
        public static class MarkerConstants
        {
            public const int Default = 1;
        }

        [AttributeUsage(AttributeTargets.All)]
        [Ignore]
        public sealed class UsesTypeAttribute(Type type) : Attribute
        {
            public Type Type { get; } = type;
        }
        """;

    [Test]
    [Arguments("DeclarationPattern", "public bool Run(object o) => o is Marker m;")]
    [Arguments("RecursivePattern", "public bool Run(object o) => o is Marker { };")]
    [Arguments(
        "TypePatternInSwitchArm",
        "public int Run(object o) => o switch { Marker => 1, _ => 0 };"
    )]
    [Arguments(
        "DeclarationPatternInSwitchArm",
        "public int Run(object o) => o switch { Marker m => 1, _ => 0 };"
    )]
    [Arguments(
        "CaseLabelPattern",
        "public int Run(object o) { switch (o) { case Marker m: return 1; default: return 0; } }"
    )]
    [Arguments(
        "PropertyPatternSubpattern",
        "public bool Run(Exception e) => e is { InnerException: MarkerException };"
    )]
    [Arguments(
        "CatchDeclaration",
        "public int Run() { try { return 1; } catch (MarkerException e) { return 0; } }"
    )]
    [Arguments(
        "LocalFunctionParameter",
        "public int Run() { int Local(Marker m) => 0; return 0; }"
    )]
    [Arguments("LocalFunctionReturn", "public int Run() { Marker? Local() => null; return 0; }")]
    [Arguments(
        "LocalFunctionConstraint",
        "public int Run() { int Local<T>() where T : Marker => 0; return 0; }"
    )]
    [Arguments("LambdaParameter", "public Delegate Run() => (Marker m) => 0;")]
    [Arguments(
        "ForEachVariable",
        "public int Run(object[] items) { foreach (Marker m in items) {} return 0; }"
    )]
    [Arguments("DefaultExpression", "public object? Run() => default(Marker);")]
    [Arguments("TypeOfInBody", "public Type Run() => typeof(Marker);")]
    [Arguments("TypeOfInFieldInitializer", "private readonly Type _type = typeof(Marker);")]
    [Arguments("TypeOfInPropertyInitializer", "public Type Kind { get; } = typeof(Marker);")]
    [Arguments(
        "ConstantInFieldInitializer",
        "private readonly int _value = MarkerConstants.Default;"
    )]
    [Arguments(
        "DefaultParameterValue",
        "public int Run(int value = MarkerConstants.Default) => value;"
    )]
    [Arguments(
        "ConstructorInitializerArgument",
        "public Consumer() : this(typeof(Marker)) {} public Consumer(Type type) {}"
    )]
    [Arguments(
        "OperatorBody",
        "public static Consumer operator +(Consumer a, Consumer b) => typeof(Marker) is null ? a : b;"
    )]
    [Arguments(
        "UserMethodNamedNameOf",
        "private static int nameof(Type type) => 0; public int Run() => nameof(typeof(Marker));"
    )]
    [Arguments(
        "ConversionSignature",
        "public static explicit operator Marker(Consumer consumer) => throw null!;"
    )]
    [Arguments("EventType", "public event Action<Marker>? Changed;")]
    public async Task IgnoreType_InPosition_RaisesMs0013(string position, string member)
    {
        var diagnostics = Ms0013For(ConsumerWith(member));

        await Assert.That(diagnostics).Count().IsEqualTo(1).Because(position);
    }

    [Test]
    public async Task IgnoreType_InRecordPrimaryConstructorBaseArgument_RaisesMs0013()
    {
        var diagnostics = Ms0013For(
            $$"""
            [assembly: TranspileAssembly]
            {{IgnoredDeclarations}}

            public record Base(Type Kind);

            public record Derived(int Count) : Base(typeof(Marker));
            """
        );

        await Assert.That(diagnostics).Count().IsEqualTo(1);
    }

    [Test]
    public async Task IgnoreType_InDelegateSignature_RaisesMs0013()
    {
        var diagnostics = Ms0013For(
            $$"""
            [assembly: TranspileAssembly]
            {{IgnoredDeclarations}}

            public delegate Marker Handler(int count);
            """
        );

        await Assert.That(diagnostics).Count().IsEqualTo(1);
    }

    [Test]
    public async Task IgnoreMember_ReadWithoutQualifier_RaisesMs0013()
    {
        var diagnostics = Ms0013For(
            ConsumerWith(
                """
                [Ignore]
                public int Hidden => 1;

                public int Run() => Hidden;
                """
            )
        );

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Message).Contains("Consumer.Hidden");
    }

    [Test]
    [Arguments("ExpressionBodiedProperty", "public Marker Current => throw null!;")]
    [Arguments("AutoProperty", "public Marker? Current { get; set; }")]
    [Arguments("AccessorBodiedProperty", "public Marker? Current { get { return null; } set { } }")]
    public async Task IgnoreType_AsPropertyType_RaisesMs0013ExactlyOnce(string shape, string member)
    {
        var diagnostics = Ms0013For(ConsumerWith(member));

        await Assert.That(diagnostics).Count().IsEqualTo(1).Because(shape);
    }

    [Test]
    [Arguments("NameOfType", "public string Run() => nameof(Marker);")]
    [Arguments("NameOfMember", "public string Run() => nameof(Marker.Value);")]
    [Arguments(
        "NameOfIgnoredMember",
        "[Ignore] public int Hidden => 1; public string Run() => nameof(Hidden);"
    )]
    [Arguments(
        "AttributeOnFieldWithInitializer",
        "[UsesType(typeof(Marker))] private readonly int _value = 1;"
    )]
    [Arguments(
        "AttributeOnLocalFunction",
        "public int Run() { [UsesType(typeof(Marker))] int Local() => 0; return Local(); }"
    )]
    [Arguments(
        "AttributeOnLambda",
        "public Func<int> Run() => [UsesType(typeof(Marker))] () => 0;"
    )]
    [Arguments("IgnoredPropertyOfIgnoredType", "[Ignore] public Marker Current => new Marker();")]
    [Arguments("IgnoredFieldInitializer", "[Ignore] private readonly Type _type = typeof(Marker);")]
    public async Task IgnoreType_InNonEmittedPosition_DoesNotRaiseMs0013(
        string position,
        string member
    )
    {
        var diagnostics = Ms0013For(ConsumerWith(member));

        await Assert.That(diagnostics).IsEmpty().Because(position);
    }

    [Test]
    public async Task IgnoreMember_OfRecordWithPrimaryConstructor_DoesNotRaiseMs0013()
    {
        var diagnostics = Ms0013For(
            $$"""
            [assembly: TranspileAssembly]
            {{IgnoredDeclarations}}

            public record Consumer(int Count)
            {
                [Ignore]
                public Marker Create() => new Marker();

                public int Twice() => Count * 2;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    private static string ConsumerWith(string member) =>
        $$"""
            [assembly: TranspileAssembly]
            {{IgnoredDeclarations}}

            public class Consumer
            {
                {{member}}
            }
            """;

    private static List<MetanoDiagnostic> Ms0013For(string source)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        return diagnostics
            .Where(diagnostic =>
                diagnostic.Code == DiagnosticCodes.IgnoreReferencedByTranspiledCode
            )
            .ToList();
    }
}
