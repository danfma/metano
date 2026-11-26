namespace Metano.Tests;

/// <summary>
/// The module functions of extension members declared in a referenced
/// package (property and indexer accessors, static or not, and extension
/// methods) are imported from that package (#766, FR-110), like its
/// extension operators.
/// </summary>
public class CrossPackageExtensionAccessorTests
{
    private const string Library = """
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("sig-lib")]

        public sealed class Sig(int value)
        {
            public int Value { get; set; } = value;
        }

        public static class SigExtensions
        {
            private static Sig _zero = new(0);

            extension(Sig)
            {
                public static Sig Zero
                {
                    get => _zero;
                    set => _zero = value;
                }

                public static Sig Make(int value) => new(value);
            }

            extension(Sig sig)
            {
                public int Doubled => sig.Value * 2;

                public int Twice() => sig.Value * 2;

                public int this[int offset]
                {
                    get => sig.Value + offset;
                    set => sig.Value = value - offset;
                }
            }

            public static int Thrice(this Sig sig) => sig.Value * 3;
        }
        """;

    [Test]
    public async Task StaticExtensionPropertyFromAReferencedPackage_IsImportedFromIt()
    {
        const string consumer = """
            [Transpile]
            public static class Use
            {
                public static Sig Read() => Sig.Zero;

                public static void Write(Sig s) { Sig.Zero = s; }
            }
            """;

        foreach (var files in BothLibraryShapes(consumer))
        {
            var use = files["use.ts"];

            await Assert
                .That(use)
                .Contains("import { zero$get, zero$set, type Sig } from \"sig-lib\";");

            await Assert.That(use).Contains("return zero$get();");
            await Assert.That(use).Contains("zero$set(s);");
        }
    }

    [Test]
    public async Task InstanceExtensionAccessorsFromAReferencedPackage_AreImportedFromIt()
    {
        const string consumer = """
            [Transpile]
            public static class Use
            {
                public static int Read(Sig s) => s.Doubled + s[1] + s.Twice() + s.Thrice();

                public static void Write(Sig s) { s[1] = 5; }

                public static Sig Make() => Sig.Make(3);
            }
            """;

        foreach (var files in BothLibraryShapes(consumer))
        {
            var use = files["use.ts"];

            await Assert
                .That(use)
                .Contains(
                    "import { doubled$get, item$get, item$set, make, thrice, twice, type Sig } "
                        + "from \"sig-lib\";"
                );
        }
    }

    [Test]
    public async Task ExtensionIndexerFromAReferencedPackage_ImportsItsAccessors()
    {
        const string consumer = """
            [Transpile]
            public static class Use
            {
                public static int Bump(Sig s) => s[1]++;

                public static void Write(Sig s) { s[2] = 7; }
            }
            """;

        await TranspileHelper.AssertGolden(
            "extension-indexer-cross-package.ts",
            TranspileHelper.TranspileWithLibrary(Library, consumer)["use.ts"]
        );
    }

    private static IEnumerable<Dictionary<string, string>> BothLibraryShapes(string consumer)
    {
        yield return TranspileHelper.TranspileWithLibrary(Library, consumer);
        yield return TranspileHelper.TranspileWithLibraryImage(Library, consumer);
    }
}
