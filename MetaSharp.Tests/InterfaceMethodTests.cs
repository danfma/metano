namespace MetaSharp.Tests;

public class InterfaceMethodTests
{
    [Test]
    public async Task InterfaceWithMethods_GeneratesSignatures()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IRepository
            {
                string GetById(int id);
                void Save(string name);
            }
            """
        );

        var output = result["i-repository.ts"];
        await Assert.That(output).Contains("getById(id: number): string;");
        await Assert.That(output).Contains("save(name: string): void;");
    }

    [Test]
    public async Task InterfaceWithPropertiesAndMethods_GeneratesBoth()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IEntity
            {
                int Id { get; }
                string GetName();
            }
            """
        );

        var output = result["i-entity.ts"];
        await Assert.That(output).Contains("readonly id: number;");
        await Assert.That(output).Contains("getName(): string;");
    }

    [Test]
    public async Task InterfaceWithAsyncMethods_GeneratesPromiseReturn()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public interface IAsyncRepo
            {
                Task<string> FindAsync(int id);
                Task SaveAsync(string name);
            }
            """
        );

        var output = result["i-async-repo.ts"];
        await Assert.That(output).Contains("findAsync(id: number): Promise<string>;");
        await Assert.That(output).Contains("saveAsync(name: string): Promise<void>;");
    }
}
