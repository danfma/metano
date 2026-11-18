using Metano.Compiler.IR;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests;

/// <summary>
/// The reached-set assertion of spec 011 stage 011b (D29) and the single
/// classification of NFR-004: the bridge fails loudly on a descriptor read the
/// reachability phase did not reach, and the overload dispatcher, the type
/// pattern and the guard builder render one test per type.
/// </summary>
public class RuntimeTypeSingleClassificationTests
{
    private const System.Reflection.BindingFlags BindingFlags =
        System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.Static;

    private static readonly IrTypeRef ObjectType = new IrPrimitiveTypeRef(IrPrimitive.Object);

    private static readonly IrNamedTypeRef Interface = Named("IShape", IrNamedTypeKind.Interface);

    private static readonly IrNamedTypeRef NumericEnum = Named(
        "Color",
        IrNamedTypeKind.NumericEnum
    );

    private static readonly IrNamedTypeRef Class = Named("Circle", IrNamedTypeKind.Class);

    private static readonly IrNamedTypeRef PlainObject = new(
        "Point",
        "Geo",
        Semantics: new IrNamedTypeSemantics(
            IrNamedTypeKind.Record,
            IsTranspilable: true,
            IsPlainObject: true
        )
    );

    [Test]
    [Arguments(nameof(RuntimeTypeDescriptorTests.FactorySource))]
    [Arguments(nameof(RuntimeTypeDescriptorTests.InterfacesSource))]
    [Arguments(nameof(RuntimeTypeDescriptorTests.ReceiversSource))]
    [Arguments(nameof(RuntimeTypeDescriptorTests.FallbacksSource))]
    [Arguments(nameof(RuntimeTypeDescriptorTests.CastsSource))]
    public async Task EveryStageGoldenSource_PassesTheReachedSetAssertion(string sourceName)
    {
        var source = (string)
            typeof(RuntimeTypeDescriptorTests).GetField(sourceName, BindingFlags)!.GetValue(null)!;

        // TransformAll publishes the reachability, so every descriptor read the
        // bridge renders runs the assertion; a drift throws.
        var files = TranspileHelper.Transpile(source);

        await Assert.That(files).IsNotEmpty();
    }

    [Test]
    public async Task DescriptorReadOutsideTheReachedSet_FailsLoudly() =>
        await Assert
            .That(() =>
                IrToTsRuntimeTypeReferences.Of(
                    Interface,
                    "a test",
                    RuntimeTypeReachabilityResult.Empty
                )
            )
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Geo.IShape");

    [Test]
    public async Task DescriptorReadInsideTheReachedSet_RendersTheExport()
    {
        var reachability = RuntimeTypeReachabilityResult.Empty with
        {
            Reached = new HashSet<SupertypeKey> { Interface.SupertypeKey },
        };

        var descriptor = IrToTsRuntimeTypeReferences.Of(Interface, "a test", reachability)!;

        await Assert.That(Print(descriptor)).IsEqualTo("IShape$runtimeType");
    }

    /// <summary>
    /// NFR-004: for the same type, the dispatcher (no static type: the
    /// argument may be anything) and the type pattern over an <c>object</c>
    /// render the identical test.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(ClassifiedTypes))]
    public async Task DispatcherAndPattern_RenderTheSameTest(string label, IrTypeRef type)
    {
        var value = new TsIdentifier("value");

        var reached = RuntimeTypeReachabilityResult.Empty with
        {
            Reached = new HashSet<SupertypeKey> { Interface.SupertypeKey },
        };

        var dispatcher = IrTypeCheckBuilder.Generate(
            type,
            value,
            TypeCheckScope.Empty with
            {
                RuntimeTypes = reached,
            }
        );

        var pattern = IrToTsExpressionBridge.BuildTypeTest(
            value,
            type,
            ObjectType,
            IrToTsBridgeContext.ForGroup(null, new GroupCasts(new HashSet<string>()), reached)
        );

        await Assert.That(Print(dispatcher!)).IsEqualTo(Print(pattern));
        await Assert.That(label).IsNotEmpty();
    }

