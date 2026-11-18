using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Tests;

/// <summary>
/// Issue #682: consecutive single-line class fields print without blank lines
/// between them; a field that prints on more than one line (a multi-line
/// initializer or a doc comment) gets a blank line above and below; one blank
/// line still separates the field block from the constructor and the other
/// member kinds.
/// </summary>
public class ClassFieldLayoutTests
{
    private static readonly TsType NumberType = new TsNumberType();

    [Test]
    public async Task SingleLineFields_PrintWithoutBlankLines()
    {
        var output = PrintClass([
            new TsFieldMember("x", NumberType, new TsLiteral("0")),
            new TsFieldMember("y", NumberType, new TsLiteral("0"), Readonly: true),
            new TsFieldMember("z", NumberType, DefinitelyAssigned: true),
        ]);

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export class Probe {
                  x: number = 0;
                  readonly y: number = 0;
                  z!: number;
                }

                """
            );
    }

    [Test]
    public async Task MultiLineInitializerInTheMiddle_IsSurroundedByBlankLines()
    {
        var output = PrintClass([
            new TsFieldMember("a", NumberType, new TsLiteral("1")),
            new TsFieldMember("options", NumberType, FourPropertyObject()),
            new TsFieldMember("b", NumberType, new TsLiteral("2")),
        ]);

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export class Probe {
                  a: number = 1;

                  options: number = {
                    a: 1,
                    b: 2,
                    c: 3,
                    d: 4,
                  };

                  b: number = 2;
                }

                """
            );
    }

    [Test]
    public async Task FieldWithDocComment_CountsAsMultiLine()
    {
        var output = PrintClass([
            new TsFieldMember("a", NumberType, new TsLiteral("1")),
            new TsFieldMember(
                "documented",
                NumberType,
                new TsLiteral("2"),
                JSDoc: new TsJSDoc(["The documented field."])
            ),
            new TsFieldMember("b", NumberType, new TsLiteral("3")),
        ]);

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export class Probe {
                  a: number = 1;

                  /** The documented field. */
                  documented: number = 2;

                  b: number = 3;
                }

                """
            );
    }

    [Test]
    public async Task StaticAndInstanceFields_PrintAsOneBlock()
    {
        var output = PrintClass([
            new TsFieldMember("count", NumberType, new TsLiteral("0"), Static: true),
            new TsFieldMember("id", NumberType, new TsLiteral("0"), Readonly: true),
            new TsFieldMember(
                "limit",
                NumberType,
                new TsLiteral("10"),
                Readonly: true,
                Static: true
            ),
            new TsFieldMember("name", NumberType, Optional: true),
        ]);

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export class Probe {
                  static count: number = 0;
                  readonly id: number = 0;
                  static readonly limit: number = 10;
                  name?: number;
                }

                """
            );
    }

    [Test]
    public async Task FieldBlock_IsSeparatedFromTheConstructorAndMethods()
    {
        var output = PrintClass(
            [
                new TsFieldMember("x", NumberType, new TsLiteral("0")),
                new TsFieldMember("y", NumberType, new TsLiteral("0")),
                new TsMethodMember("first", [], new TsVoidType(), []),
                new TsMethodMember("second", [], new TsVoidType(), []),
            ],
            new TsConstructor([], [])
        );

        await Assert
            .That(output)
            .IsEqualTo(
                """
                export class Probe {
                  x: number = 0;
                  y: number = 0;

                  constructor() { }

                  first(): void {
                  }

                  second(): void {
                  }
                }

                """
            );
    }

    private static TsObjectLiteral FourPropertyObject() =>
        new([
            new TsObjectProperty("a", new TsLiteral("1")),
            new TsObjectProperty("b", new TsLiteral("2")),
            new TsObjectProperty("c", new TsLiteral("3")),
            new TsObjectProperty("d", new TsLiteral("4")),
        ]);

    private static string PrintClass(
        IReadOnlyList<TsClassMember> members,
        TsConstructor? constructor = null
    ) =>
        new Printer().Print(
            new TsSourceFile("probe.ts", [new TsClass("Probe", constructor, members)])
        );
}
