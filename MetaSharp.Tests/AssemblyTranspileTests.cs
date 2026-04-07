namespace MetaSharp.Tests;

public class AssemblyTranspileTests
{
    [Test]
    public async Task TranspileAssembly_TranspilesAllPublicTypes()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public record Point(int X, int Y);
            public enum Color { Red, Green, Blue }
            public class Service { }
            """
        );

        await Assert.That(result).ContainsKey("point.ts");
        await Assert.That(result).ContainsKey("color.ts");
        await Assert.That(result).ContainsKey("service.ts");
    }

    [Test]
    public async Task TranspileAssembly_SkipsNonPublicTypes()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public record Visible(int X);
            internal record Hidden(int Y);
            """
        );

        await Assert.That(result).ContainsKey("visible.ts");
        await Assert.That(result).DoesNotContainKey("hidden.ts");
    }

    [Test]
    public async Task NoTranspile_ExcludesType()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public record Included(int X);

            [NoTranspile]
            public record Excluded(int Y);
            """
        );

        await Assert.That(result).ContainsKey("included.ts");
        await Assert.That(result).DoesNotContainKey("excluded.ts");
    }

    [Test]
    public async Task ExplicitTranspile_StillWorks()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Item(string Name);
            """
        );

        await Assert.That(result).ContainsKey("item.ts");
    }

    [Test]
    public async Task NoTranspile_OverridesExplicitTranspile()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoTranspile]
            public record Conflicted(int X);
            """
        );

        await Assert.That(result).DoesNotContainKey("conflicted.ts");
    }

    [Test]
    public async Task AssemblyWide_InheritanceWorks()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            namespace App
            {
                public record Base(int Id);
                public record Child(int Id, string Name) : Base(Id);
            }
            """
        );

        var childTs = result["child.ts"];
        await Assert.That(childTs).Contains("extends Base");
    }

    [Test]
    public async Task AssemblyWide_InterfaceImplementsWorks()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            namespace App
            {
                public interface IEntity { string Id { get; } }
                public record User(string Id, string Name) : IEntity;
            }
            """
        );

        var userTs = result["user.ts"];
        await Assert.That(userTs).Contains("implements IEntity");
    }

    [Test]
    public async Task AssemblyWide_WithGuardAttribute()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [GenerateGuard]
            public record Point(int X, int Y);

            public record Line(int Length);
            """
        );

        // Point has guard (explicit [GenerateGuard])
        await Assert.That(result["point.ts"]).Contains("isPoint");
        // Line does NOT have guard (no [GenerateGuard])
        await Assert.That(result["line.ts"]).DoesNotContain("isLine");
    }
}
