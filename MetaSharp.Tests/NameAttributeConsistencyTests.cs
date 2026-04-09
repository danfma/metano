namespace MetaSharp.Tests;

public class NameAttributeConsistencyTests
{
    [Test]
    public async Task Record_WithNameOverride_UsesRenamedClassDeclaration()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [Name("Renamed")]
            public record struct Original(int Value);
            """
        );

        await Assert.That(result).ContainsKey("renamed.ts");
        var ts = result["renamed.ts"];
        // Class declaration uses the overridden name
        await Assert.That(ts).Contains("export class Renamed");
        // Constructor uses the overridden name in equals/hashCode/with
        await Assert.That(ts).Contains("other instanceof Renamed");
        await Assert.That(ts).Contains("new Renamed(");
        // Should NOT contain the original C# name in the declaration
        await Assert.That(ts).DoesNotContain("export class Original");
    }

    [Test]
    public async Task Class_WithNameOverride_UsedFromAnotherType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [Name("ApiUser")]
            public record struct User(string Name);

            [Transpile]
            public record struct Team(string Label)
            {
                public User CreateUser() => new User("test");
            }
            """
        );

        await Assert.That(result).ContainsKey("api-user.ts");
        var userTs = result["api-user.ts"];
        await Assert.That(userTs).Contains("export class ApiUser");

        var teamTs = result["team.ts"];
        // The reference in new expression should use the overridden name
        await Assert.That(teamTs).Contains("new ApiUser(");
    }

    [Test]
    public async Task Enum_WithNameOverride_UsesRenamedDeclaration()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [Name("StatusCode")]
            public enum Status
            {
                Active = 0,
                Inactive = 1
            }
            """
        );

        await Assert.That(result).ContainsKey("status-code.ts");
        var ts = result["status-code.ts"];
        await Assert.That(ts).Contains("export enum StatusCode");
        await Assert.That(ts).DoesNotContain("export enum Status {");
    }

    [Test]
    public async Task StringEnum_WithNameOverride_UsesRenamedConstAndTypeAlias()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [StringEnum]
            [Name("CurrencyCode")]
            public enum Currency
            {
                USD,
                EUR
            }
            """
        );

        await Assert.That(result).ContainsKey("currency-code.ts");
        var ts = result["currency-code.ts"];
        await Assert.That(ts).Contains("export const CurrencyCode");
        await Assert.That(ts).Contains("export type CurrencyCode");
        await Assert.That(ts).DoesNotContain("export const Currency ");
        await Assert.That(ts).DoesNotContain("export type Currency ");
    }

    [Test]
    public async Task Interface_WithNameOverride_UsesRenamedDeclaration()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [Name("IApiService")]
            public interface IService
            {
                string GetName();
            }
            """
        );

        await Assert.That(result).ContainsKey("i-api-service.ts");
        var ts = result["i-api-service.ts"];
        await Assert.That(ts).Contains("export interface IApiService");
        await Assert.That(ts).DoesNotContain("export interface IService");
    }

    [Test]
    public async Task Exception_WithNameOverride_UsesRenamedClassDeclaration()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [Name("ApiError")]
            public class ServiceException(string message) : Exception(message);
            """
        );

        await Assert.That(result).ContainsKey("api-error.ts");
        var ts = result["api-error.ts"];
        await Assert.That(ts).Contains("export class ApiError");
        await Assert.That(ts).DoesNotContain("export class ServiceException");
    }

    [Test]
    public async Task TypeReference_WithNameOverride_UsesRenamedInTypeMapper()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            [Name("ApiUser")]
            public record struct User(string Name);

            [Transpile]
            public record struct UserList(List<User> Users);
            """
        );

        var ts = result["user-list.ts"];
        // The type reference in the property should use the overridden name
        await Assert.That(ts).Contains("ApiUser[]");
    }
}
