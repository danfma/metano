namespace Metano.Tests;

/// <summary>
/// An array literal passed to a parameter declared through a type parameter
/// keeps the element type C# gives it: TypeScript infers the type argument
/// from the literal, so <c>[1]</c> alone would make a <c>List&lt;int?&gt;</c>
/// a <c>List&lt;number&gt;</c> and a later <c>Add(null)</c> fail (#614).
/// </summary>
public class GenericArgumentLiteralTypingTests
{
    private const string Source = """
        using System.Collections.Generic;
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        public static class Literals
        {
            public static T[] WrapArray<T>(T[] xs) => xs;

            public static int Count(List<int?> xs) => xs.Count;

            public static int Run()
            {
                var copied = new List<int?>([1]);
                copied.Add(null);

                var wrapped = WrapArray<int?>([1]);
                wrapped[0] = null;

                return copied.Count + wrapped.Length + Count([1]);
            }
        }
        """;

    [Test]
    [Arguments("const copied = List.from([1] as (number | null)[]);")]
    [Arguments("const wrapped = Literals.wrapArray<number | null>([1] as (number | null)[]);")]
    [Arguments("Literals.count(List.of<number | null>([1]))")]
    public async Task LiteralArgument_KeepsItsElementType(string expected)
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert.That(result["literals.ts"]).Contains(expected);
    }
}
