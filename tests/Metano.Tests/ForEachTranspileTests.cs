namespace Metano.Tests;

public class ForEachTranspileTests
{
    [Test]
    public async Task ForEach_OverList_LowersToForOf()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Printer
            {
                public void PrintAll(IReadOnlyList<string> lines)
                {
                    foreach (var line in lines)
                    {
                        System.Console.WriteLine(line);
                    }
                }
            }
            """
        );

        var output = result["app/printer.ts"];
        await Assert.That(output).Contains("for (const line of lines)");
        await Assert.That(output).Contains("console.log(line)");
    }

    [Test]
    public async Task ForEach_OverLinqChain_EvaluatesOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            namespace App;

            [Transpile]
            public sealed class Filter
            {
                public void Run(IReadOnlyList<int> source)
                {
                    foreach (var x in source.Where(n => n > 0).Select(n => n * 2))
                    {
                        System.Console.WriteLine(x);
                    }
                }
            }
            """
        );

        var output = result["app/filter.ts"];
        await Assert.That(output).Contains("for (const x of");
        // After #20 the LINQ chain lowers to the pipe form: a single
        // `linq(source, where(...), select(...))` call instead of a
        // fluent `.where().select()` chain on `Enumerable.from(...)`.
        await Assert.That(output).Contains("linq(source, where(");
        await Assert.That(output).Contains("select(");
    }

    [Test]
    public async Task ForEach_BreakAndContinue_LowersDirectly()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Walker
            {
                public void Walk(IReadOnlyList<int> nums)
                {
                    foreach (var n in nums)
                    {
                        if (n < 0) continue;
                        if (n > 100) break;
                        System.Console.WriteLine(n);
                    }
                }
            }
            """
        );

        var output = result["app/walker.ts"];
        await Assert.That(output).Contains("for (const n of nums)");
        await Assert.That(output).Contains("continue");
        await Assert.That(output).Contains("break");
    }

    [Test]
    public async Task ForEach_Nested_BothBindingsSurvive()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Matrix
            {
                public void Print(IReadOnlyList<IReadOnlyList<int>> grid)
                {
                    foreach (var row in grid)
                    {
                        foreach (var cell in row)
                        {
                            System.Console.WriteLine(cell);
                        }
                    }
                }
            }
            """
        );

        var output = result["app/matrix.ts"];
        await Assert.That(output).Contains("for (const row of grid)");
        await Assert.That(output).Contains("for (const cell of row)");
    }

    [Test]
    public async Task ForEach_OverMethodCall_EvaluatesCallExpression()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Repo
            {
                public IReadOnlyList<string> GetItems() => new List<string>();

                public void Run()
                {
                    foreach (var x in GetItems())
                    {
                        System.Console.WriteLine(x);
                    }
                }
            }
            """
        );

        var output = result["app/repo.ts"];
        await Assert.That(output).Contains("for (const x of this.getItems())");
    }

    /// <summary>
    /// <c>foreach (int x in double?[])</c> throws like .NET when an element is
    /// missing (<c>InvalidOperationException</c> — "Nullable object must have
    /// a value.", issue #488) instead of lifting the <c>null</c> into the
    /// non-nullable variable: the loop reads through the same
    /// unwrap-or-throw the explicit cast (<c>(int)d</c>) lowers to.
    /// </summary>
    [Test]
    public async Task ForEach_NullableElementIntoNonNullableVariable_ThrowsWhenMissing()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Sum
            {
                public int Total(double?[] values)
                {
                    var total = 0;

                    #pragma warning disable CS8629
                    foreach (int x in values)
                        total += x;
                    #pragma warning restore CS8629

                    return total;
                }
            }
            """
        );

        var output = result["app/sum.ts"];

        await Assert.That(output).Contains("const x = int32FromDouble(x$element ?? (() => {");
        await Assert.That(output).Contains("Nullable object must have a value.");
        await Assert.That(output).DoesNotContain("x$element == null ? null :");
    }

    /// <summary>
    /// <c>foreach (int x in int?[])</c> unwraps the element with the same
    /// throw even though the kinds match, so a missing value never flows into
    /// the variable unconverted.
    /// </summary>
    [Test]
    public async Task ForEach_NullableElementOfTheSameKind_StillThrowsWhenMissing()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Sum
            {
                public int Total(int?[] values)
                {
                    var total = 0;

                    #pragma warning disable CS8629
                    foreach (int x in values)
                        total += x;
                    #pragma warning restore CS8629

                    return total;
                }
            }
            """
        );

        var output = result["app/sum.ts"];

        await Assert.That(output).Contains("const x = x$element ?? (() => {");
        await Assert.That(output).Contains("Nullable object must have a value.");
    }

    /// <summary>
    /// <c>foreach (int? x in double?[])</c> keeps the lifted conversion: a
    /// missing element holds the variable as <c>null</c>, exactly .NET's
    /// lifted behavior.
    /// </summary>
    [Test]
    public async Task ForEach_NullableElementIntoNullableVariable_Lifts()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Sum
            {
                public int? Total(double?[] values)
                {
                    int? total = 0;

                    foreach (int? x in values)
                        total += x;

                    return total;
                }
            }
            """
        );

        var output = result["app/sum.ts"];

        await Assert
            .That(output)
            .Contains("const x = x$element == null ? null : int32FromDouble(x$element);");
    }

    /// <summary>
    /// Dart keeps the same runtime shape the TypeScript loop has: the loop
    /// variable is declared from the unwrap-or-throw of the raw element,
    /// read through the underlying type and truncated there.
    /// </summary>
    [Test]
    public async Task Dart_ForEach_NullableElementIntoNonNullableVariable_ThrowsWhenMissing()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Sum
            {
                public int Total(double?[] values)
                {
                    var total = 0;

                    #pragma warning disable CS8629
                    foreach (int x in values)
                        total += x;
                    #pragma warning restore CS8629

                    return total;
                }
            }
            """
        );

        var dart = files["sum.dart"];

        await Assert
            .That(dart)
            .Contains(
                "int x = ((x$element ?? (throw InvalidOperationException('Nullable object must have a value.'))) as double).toInt().toSigned(32);"
            );
        await Assert.That(dart).DoesNotContain("? null :");
    }

    /// <summary>
    /// With the kinds already equal there is no conversion to run after the
    /// unwrap, so Dart declares the variable from the throw alone.
    /// </summary>
    [Test]
    public async Task Dart_ForEach_NullableElementOfTheSameKind_StillThrowsWhenMissing()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Sum
            {
                public int Total(int?[] values)
                {
                    var total = 0;

                    #pragma warning disable CS8629
                    foreach (int x in values)
                        total += x;
                    #pragma warning restore CS8629

                    return total;
                }
            }
            """
        );

        var dart = files["sum.dart"];

        await Assert
            .That(dart)
            .Contains(
                "int x = (x$element ?? (throw InvalidOperationException('Nullable object must have a value.'))) as int;"
            );
        await Assert.That(dart).DoesNotContain("? null :");
    }

    [Test]
    public async Task Dart_ForEach_NullableElementIntoNullableVariable_Lifts()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Sum
            {
                public int? Total(double?[] values)
                {
                    int? total = 0;

                    foreach (int? x in values)
                        total += x;

                    return total;
                }
            }
            """
        );

        var dart = files["sum.dart"];

        // Dart reads the lifted conversion through the nullable-aware `?.`
        // member chain: a missing element holds the variable as `null`.
        await Assert.That(dart).Contains("int? x = x$element?.toInt().toSigned(32);");
    }

    /// <summary>
    /// With the kinds already matching there is no conversion to run after
    /// the unwrap, so the unwrap itself is the variable's cast — an enum
    /// loop variable reads `as Color`, not the underlying `as int`.
    /// </summary>
    [Test]
    public async Task Dart_ForEach_NullableEnumElement_CastsToTheVariableType()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            public enum Color { Red, Green }

            [Transpile]
            public sealed class Picker
            {
                public Color First(Color?[] values)
                {
                    #pragma warning disable CS8629
                    foreach (Color c in values)
                        return c;
                    #pragma warning restore CS8629

                    return Color.Red;
                }
            }
            """
        );

        await Assert
            .That(files["picker.dart"])
            .Contains(
                "Color c = (c$element ?? (throw InvalidOperationException('Nullable object must have a value.'))) as Color;"
            );
    }
}
