namespace Metano.Tests;

/// <summary>
/// A <c>[Name]</c> on a property of a C# 14 extension block renames its
/// accessor functions and keeps their <c>$get</c> / <c>$set</c> suffix, which
/// the call sites spell: before the fix the getter and setter both declared
/// the bare override (one name twice) and the call imported a
/// <c>renamed$get</c> nothing exported.
/// </summary>
public class ExtensionPropertyNameOverrideTests
{
    private const string Source = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        public static class TextExtensions
        {
            extension(string s)
            {
                [Name("lengthK")]
                public int Size => s.Length;

                [Name("widthK")]
                public int Width { get => 1; set { } }

                [Name("twiceK")]
                public string Twice() => s + s;
            }
        }

        public static class Use
        {
            public static int Run(string q)
            {
                q.Width = 3;

                return q.Size + q.Width + q.Twice().Length;
            }
        }
        """;

    [Test]
    public async Task RenamedAccessors_KeepTheirSuffix()
    {
        var result = TranspileHelper.Transpile(Source);
        var helpers = result["text-extensions.ts"];

        await Assert.That(helpers).Contains("export function lengthK$get(s: string): number");
        await Assert.That(helpers).Contains("export function widthK$get(s: string): number");
        await Assert.That(helpers).Contains("export function widthK$set(s: string, value: number)");
        await Assert.That(helpers).Contains("export function twiceK(s: string): string");
    }

    [Test]
    public async Task CallSites_NameTheDeclaredAccessors()
    {
        var result = TranspileHelper.Transpile(Source);
        var use = result["use.ts"];

        await Assert.That(use).Contains("widthK$set(q, 3);");
        await Assert.That(use).Contains("lengthK$get(q) + widthK$get(q) + twiceK(q).length");
    }
}
