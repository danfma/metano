using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests.IR;

/// <summary>
/// The cast state of a file group travels in the bridge context
/// (<see cref="IrToTsBridgeContext.Casts"/>, #685): a cast to a class the
/// group asserts is a TypeScript assertion, any other class is checked with
/// <c>castTo</c> and recorded for the import-graph pass, and outside a group
/// every class is checked and nothing is recorded.
/// </summary>
public class ReferenceCastContextTests
{
    private static readonly IrCastExpression DowncastToCircle = new(
        new IrIdentifier("shape"),
        new IrNamedTypeRef("Circle", "App"),
        ReferenceConversion: new IrReferenceConversion(
            IsNarrowedByTypeTest: false,
            TargetDerivesFromEnclosingType: false,
            InInlineExpansion: false,
            TargetMentionsTypeParameter: false
        )
    );

    [Test]
    public async Task OutsideAGroup_ChecksTheCast()
    {
        var lowered = Lower(IrToTsBridgeContext.For(null));

        await Assert.That(IsChecked(lowered)).IsTrue();
    }

    [Test]
    public async Task InAGroup_ChecksAndRecordsAClassItDoesNotAssert()
    {
        var casts = new GroupCasts(new HashSet<string>(StringComparer.Ordinal));

        var lowered = Lower(
            IrToTsBridgeContext.ForGroup(null, casts, RuntimeTypeReachabilityResult.Empty)
        );

        await Assert.That(IsChecked(lowered)).IsTrue();
        await Assert.That(casts.Checked).IsEquivalentTo(["Circle"]);
    }

    [Test]
    public async Task InAGroup_AssertsAClassItAsserts()
    {
        var casts = new GroupCasts(new HashSet<string>(["Circle"], StringComparer.Ordinal));

        var lowered = Lower(
            IrToTsBridgeContext.ForGroup(null, casts, RuntimeTypeReachabilityResult.Empty)
        );

        await Assert.That(lowered is TsCastExpression).IsTrue();
        await Assert.That(casts.Checked).IsEmpty();
    }

    private static TsExpression Lower(IrToTsBridgeContext context) =>
        IrToTsExpressionBridge.Map(DowncastToCircle, context);

    /// <summary>Whether the cast lowered to <c>castTo&lt;Circle&gt;(shape, Circle)</c>.</summary>
    private static bool IsChecked(TsExpression lowered) =>
        lowered
            is TsCallExpression
            {
                Callee: TsRuntimeHelperReference { Name: ReferenceCastLowering.CastHelper },
                Arguments: [TsIdentifier { Name: "shape" }, TsIdentifier { Name: "Circle" }],
            };
}
