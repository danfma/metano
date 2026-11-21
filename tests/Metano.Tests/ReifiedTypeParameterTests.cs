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
}
