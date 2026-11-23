using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

public class ReifiedTypeParameterTests
{
    [Test]
    public async Task ReifiedTypeParameters_Golden()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace App;

            [Transpile]
            public class Circle { }

            [Transpile]
            public struct Point
            {
                public int X { get; set; }
            }

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static bool IsA<T>(object o) => o is T;

                public static T? AsA<T>(object o) where T : class => o as T;

                public static T CastA<T>(object o) => (T)o;

                public static Type TypeOf<T>() => typeof(T);

                public static T First<T>(IEnumerable<T> xs) => xs.First();

                public static T Make<T>() where T : new() => new T();

                public static T? Zero<T>() => default;

                public static T? ZeroOf<T>(object o) => o is T ? (T)o : default;

                public static Type RuntimeTypeOf<T>(T value) => value!.GetType();

                public static List<T> MakeList<T>() => new List<T>();

                public static void Run(object o)
                {
                    IsA<int>(o);
                    IsA<List<string>>(o);
                    AsA<Circle>(o);
                    CastA<Circle>(o);
                    TypeOf<Circle>();
                    Make<Circle>();
                    Zero<int>();
                    ZeroOf<Point>(o);
                    MakeList<Circle>();
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("reified-type-parameters.ts", files["app/probes.ts"]);
    }

    [Test]
    public async Task ReifiedOverrides_Golden()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public interface IChecker
            {
                bool Check<T>(object value);
            }

            [Transpile]
            public class BaseChecker : IChecker
            {
                public virtual bool Check<T>(object value) => false;

                public virtual string Check(string value) => value;
            }