    /// <summary>
    /// NFR-004 for a <c>[PlainObject]</c> shape (FR-020): the dispatcher, the
    /// type pattern and a structural JSON case (which stores the test as a
    /// function, <see cref="IrToTsRuntimeTestBridge.TestFunction"/>) render the
    /// same synthesized guard call.
    /// </summary>
    [Test]
    public async Task DispatcherPatternAndJsonCase_RenderTheSameShapeTest()
    {
        var value = new TsIdentifier("value");

        var reached = RuntimeTypeReachabilityResult.Empty with
        {
            GuardedPlainObjects = new HashSet<SupertypeKey> { PlainObject.SupertypeKey },
        };

        var dispatcher = IrTypeCheckBuilder.Generate(
            PlainObject,
            value,
            TypeCheckScope.Empty with
            {
                RuntimeTypes = reached,
            }
        );

        var pattern = IrToTsExpressionBridge.BuildTypeTest(
            value,
            PlainObject,
            ObjectType,
            IrToTsBridgeContext.ForGroup(null, new GroupCasts(new HashSet<string>()), reached)
        );

        var jsonCase = new TsCallExpression(
            IrToTsRuntimeTestBridge.TestFunction(PlainObject, reached)!,
            [value]
        );

        await Assert.That(Print(pattern)).IsEqualTo("isPoint(value)");
        await Assert.That(Print(dispatcher!)).IsEqualTo(Print(pattern));
        await Assert.That(Print(jsonCase)).IsEqualTo(Print(pattern));
    }

    [Test]
    public async Task AGuardReadOutsideTheReachedSet_FailsLoudly() =>
        await Assert
            .That(() =>
                IrToTsRuntimeTestBridge.Render(
                    new TsIdentifier("value"),
                    PlainObject,
                    ObjectType,
                    RuntimeTypeReachabilityResult.Empty
                )
            )
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Geo.Point");

    /// <summary>
    /// The guard builder's structural checks are the runtime-test bridge's: a
    /// fixed tuple and a <c>typeof</c> test print the same through both.
    /// </summary>
    [Test]
    public async Task GuardAndPattern_ShareTheFixedTupleTest()
    {
        var value = new TsIdentifier("value");
        var tuple = Named("Pair", IrNamedTypeKind.Record) with
        {
            Semantics = new IrNamedTypeSemantics(
                IrNamedTypeKind.Record,
                IsTranspilable: true,
                IsJsTuple: true,
                JsTupleArity: 2
            ),
        };

        await Assert
            .That(
                Print(
                    IrToTsExpressionBridge.BuildTypeTest(
                        value,
                        tuple,
                        staticType: null,
                        IrToTsBridgeContext.For(null)
                    )
                )
            )
            .IsEqualTo(Print(IrToTsRuntimeTestBridge.FixedTupleTest(value, 2)));

        await Assert
            .That(Print(IrToTsRuntimeTestBridge.TypeofTest(value, "string")))
            .IsEqualTo("typeof value === \"string\"");
    }

    public static IEnumerable<Func<(string, IrTypeRef)>> ClassifiedTypes()
    {
        yield return () => ("interface", Interface);
        yield return () => ("enum", NumericEnum);
        yield return () => ("class", Class);
        yield return () => ("int", new IrPrimitiveTypeRef(IrPrimitive.Int32));
        yield return () => ("long", new IrPrimitiveTypeRef(IrPrimitive.Int64));
        yield return () => ("double", new IrPrimitiveTypeRef(IrPrimitive.Float64));
        yield return () => ("string", new IrPrimitiveTypeRef(IrPrimitive.String));
        yield return () => ("bool", new IrPrimitiveTypeRef(IrPrimitive.Boolean));
        yield return () => ("char", new IrPrimitiveTypeRef(IrPrimitive.Char));
        yield return () => ("decimal", new IrPrimitiveTypeRef(IrPrimitive.Decimal));
    }

    private static IrNamedTypeRef Named(string name, IrNamedTypeKind kind) =>
        new(name, "Geo", Semantics: new IrNamedTypeSemantics(kind, IsTranspilable: true));

    private static string Print(TsExpression expression) =>
        new Printer().PrintExpressionFragment(expression);
}
