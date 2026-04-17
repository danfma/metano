using Metano.Compiler.Extraction;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Tests.IR;

/// <summary>
/// Golden tests for the IR-to-TypeScript bridges. These fix the expected output so
/// future IR changes can't silently drift from the current TypeScript rendering.
/// </summary>
public class IrToTsBridgeGoldenTests
{
    [Test]
    public async Task NumericEnum_PrintsCorrectly()
    {
        var output = PrintFromSource(
            """
            [Transpile]
            public enum Priority { Low = 0, Medium = 1, High = 2 }
            """,
            TypeKind.Enum,
            (type, sink) => IrToTsEnumBridge.Convert(IrEnumExtractor.Extract(type), sink)
        );

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export enum Priority {
                  Low = 0,
                  Medium = 1,
                  High = 2,
                }

                """
            );
    }

    [Test]
    public async Task StringEnum_PrintsAsConstObjectAndTypeAlias()
    {
        var output = PrintFromSource(
            """
            [Transpile]
            [StringEnum]
            public enum Color { Red, Green, Blue }
            """,
            TypeKind.Enum,
            (type, sink) => IrToTsEnumBridge.Convert(IrEnumExtractor.Extract(type), sink)
        );

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export const Color = {
                  Red: "Red",
                  Green: "Green",
                  Blue: "Blue",
                } as const;

                export type Color = typeof Color[keyof typeof Color];

                """
            );
    }

    [Test]
    public async Task SimpleInterface_Prints()
    {
        var output = PrintFromSource(
            """
            [Transpile]
            public interface ITodoItem
            {
                string Title { get; }
                bool IsCompleted { get; set; }
            }
            """,
            TypeKind.Interface,
            (type, sink) => IrToTsInterfaceBridge.Convert(IrInterfaceExtractor.Extract(type), sink)
        );

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export interface ITodoItem {
                  readonly title: string;
                  isCompleted: boolean;
                }

                """
            );
    }

    [Test]
    public async Task GenericInterface_Prints()
    {
        var output = PrintFromSource(
            """
            [Transpile]
            public interface IRepository<T>
            {
                Task<T?> FindAsync(string id);
            }
            """,
            TypeKind.Interface,
            (type, sink) => IrToTsInterfaceBridge.Convert(IrInterfaceExtractor.Extract(type), sink)
        );

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export interface IRepository<T> {
                  findAsync(id: string): Promise<T | null>;
                }

                """
            );
    }

    [Test]
    public async Task PlainObjectRecord_PrintsAsInterface()
    {
        var output = PrintFromSource(
            """
            [Transpile]
            [PlainObject]
            public record UserDto(string Name, int Age = 0);
            """,
            TypeKind.Class,
            (type, sink) => IrToTsPlainObjectBridge.Convert(IrClassExtractor.Extract(type), sink)
        );

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export interface UserDto {
                  readonly name: string;
                  readonly age?: number;
                }

                """
            );
    }

    // -- helper --

    private static string PrintFromSource(
        string csharpSource,
        TypeKind kind,
        Action<INamedTypeSymbol, List<TsTopLevel>> convert
    )
    {
        var compilation = IrTestHelper.Compile(csharpSource);
        INamedTypeSymbol? found = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                var symbol = model.GetDeclaredSymbol(node);
                if (
                    symbol is INamedTypeSymbol named
                    && named.TypeKind == kind
                    && Metano.Compiler.SymbolHelper.HasTranspile(named)
                )
                {
                    found = named;
                    break;
                }
            }
            if (found is not null)
                break;
        }
        if (found is null)
            throw new InvalidOperationException($"No [Transpile]-annotated {kind} found.");

        var statements = new List<TsTopLevel>();
        convert(found, statements);
        var file = new TsSourceFile("test.ts", statements);
        return new Printer().Print(file);
    }
}
