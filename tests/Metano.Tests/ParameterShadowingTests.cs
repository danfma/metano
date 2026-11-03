namespace Metano.Tests;

/// <summary>
/// A call to a containerless static method lowers to the bare function name,
/// so a parameter of the same camelCase name would shadow it (<c>log(log)</c>
/// calls the parameter, #323). Such a parameter takes a <c>$</c> suffix where
/// it is declared and where it is read; no other parameter changes.
/// </summary>
public class ParameterShadowingTests
{
    private const string Module = """
        [Transpile, NoContainer]
        public static class Mod
        {
            public static int Log(int value) => value + 1;
        """;

    [Test]
    public async Task ModuleFunction_ParameterNamedLikeACalledFunction_IsRenamed()
    {
        var files = Transpile(
            $$"""
            {{Module}}
                public static int Run(int log) => Log(log);
                public static int Twice(int log) { var x = Log(log); return Log(x) + log; }
            }
            """
        );

        await Assert.That(files["mod.ts"]).Contains("export function run(log$: number): number {");
        await Assert.That(files["mod.ts"]).Contains("return log(log$);");
        await Assert.That(files["mod.ts"]).Contains("const x = log(log$);");
        await Assert.That(files["mod.ts"]).Contains("return log(x) + log$;");
    }

    [Test]
    public async Task ClassMethod_CallingTheModuleFunction_RenamesTheShadowingParameter()
    {
        var files = Transpile(
            $$"""
            {{Module}}
            }

            [Transpile]
            public class User
            {
                public int Run(int log) => Mod.Log(log);
                public int Fine(int other) => Mod.Log(other);
            }
            """
        );

        await Assert.That(files["user.ts"]).Contains("run(log$: number): number {");
        await Assert.That(files["user.ts"]).Contains("return log(log$);");
        await Assert.That(files["user.ts"]).Contains("return log(other);");
    }

    [Test]
    public async Task Lambdas_CapturingOrDeclaringTheShadowingParameter_UseTheRenamedName()
    {
        var files = Transpile(
            $$"""
            {{Module}}
            }

            [Transpile]
            public class User
            {
                public Func<int, int> Declares() => log => Mod.Log(log);
                public Func<int, int> Captures(int log) => x => Mod.Log(x + log);
            }
            """
        );

        await Assert.That(files["user.ts"]).Contains("return (log$: number) => log(log$);");
        await Assert.That(files["user.ts"]).Contains("captures(log$: number)");
        await Assert.That(files["user.ts"]).Contains("return (x: number) => log(x + log$);");
    }

    [Test]
    public async Task GenericCallee_ShadowedByAParameter_IsRenamed()
    {
        var files = Transpile(
            """
            [Transpile, NoContainer]
            public static class Mod
            {
                public static T Id<T>(T value) => value;
                public static int Run(int id) => Id<int>(id);
            }
            """
        );

        await Assert.That(files["mod.ts"]).Contains("run(id$: number)");
        await Assert.That(files["mod.ts"]).Contains("return id(id$);");
    }

    [Test]
    public async Task RecordAndPrimaryConstructorParameters_KeepTheirNames()
    {
        var files = Transpile(
            $$"""
            {{Module}}
            }

            [Transpile]
            public record Rec(int Log)
            {
                public int Twice() => Mod.Log(Log) * 2;
            }

            [Transpile]
            public class Cls(int log)
            {
                public int Run() => Mod.Log(log);
            }
            """
        );

        await Assert.That(files["rec.ts"]).Contains("readonly log: number");
        await Assert.That(files["rec.ts"]).DoesNotContain("log$");
        await Assert.That(files["cls.ts"]).DoesNotContain("log$");
    }

    [Test]
    public async Task AccessorValueParameter_KeepsItsName()
    {
        var files = Transpile(
            $$"""
            [Transpile, NoContainer]
            public static class Mod
            {
                public static int Value(int v) => v;
            }

            [Transpile]
            public class Holder
            {
                private int _x;
                public int X { set => _x = Mod.Value(value); }
            }
            """
        );

        await Assert.That(files["holder.ts"]).DoesNotContain("value$");
    }

    [Test]
    public async Task InstanceMethod_ParameterNamedLikeASiblingMethod_IsNotRenamed()
    {
        var files = Transpile(
            """
            [Transpile]
            public class Inst
            {
                public int Log(int v) => v;
                public int Run(int log) => Log(log) + log;
            }
            """
        );

        await Assert.That(files["inst.ts"]).Contains("run(log: number): number {");
        await Assert.That(files["inst.ts"]).Contains("return this.log(log) + log;");
    }

    [Test]
    public async Task NameOverride_AvoidingTheCollision_KeepsTheParameter()
    {
        var files = Transpile(
            """
            [Transpile, NoContainer]
            public static class Mod
            {
                [Name("logMessage")]
                public static int Log(int value) => value + 1;

                public static int Run(int log) => Log(log);
            }
            """
        );

        await Assert.That(files["mod.ts"]).Contains("export function run(log: number): number {");
        await Assert.That(files["mod.ts"]).Contains("return logMessage(log);");
    }

    private static Dictionary<string, string> Transpile(string source) =>
        TranspileHelper.Transpile(
            $$"""
            using System;
            using Metano.Annotations;

            {{source}}
            """
        );
}
