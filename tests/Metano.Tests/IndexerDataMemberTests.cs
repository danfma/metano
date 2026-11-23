namespace Metano.Tests;

/// <summary>
/// An indexer is not a data member: no guard, interface field, record
/// equality term or JSON member may enumerate it (#474).
/// </summary>
public class IndexerDataMemberTests
{
    private const string PlainObjectSource = """
        [Transpile, PlainObject, GenerateGuard]
        public record Slot(int A)
        {
            public int this[int i] => A + i;
        }
        """;

    private const string ClassSource = """
        [Transpile, GenerateGuard]
        public class Holder
        {
            public int Size { get; set; } = 2;

            public int this[int i] => Size * i;
        }
        """;

    [Test]
    public async Task PlainObjectGuard_SkipsIndexer()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(PlainObjectSource);
        var output = string.Join("\n", files.Values);

        await Assert.That(output).DoesNotContain("this[]");
        await Assert.That(output).Contains("isSlot");
        await Assert.That(output).Contains("typeof v.a === \"number\"");
    }

    [Test]
    public async Task ClassGuardAndMembers_SkipIndexer()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(ClassSource);
        var output = files["holder.ts"];

        await Assert.That(output).DoesNotContain("this[]");
        await Assert.That(output).Contains("size");
    }
}
