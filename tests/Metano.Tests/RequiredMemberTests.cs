namespace Metano.Tests;

/// <summary>
/// A <c>required</c> member is set by the object initializer C# demands at every
/// creation, after the TypeScript constructor runs, so a member with no initializer
/// carries the definite-assignment assertion (<c>name!: T</c>) instead of failing
/// <c>strictPropertyInitialization</c> (TS2564). A member with an initializer or a
/// computed default keeps it.
/// </summary>
public class RequiredMemberTests
{
    [Test]
    public async Task RequiredMembers_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Point
            {
                public int X;
            }

            [Transpile]
            public class Person
            {
                public required string Name { get; set; }
                public required string Nick;
                public required int Age { get; init; }
                public required string Title { get; set; } = "Mx";
                public required Point Home { get; set; }
                public string? Note { get; set; }
            }
            """
        );

        await Assert
            .That(result["person.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("required-members.ts"));
    }

    [Test]
    public async Task RequiredRecordProperty_CarriesTheDefiniteAssignmentAssertion()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Tag
            {
                public required string Name { get; init; }
            }
            """
        );

        await Assert.That(result["tag.ts"]).Contains("readonly name!: string;");
    }
}
