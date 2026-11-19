using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Transformation;
using Metano.Compiler.TypeScript.Transformation.Json;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>
/// <c>MS0047</c> (spec 011 FR-020): a test of a <c>[PlainObject]</c> type is
/// structural, so another plain object whose values the test's static type can
/// hold, and that has every member the tested guard requires, tests as the
/// tested type too. Reported once per (tested type, overlapping type) pair,
/// over the frontend's runtime questions, sequentially before the group
/// transforms, so a warm group cache keeps it. The <i>covers</i> relation is
/// spec 009 FR-019 rule 3's (<see cref="JsonStructuralVariants.Covers"/>).
/// </summary>
internal sealed class PlainObjectOverlapReporter(
    TypeScriptTransformContext context,
    IReadOnlyList<INamedTypeSymbol> emittedTypes
)
{
    private readonly HashSet<OverlapPair> _reported = [];

    private readonly IReadOnlyList<INamedTypeSymbol> _plainObjects =
    [
        .. emittedTypes.Where(type =>
            SymbolHelper.HasPlainObject(type) && TypeGuardBuilder.CanHaveGuard(type)
        ),
    ];

    private readonly TypeGuardBuilder _guards = new(context);

    public void Report(IReadOnlyList<IrRuntimeQuestion> questions)
    {
        foreach (var question in questions.Where(IsStructuralTest))
        {
            if (PlainObjectOf(question.Type) is { } tested)
                ReportOverlaps(question, tested);
        }
    }

    private static bool IsStructuralTest(IrRuntimeQuestion question) =>
        question.Kind
            is IrRuntimeQuestionKind.Test
                or IrRuntimeQuestionKind.As
                or IrRuntimeQuestionKind.Cast
                or IrRuntimeQuestionKind.OverloadParameter;

    private INamedTypeSymbol? PlainObjectOf(IrQuestionedType type) =>
        type is { Shape: IrQuestionedTypeShape.Named, Key: { } key }
        && IrTypeKeys.Find(key, context.Compilation) is { } symbol
            ? _plainObjects.FirstOrDefault(plain =>
                SymbolEqualityComparer.Default.Equals(plain, symbol.OriginalDefinition)
            )
            : null;

    private void ReportOverlaps(IrRuntimeQuestion question, INamedTypeSymbol tested)
    {
        var testedMembers = _guards.GuardedMembersOf(tested);

        foreach (var other in _plainObjects)
        {
            if (
                SymbolEqualityComparer.Default.Equals(other, tested)
                || !IsHeldBy(other, question.InputType)
                || !JsonStructuralVariants.Covers(testedMembers, _guards.GuardedMembersOf(other))
                || !_reported.Add(new OverlapPair(KeyOf(tested), KeyOf(other)))
            )
                continue;

            context.ReportDiagnostic(Overlap(question, tested, testedMembers, other));
        }
    }

    /// <summary>
    /// Whether a value of <paramref name="other"/> can reach the test: the
    /// tested value's static type holds it (<c>object</c>, <c>dynamic</c>,
    /// an interface or base it implements); an unknown static type (a
    /// dispatcher's argument) holds anything.
    /// </summary>
    private bool IsHeldBy(INamedTypeSymbol other, IrQuestionedType? inputType) =>
        inputType switch
        {
            null or { Shape: IrQuestionedTypeShape.Object or IrQuestionedTypeShape.Dynamic } =>
                true,
            { Shape: IrQuestionedTypeShape.Named, Key: { } key } => HoldsValueOf(key, other),
            _ => false,
        };

    /// <summary>Whether a value of <paramref name="other"/> converts implicitly to the type <paramref name="holderKey"/> names.</summary>
    private bool HoldsValueOf(IrTypeKey holderKey, INamedTypeSymbol other)
    {
        if (IrTypeKeys.Find(holderKey, context.Compilation) is not { } holder)
            return false;

        var conversion = context.Compilation.ClassifyCommonConversion(other, holder);

        return conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference);
    }

    private static MetanoDiagnostic Overlap(
        IrRuntimeQuestion question,
        INamedTypeSymbol tested,
        IReadOnlyList<GuardedMember> testedMembers,
        INamedTypeSymbol other
    ) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.PlainObjectShapeOverlap,
            $"'{question.Text}' is structural: '{other.Name}' also satisfies the shape "
                + $"{{ {string.Join(", ", testedMembers.Where(m => m.IsRequired).Select(m => m.Name))} }}, "
                + $"so a {other.Name} value tests as {tested.Name} (spec 011 FR-020).",
            question.Span is { } span ? IrSourceSpanLocations.ToLocation(span) : null
        );

    private static SupertypeKey KeyOf(INamedTypeSymbol type) => RuntimeTypeReachability.KeyOf(type);

    /// <summary>A tested type and a plain object that passes its test.</summary>
    private readonly record struct OverlapPair(SupertypeKey Tested, SupertypeKey Other);
}
