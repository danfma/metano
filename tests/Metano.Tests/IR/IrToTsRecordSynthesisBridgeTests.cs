using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

/// <summary>
/// Pins the TS output shape produced by
/// <see cref="IrToTsRecordSynthesisBridge"/>. Previously this also
/// cross-checked against the legacy <c>RecordSynthesizer</c>; that
/// synthesizer has since been retired now that the IR path is the
/// production default, so the tests assert the emitted structure
/// directly.
/// </summary>
public class IrToTsRecordSynthesisBridgeTests
{
    [Test]
    public async Task EqualsHashCodeWith_EmitExpectedShape()
    {
        var (_, ir, _, ctorParams) = Extract(
            """
            [Transpile]
            public record Money(int Amount, string Currency);
            """,
            "Money"
        );
        var printed = PrintMembers(IrToTsRecordSynthesisBridge.Generate(ir, ctorParams));

        // equals: narrow to the record type, then compare every field with ===.
        await Assert.That(printed).Contains("equals(other: any): boolean");
        await Assert
            .That(printed)
            .Contains(
                "other instanceof Money && this.amount === other.amount && this.currency === other.currency"
            );
        // hashCode: drive the HashCode builder over every field.
        await Assert.That(printed).Contains("hashCode(): number");
        await Assert.That(printed).Contains("const hc = new HashCode();");
        await Assert.That(printed).Contains("hc.add(this.amount);");
        await Assert.That(printed).Contains("hc.add(this.currency);");
        await Assert.That(printed).Contains("return hc.toHashCode();");
        // with: rebuild the record, preferring overrides (?? falls back to this).
        await Assert.That(printed).Contains("with(overrides?: Partial<Money>): Money");
        await Assert.That(printed).Contains("overrides?.amount ?? this.amount");
        await Assert.That(printed).Contains("overrides?.currency ?? this.currency");
        await Assert.That(printed).Contains("new Money(");
    }

    [Test]
    public async Task GenericRecord_CarriesTypeParametersOnWithReturnType()
    {
        var (_, ir, _, ctorParams) = Extract(
            """
            [Transpile]
            public record Pair<K, V>(K Key, V Value);
            """,
            "Pair"
        );
        var members = IrToTsRecordSynthesisBridge.Generate(ir, ctorParams);
        var printed = PrintMembers(members);

        // `with` must retain the full self type so the return signature
        // reads Pair<K, V>.
        await Assert.That(printed).Contains("Partial<Pair<K, V>>");
        await Assert.That(printed).Contains("): Pair<K, V>");
    }

    [Test]
    public async Task PlainObjectRecord_IsSkippedByShouldSynthesize()
    {
        // [PlainObject] records must NOT receive synthesized members — they
        // emit as plain data carriers. ShouldSynthesize is the gate.
        var (_, ir, _, _) = Extract(
            """
            [Transpile, PlainObject]
            public record Money(int Amount);
            """,
            "Money"
        );
        await Assert.That(IrToTsRecordSynthesisBridge.ShouldSynthesize(ir)).IsFalse();
    }

    [Test]
    public async Task NonRecordClass_IsSkippedByShouldSynthesize()
    {
        var (_, ir, _, _) = Extract(
            """
            [Transpile]
            public class Bag { public int Count { get; } }
            """,
            "Bag"
        );
        await Assert.That(IrToTsRecordSynthesisBridge.ShouldSynthesize(ir)).IsFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (
        INamedTypeSymbol Unused,
        IrClassDeclaration Ir,
        INamedTypeSymbol Symbol,
        IReadOnlyList<TsConstructorParam> CtorParams
    ) Extract(string source, string typeName)
    {
        var compilation = IrTestHelper.Compile(source);
        var tree = compilation.SyntaxTrees.First();
        var typeDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(t => t.Identifier.ValueText == typeName);
        var model = compilation.GetSemanticModel(tree);
        var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(typeDecl)!;

        var ir = IrClassExtractor.Extract(symbol, compilation: compilation);

        // Build TsConstructorParams from the IR — mirrors what
        // IrToTsClassEmitter passes to IrToTsRecordSynthesisBridge.
        var ctorParams =
            ir.Constructor?.Parameters.Select(p => new TsConstructorParam(
                    TypeScriptNaming.ToCamelCase(p.Parameter.Name),
                    IrToTsTypeMapper.Map(p.Parameter.Type),
                    Readonly: p.Promotion == IrParameterPromotion.ReadonlyProperty,
                    Accessibility: TsAccessibility.Public
                ))
                .ToList()
            ?? (IReadOnlyList<TsConstructorParam>)[];
        return (symbol, ir, symbol, ctorParams);
    }

    private static string PrintMembers(IReadOnlyList<TsClassMember> members)
    {
        var cls = new TsClass("Scratch", Constructor: null, Members: members);
        var file = new TsSourceFile("scratch.ts", [cls]);
        var printed = new Printer().Print(file);
        var start = printed.IndexOf('\n') + 1;
        var end = printed.LastIndexOf('}');
        return printed[start..end].Trim();
    }
}
