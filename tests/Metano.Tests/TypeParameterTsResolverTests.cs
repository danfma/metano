using Metano.Compiler.IR;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests;

/// <summary>
/// Unit tests for Group G's scope-aware <see cref="TypeParameterTsResolver"/>
/// and constructor slot helpers (spec 011 W5-5(a), (b), (g), (i)).
/// </summary>
public class TypeParameterTsResolverTests
{
    private static string PrintExpression(TsExpression expression) =>
        new Printer()
            .Print(
                new TsSourceFile(
                    "test.ts",
                    [new TsTopLevelStatement(new TsExpressionStatement(expression))]
                )
            )
            .Trim();

    [Test]
    public async Task MethodScope_ResolvesToHiddenParameter()
    {
        var scope = TsTypeParameterScope.FromBindings([
            new TsTypeParameterBinding(
                "T",
                TsTypeParameterScopeKind.Method,
                0,
                HiddenParameterName: "T$type"
            ),
        ]);

        var resolved = TypeParameterTsResolver.ResolveParameter("T", scope);

        await Assert.That(resolved).IsNotNull();
        await Assert.That(PrintExpression(resolved!)).IsEqualTo("T$type;");
        await Assert.That(TypeParameterTsResolver.ResolveParameter("U", scope)).IsNull();
    }

    [Test]
    public async Task ClassSlotScope_ResolvesToSlotIndex()
    {
        var scope = TsTypeParameterScope.ForClassSlot("$typeArguments$Pair", ["TFirst", "TSecond"]);

        var first = TypeParameterTsResolver.ResolveParameter("TFirst", scope);
        var second = TypeParameterTsResolver.ResolveParameter("TSecond", scope);

        await Assert.That(PrintExpression(first!)).IsEqualTo("this.$typeArguments$Pair[0];");
        await Assert.That(PrintExpression(second!)).IsEqualTo("this.$typeArguments$Pair[1];");
    }

    [Test]
    public async Task CtorParamScope_ResolvesWithUnknownTypeDefault()
    {
        var scope = TsTypeParameterScope.ForCtorParam(["T"]);

        var resolved = TypeParameterTsResolver.ResolveParameter("T", scope);

        await Assert.That(PrintExpression(resolved!)).IsEqualTo("($typeArgs?.[0] ?? UnknownType);");
    }

    [Test]
    public async Task CollectionDescriptor_ResolvesReifiedTypeParameterArgument()
    {
        var scope = TsTypeParameterScope.ForClassSlot("$typeArguments$Box", ["T"]);
        var listOfT = new IrCollectionTypeRef(
            IrCollectionKind.List,
            [new IrTypeParameterRef("T")],
            [IrVariance.Invariant]
        );

        var resolved = TypeParameterTsResolver.ResolveTypeRef(
            listOfT,
            "type argument",
            RuntimeTypeReachabilityResult.Empty,
            scope
        );

        await Assert
            .That(PrintExpression(resolved!))
            .IsEqualTo("listType(this.$typeArguments$Box[0]);");
    }

    [Test]
    public async Task WithTypeArgsParam_InsertsBeforeParamsRest()
    {
        IReadOnlyList<IrTypeParameter> typeParameters = [new IrTypeParameter("T")];
        IReadOnlyList<TsConstructorParam> parameters =
        [
            new TsConstructorParam("value", new TsNamedType("T")),
            new TsConstructorParam(
                "rest",
                new TsNamedType("string[]"),
                Accessibility: TsAccessibility.None,
                Rest: true
            ),
        ];

        var updated = IrToTsClassBridge.WithTypeArgsParam(parameters, typeParameters);

        await Assert.That(updated.Count).IsEqualTo(3);
        await Assert.That(updated[0].Name).IsEqualTo("value");
        await Assert.That(updated[1].Name).IsEqualTo("$typeArgs");
        await Assert.That(updated[1].Optional).IsTrue();
        await Assert.That(updated[2].Name).IsEqualTo("rest");
        await Assert.That(updated[2].Rest).IsTrue();
    }

    [Test]
    public async Task SlotDeclarationAndDefineProperty_PrintExpectedShape()
    {
        IReadOnlyList<IrTypeParameter> typeParameters = [new IrTypeParameter("T")];
        var slotName = IrToTsClassBridge.SlotNameOf("Box");
        var field = IrToTsClassBridge.BuildSlotDeclaration(slotName, typeParameters);
        var define = IrToTsClassBridge.BuildSlotDefineProperty(slotName);

        var tsClass = new TsClass(
            "Box",
            new TsConstructor([], [define]),
            [field],
            TypeParameters: [new TsTypeParameter("T")]
        );

        var printed = new Printer().Print(new TsSourceFile("box.ts", [tsClass]));

        await Assert
            .That(printed)
            .Contains("declare readonly $typeArguments$Box: readonly [RuntimeType<T>];");
        await Assert
            .That(printed)
            .Contains("Object.defineProperty(this, \"$typeArguments$Box\", {");
        await Assert.That(printed).Contains("value: $typeArgs");
        await Assert.That(printed).Contains("enumerable: false");
    }
}