            [Transpile]
            public class ReifiedChecker : BaseChecker
            {
                public override bool Check<T>(object value) => value is T;
            }
            """
        );

        var combined = string.Join(
            "\n// ---\n",
            files["app/i-checker.ts"],
            files["app/base-checker.ts"],
            files["app/reified-checker.ts"]
        );

        await TranspileHelper.AssertGolden("reified-overrides.ts", combined);
    }

    [Test]
    public async Task ReifiedDelegates_Golden()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public class Circle { }

            [Transpile]
            [ExportedAsModule]
            public static class DelegateProbes
            {
                public static bool IsA<T>(object o) => o is T;

                public static bool Run(object o)
                {
                    Func<object, bool> f = IsA<Circle>;
                    return f(o);
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("reified-delegates.ts", files["app/delegate-probes.ts"]);
    }

    [Test]
    public async Task ReifiedParamsOptional_Golden()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Circle { }

            [Transpile]
            [ExportedAsModule]
            public static class OptionalProbes
            {
                public static bool TryMatch<T>(object o, int n = 1, params string[] rest) => o is T;

                public static bool Run(object o)
                {
                    var a = TryMatch<Circle>(o);
                    var b = TryMatch<Circle>(o, 2, "x", "y");
                    return a && b;
                }
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "reified-params-optional.ts",
            files["app/optional-probes.ts"]
        );
    }

    [Test]
    public async Task TransitiveCall_PropagatesReifiedParameter()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [ExportedAsModule]
            public static class Chain
            {
                public static bool Inner<T>(object o) => o is T;

                public static bool Outer<U>(object o) => Inner<U>(o);
            }
            """
        );

        await Assert.That(files["app/chain.ts"]).Contains("U$type: RuntimeType<U>");
        await Assert.That(files["app/chain.ts"]).Contains("inner<U>(o, U$type)");
    }

    [Test]
    public async Task ExportedAsModule_WithReifiedParam_EmitsMs0050Once()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            [ExportedAsModule]
            public static class Api
            {
                public static bool Check<T>(object o) => o is T && o is T;
            }
            """
        );

        var ms0050 = diagnostics
            .Where(d => d.Code == DiagnosticCodes.ReifiedParameterOnInteropSurface)
            .ToList();

        await Assert.That(ms0050.Count).IsEqualTo(1);
        await Assert.That(ms0050[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(ms0050[0].Message).Contains("UnknownType");
    }

    [Test]
    public async Task EmitPackagePublicMember_WithReifiedParam_EmitsMs0054()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [assembly: EmitPackage("my-lib")]

            namespace Lib;

            [Transpile]
            public class Bag
            {
                public bool Check<T>(object o) => o is T;
                internal bool InternalCheck<T>(object o) => o is T;
            }
            """
        );

        var ms0054 = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnreifiableTypeParameter)
            .ToList();

        await Assert.That(ms0054.Count).IsEqualTo(1);
        await Assert.That(ms0054[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task UnreifiableSites_EmitMs0054()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public static class Unreifiable
            {
                [ModuleEntryPoint]
                public static void Main<T>()
                {
                    var x = typeof(T);
                }

                [Emit("($0)")]
                public static bool ExternCheck<T>(object o) => o is T;
            }
            """
        );

        var ms0054 = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnreifiableTypeParameter)
            .ToList();

        await Assert.That(ms0054.Count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task NewConstraintCallSite_WithPlainObjectArg_EmitsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            [PlainObject]
            public class Dto
            {
                public int Id { get; set; }
            }

            [Transpile]
            [ExportedAsModule]
            public static class Factory
            {
                public static T Make<T>() where T : new() => new T();

                public static Dto Run() => Make<Dto>();
            }
            """
        );

        var ms0001 = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Message.Contains("no 'create' factory")
            )
            .ToList();

        await Assert.That(ms0001.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReifiedClass_Golden()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public class Box<T>
            {
                private readonly Type _initType = typeof(T);
                public Type CtorType { get; }

                public Box()
                {
                    CtorType = typeof(T);
                }

                public bool Matches(object o) => o is T;

                public Type InitType => _initType;
            }

            [Transpile]
            [ExportedAsModule]
            public static class Runner
            {
                public static bool Check(object o)
                {
                    var box = new Box<int>();
                    return box.Matches(o) && box.CtorType == box.InitType;
                }
            }
            """
        );

        var combined = string.Join("\n// ---\n", files["app/box.ts"], files["app/runner.ts"]);
        await TranspileHelper.AssertGolden("reified-class.ts", combined);
    }

    [Test]
    public async Task ReifiedDerivedClass_Golden()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public class Box<T>
            {
                public bool Matches(object o) => o is T;
            }

            [Transpile]
            public class IntBox : Box<int>
            {
            }

            [Transpile]
            public class Pair<U, V> : Box<List<U>>
            {
                public bool MatchesSecond(object o) => o is V;
            }

            [Transpile]
            [ExportedAsModule]
            public static class Runner
            {
                public static bool Check(object a, object b)
                {
                    var intBox = new IntBox();
                    var pair = new Pair<string, int>();
                    return intBox.Matches(a) && pair.Matches(b) && pair.MatchesSecond(a);
                }
            }
            """
        );

        var combined = string.Join(
            "\n// ---\n",
            files["app/box.ts"],
            files["app/int-box.ts"],
            files["app/pair.ts"],
            files["app/runner.ts"]
        );

        await TranspileHelper.AssertGolden("reified-derived-class.ts", combined);
    }

    [Test]
    public async Task ReifiedStatics_Golden()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public class Box<T>
            {
                public static bool Is(object o) => o is T;

                public static bool IsBoth<U>(object a, object b) => a is T && b is U;

                public static string TypeName() => typeof(T).Name;
            }

            [Transpile]
            [ExportedAsModule]
            public static class Runner
            {
                public static string Check(object a, object b)
                {
                    return Box<int>.Is(a)
                        + ":"
                        + Box<int>.IsBoth<string>(a, b)
                        + ":"
                        + Box<string>.TypeName();
                }
            }
            """
        );

        var combined = string.Join("\n// ---\n", files["app/box.ts"], files["app/runner.ts"]);
        await TranspileHelper.AssertGolden("reified-statics.ts", combined);
    }

    [Test]
    public async Task OpaqueType_InCollectionSlot_ComparesByIdentityAndNeverInstanceTests()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using Metano.Annotations;

            namespace App;

            public class NonTranspiledA { }

            public class NonTranspiledB { }

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static bool Check(object o)
                {
                    object list = new List<NonTranspiledA>();
                    return list is List<NonTranspiledA> && !(o is List<NonTranspiledB>);
                }
            }
            """
        );

        var ts = files["app/probes.ts"];
        await Assert.That(ts).Contains("opaqueType(\"App.NonTranspiledA\")");
        await Assert.That(ts).Contains("opaqueType(\"App.NonTranspiledB\")");
        await Assert.That(ts).DoesNotContain("UnknownType");
    }

    [Test]
    public async Task EnumDescriptor_EmitsDefaultFactory_Numeric64BitAndStringEnum()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App;

            [Transpile] public enum Color { Red = 0, Green = 1 }
            [Transpile] public enum NonZero { A = 1, B = 2 }
            [Transpile] public enum BigEnum : long { A = 1 }
            [Transpile, StringEnum] public enum Status { Active, Inactive }

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static T? Zero<T>() { _ = typeof(T); return default; }
                public static object? Run() =>
                    (Zero<Color>(), Zero<NonZero>(), Zero<BigEnum>(), Zero<Status>());
            }
            """
        );

        await Assert.That(files["app/color.ts"]).Contains("default: () => 0,");
        await Assert.That(files["app/non-zero.ts"]).Contains("default: () => 0 as NonZero,");
        await Assert.That(files["app/big-enum.ts"]).Contains("default: () => 0n as BigEnum,");
        await Assert.That(files["app/status.ts"]).Contains("default: () => \"Active\",");
    }

    [Test]
    public async Task StructZeroFactory_RecordStructAndExplicitCtor_NeverCallsUserCtor()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App;

            [Transpile]
            public readonly record struct PairStruct(int X, string Y);

            [Transpile]
            public struct CtorStruct
            {
                public int X;
                public CtorStruct() { X = 5; }
            }

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static T? Zero<T>() where T : new() { _ = typeof(T); return new T(); }
                public static object? Run() => (Zero<PairStruct>(), Zero<CtorStruct>());
            }
            """
        );

        var pairTs = files["app/pair-struct.ts"];
        await Assert.That(pairTs).Contains("static $default(): PairStruct");

        await Assert
            .That(pairTs)
            .Contains("Object.assign(Object.create(PairStruct.prototype), { x: 0, y: null })");

        await Assert.That(pairTs).Contains("default: () => PairStruct.$default(),");
        await Assert.That(pairTs).Contains("create: () => PairStruct.$default(),");

        var ctorTs = files["app/ctor-struct.ts"];
        await Assert.That(ctorTs).Contains("static $default(): CtorStruct");

        await Assert
            .That(ctorTs)
            .Contains("Object.assign(Object.create(CtorStruct.prototype), { x: 0 })");
    }

    [Test]
    public async Task ParamsCtorWithSlot_PlacesTypeArgsBeforeRest()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App;

            [Transpile]
            public class ParamsBox<T>
            {
                public ParamsBox(string label, params int[] items) { }
                public bool Is(object o) => o is T;
            }

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static ParamsBox<int> Make() => new ParamsBox<int>("L", 10, 20);
            }
            """
        );

        await Assert
            .That(files["app/probes.ts"])
            .Contains("new ParamsBox(\"L\", [Int32Type], 10, 20)");
    }

    [Test]
    public async Task UndescribableReifiedArguments_ReportMs0001AndEmitUnsupportedType()
    {
        var result = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using System.Collections.Generic;
            using Metano.Annotations;

            namespace App;

            public class Opaque { }

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                private static bool IsA<T>(object o) => o is T;
                public static bool Run(Func<string> g) =>
                    IsA<(int, string)>("x")
                    && IsA<int[]>(new string[] { "a" })
                    && IsA<Func<int>>(g)
                    && IsA<IDisposable>("s")
                    && IsA<Opaque>("s")
                    && IsA<DayOfWeek>(1);
            }
            """
        );

        var ms0001Count = result.Diagnostics.Count(d => d.Code == "MS0001");
        await Assert.That(ms0001Count).IsGreaterThanOrEqualTo(6);
        var ts = result.Files["app/probes.ts"];
        await Assert.That(ts).Contains("unsupportedType(");
        await Assert.That(ts).DoesNotContain("UnknownType");
        await Assert.That(ts).DoesNotContain("ArrayType");
        await Assert.That(ts).DoesNotContain("DelegateType");
    }

    [Test]
    public async Task OverloadedGenericClassWithSlot_CallsParameterlessSelectorAndDefinesSlot()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App;

            [Transpile]
            public class OverBox<T>
            {
                public string Which;
                public OverBox() { Which = "empty"; }
                public OverBox(int x) { Which = "int"; }
                public bool Is(object o) => o is T;
            }

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static T Make<T>() where T : new() => new T();
                public static object Run() => (Make<OverBox<string>>(), new OverBox<int>(5));
            }
            """
        );

        var boxTs = files["app/over-box.ts"];

        await Assert
            .That(boxTs)
            .Contains(
                "create: (typeArguments) => new OverBox(OverBox.byOverBoxDefault, typeArguments as"
            );

        await Assert.That(boxTs).Contains("Object.defineProperty(this, \"$typeArguments$OverBox\"");
    }

    [Test]
    public async Task DecimalAndTemporalReifiedDefaults_ImportSeparateDefaultModules()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;

            namespace App;

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static T? Zero<T>() { _ = typeof(T); return default; }
                public static object? Run() => (Zero<decimal>(), Zero<DateOnly>());
            }
            """
        );

        var ts = files["app/probes.ts"];
        await Assert.That(ts).Contains("DecimalDefaultType");
        await Assert.That(ts).Contains("DateOnlyDefaultType");
    }

    [Test]
    public async Task SupportsReifiedTypeParameters_False_DisablesAnalysis()
    {
        var source = """
            using Metano.Annotations;

            namespace App;

            [Transpile]
            [ExportedAsModule]
            public static class Probes
            {
                public static bool IsA<T>(object o) => o is T;
            }
            """;

        var compilation = TranspileHelper.CompileLibrary(source);
        var frontend = new Metano.Compiler.CSharpSourceFrontend();

        var ir = frontend.ExtractFromCompilation(
            compilation,
            Metano.Annotations.TargetLanguage.TypeScript,
            supportsReifiedTypeParameters: false
        );

        await Assert.That(ir.ReifiedTypeAnalysis.IsEnabled).IsFalse();
    }
}
