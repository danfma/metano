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

            public static class Measure
            {
                public static T Pick<T>(T v) where T : IShape, IComparable<T> => v;
            }
            """
        );

        await Assert.That(result["measure.ts"]).Contains("static pick<T extends IShape>(v: T): T");
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
}
