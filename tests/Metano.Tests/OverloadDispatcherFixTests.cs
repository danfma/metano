namespace Metano.Tests;

/// <summary>
/// Overload dispatcher fixes of the 3.0.x backlog (wave 6, group I): what a
/// dispatcher binds, declares and names.
/// </summary>
public class OverloadDispatcherFixTests
{
    [Test]
    public async Task ConstructorBranch_BindsOnlyTheParametersItReads()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Tagged
            {
                public string Kind { get; } = "";

                public int Code { get; }

                public Tagged(int code) { Kind = "code"; }

                public Tagged(string name, int code) { Kind = name; Code = code; }
            }
            """
        );

        var output = result["tagged.ts"];

        await Assert.That(output).DoesNotContain("const code = args[0] as number;");
        await Assert.That(output).Contains("const name = args[0] as string;");
        await Assert.That(output).Contains("const code = args[1] as number;");
    }

    [Test]
    public async Task BclInterfaceConstraint_IsDroppedFromTheTypeParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [ExportedAsModule]
            public static class Picker
            {
                public static T Pick<T>(T v) where T : IComparable<T> => v;

                public static T Pick<T>(T v, int n) where T : IComparable<T> => v;
            }
            """
        );

        var output = result["picker.ts"];

        await Assert.That(output).DoesNotContain("IComparable");
        await Assert.That(output).Contains("export function pick<T>(v: T): T;");
    }

    [Test]
    public async Task TranspiledInterfaceConstraint_IsKept()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public interface IShape { }

            public interface INamed { }

            public static class Measure
            {
                public static T Pick<T>(T v) where T : IComparable<T>, IShape => v;

                public static T Both<T>(T v) where T : IShape, INamed => v;

                public static T Bcl<T>(T v) where T : IComparable<T> => v;
            }
            """
        );

        var output = result["measure.ts"];

        await Assert.That(output).Contains("static pick<T extends IShape>(v: T): T");
        await Assert.That(output).Contains("static both<T extends IShape & INamed>(v: T): T");
        await Assert.That(output).Contains("static bcl<T>(v: T): T");
        await Assert.That(output).DoesNotContain("IComparable");
    }

    [Test]
    public async Task StaticGroupOfGenericClass_RedeclaresTheClassTypeParameters()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Box<T>
            {
                public T V;

                public Box(T v) { V = v; }

                public static Box<T> Make(T v) => new Box<T>(v);

                public static Box<T> Make(T v, int n) => new Box<T>(v);
            }
            """
        );

        var output = result["box.ts"];

        await Assert.That(output).Contains("static makeV<T>(v: T): Box<T> {");
        await Assert.That(output).Contains("static makeVN<T>(v: T, n: number): Box<T> {");
        await Assert.That(output).Contains("static make<T>(v: T): Box<T>;");
        await Assert.That(output).Contains("static make(...args: unknown[]): any {");
    }

    [Test]
    public async Task StaticAndInstanceOverloads_LowerOnTheirOwnSides()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class St
            {
                public int Add(int a) => a;

                public static int Add(string a) => 0;

                public static int Add(string a, string b) => 1;

                public int Use() => Add(1) + Add("x");
            }
            """
        );

        var output = result["st.ts"];

        await Assert.That(output).Contains("static addString(a: string): number {");
        await Assert.That(output).Contains("static add(...args: unknown[]): number {");
        await Assert.That(output).Contains("return St.addString(args[0] as string);");

        await Assert
            .That(output)
            .Contains("add(a: number): number {\n    return this.addInt32(a);");

        await Assert.That(output).DoesNotContain("this.addString(");
    }

    [Test]
    public async Task PlainObjectOverloads_EmitOneFunctionPerName()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [PlainObject]
            public record Dto(int A)
            {
                [Name("getNum")] public int Get(int x) => x;

                [Name("getStr")] public int Get(string s) => 1;
            }
            """
        );

        var output = result["dto.ts"];

        await Assert
            .That(output)
            .Contains("export function getNum(self: Dto, x: number): number {");

        await Assert
            .That(output)
            .Contains("export function getStr(self: Dto, s: string): number {");
    }

    [Test]
    public async Task PlainObjectOverloads_SharingAName_AreReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [PlainObject]
            public record Dto(int A)
            {
                public int Get(int x) => x;

                public int Get(string s) => 1;
            }
            """
        );

        var reported = diagnostics.Where(diagnostic => diagnostic.Code == "MS0001").ToList();

        await Assert.That(reported).Count().IsEqualTo(1);
        await Assert.That(reported[0].Message).Contains("'Dto.Get(string)'");
    }

    [Test]
    public async Task OverloadedImplementation_ForwardsTheRenamedInterfaceMember()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public interface IGreeter
            {
                [Name("hello")]
                string Greet(int n);
            }

            public class Greeter : IGreeter
            {
                public string Greet(int n) => "int";

                public string Greet(string s) => s;
            }
            """
        );

        await Assert
            .That(result["greeter.ts"])
            .Contains("hello(n: number): string {\n    return this.greetN(n);\n  }");
    }

    [Test]
    public async Task InheritedImplementations_ForwardTheInterfaceFastPaths()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public interface IConv<T>
            {
                string Conv(T x);

                string Conv(T x, int n);
            }

            public class B2
            {
                public virtual string Conv(int a) => "bi";

                public virtual string Conv(int a, int n) => "bin";
            }

            public class D2 : B2, IConv<int> { }

            public class D3 : B2, IConv<int>
            {
                public string Conv(bool x) => "db";
            }
            """
        );

        await Assert
            .That(files["d2.ts"])
            .Contains("convX(a: number): string {\n    return this.convA(a);\n  }");

        await Assert
            .That(files["d2.ts"])
            .Contains("convXN(a: number, n: number): string {\n    return this.convAN(a, n);");

        await Assert.That(files["d3.ts"]).Contains("convX$(x: boolean): string {");
        await Assert.That(files["d3.ts"]).Contains("convX(a: number): string {");

        await Assert
            .That(diagnostics.Select(diagnostic => diagnostic.Message))
            .Contains(message => message.Contains("'D3.convX'") && message.Contains("forwarder"));
    }

    [Test]
    public async Task OptionalParameterWithoutDefault_ReceivesTheDefaultOfItsType()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Runtime.InteropServices;
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Opt
            {
                public string Q([Optional] int a, int b) => "q";

                public string R([Optional] string? a, int b) => "r";

                public string Run() => Q(b: 2) + R(b: 3);
            }
            """
        );

        await Assert.That(result["opt.ts"]).Contains("this.q(0, 2)");
        await Assert.That(result["opt.ts"]).Contains("this.r(null, 3)");
    }

    [Test]
    public async Task BrandedStaticMethod_KeepsItsDefaultValues()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct UserId(int Value)
            {
                public static UserId Make(int v = 3) => new UserId(v);
            }
            """
        );

        await Assert.That(result["user-id.ts"]).Contains("function UserId$make(v: number = 3)");
    }

    [Test]
    public async Task OverrideBesideMultiplyConstructedInterface_OverridesTheBaseFastPath()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public interface IConv<T>
            {
                string Conv(T x);

                string Conv(T x, int n);
            }

            public class B
            {
                public virtual string Conv(int a) => "bi";

                public virtual string Conv(int a, int n) => "bin";

                public virtual string Conv(string a) => "bs";

                public virtual string Conv(string a, int n) => "bsn";
            }

            public class D : B, IConv<int>, IConv<string>
            {
                public override string Conv(int a) => "di";
            }
            """
        );

        var output = result["d.ts"];

        await Assert.That(output).Contains("override convInt32(a: number): string {");
        await Assert.That(output).Contains("return this.convInt32(args[0] as number);");
        await Assert.That(output).Contains("return this.convString(args[0] as string);");
        await Assert.That(output).Contains("convXN(...args: unknown[]): string {");

        await Assert
            .That(output)
            .Contains("return this.convStringInt32(args[0] as string, args[1] as number);");
    }

    [Test]
    public async Task OverrideOfABaseAnchoredOnTheSameRoot_StaysInTheRootDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public interface IQ<T>
            {
                [Name("q")]
                string Q(T a);

                string Q(T a, int n);
            }

            public class RB : IQ<int>
            {
                public virtual string Q(int a) => "rbi";

                public virtual string Q(int a, int n) => "rbin";
            }

            public class RD : RB, IQ<string>
            {
                public override string Q(int a) => "rdi";

                public string Q(string a) => "rds";

                public string Q(string a, int n) => "rdsn";
            }
            """
        );

        var output = result["rd.ts"];

        await Assert.That(output).DoesNotContain("override qQ(a: number): string {");
        await Assert.That(output.Split("override qQ(...args: unknown[])").Length).IsEqualTo(2);
        await Assert.That(output).Contains("return \"rdi\";");
    }
}
