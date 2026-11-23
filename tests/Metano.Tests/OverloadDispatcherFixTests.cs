namespace Metano.Tests;

/// <summary>
/// Overload dispatcher fixes of the 3.0.x backlog (wave 6, group I): what a
/// dispatcher binds, declares and names.
/// </summary>
public class OverloadDispatcherFixTests
{
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
}
