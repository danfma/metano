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
