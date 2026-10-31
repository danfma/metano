using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Escape-hatch table for BCL call sites whose shape the declarative
/// <c>[MapMethod]</c> attribute can't express (today: <c>decimal</c>
/// constructor wrapping, <c>System.Math</c> static helpers that lower
/// to the receiver's instance method, and overloads normalized to a
/// canonical overload the registry can tell apart by argument count —
/// see the <c>OverloadNormalization</c> partial). Symbol-keyed via
/// <see cref="SymbolEqualityComparer.Default"/> so dispatch is O(1)
/// per call site and adding the next BCL form is one entry, not one
/// rewriter class.
/// <para>
/// <b>Not a parallel mechanism to
/// <see cref="Metano.Compiler.Mappings.DeclarativeMappingRegistry"/>.</b>
/// The declarative registry remains the front door — every mapping
/// that CAN be expressed as <c>[MapMethod]</c> belongs there. This
/// table is the escape hatch for shapes the attribute system can't
/// model: shapes that need runtime construction
/// (<c>decimal.Parse(s) → new Decimal(s)</c>), receiver-relocation
/// (<c>Math.Floor(x) → x.floor()</c>) or overload selection the
/// registry's argument-count filter cannot make on its own.
/// </para>
/// <para>
/// Called <c>Table</c> rather than <c>Registry</c> because entries
/// are seeded once per compilation in the constructor and never
/// registered externally — there is no public mutation API. The set
/// is closed; new BCL forms land as a new private <c>Seed…</c>
/// method here, not as a runtime registration call.
/// </para>
/// </summary>
/// <seealso cref="Metano.Compiler.Mappings.DeclarativeMappingRegistry"/>
public sealed partial class IntrinsicBclLoweringTable : IInvocationRewriter
{
    private readonly Dictionary<
        IMethodSymbol,
        Func<InvocationRewriteContext, IrExpression>
    > _entries;

    private readonly InvocationLoweringHelpers _helpers;

    /// <summary>
    /// Builds the per-compilation table. The Roslyn semantic model
    /// resolves built-in methods (<c>decimal.Parse</c>, <c>Math.Round</c>)
    /// to symbols whose identity is stable inside one compilation
    /// but not across — the table is rebuilt per
    /// <see cref="IrExpressionExtractor"/> instance so the lookup
    /// keys always belong to the active compilation. The
    /// <paramref name="helpers"/> façade supplies origin building and
    /// type mapping to the overload-normalizing entries.
    /// </summary>
    public IntrinsicBclLoweringTable(Compilation compilation, InvocationLoweringHelpers helpers)
    {
        _helpers = helpers;
        _entries = new Dictionary<IMethodSymbol, Func<InvocationRewriteContext, IrExpression>>(
            SymbolEqualityComparer.Default
        );
        SeedDecimalParse(compilation);
        SeedMathDecimal(compilation);
        SeedRounding(compilation);
        SeedGetValueOrDefault(compilation);
        SeedStringJoin(compilation);
    }

    /// <summary>
    /// Looks the call site up by its unreduced definition, so an
    /// extension call written as <c>dict.GetValueOrDefault(key)</c>
    /// matches the entry seeded for the static
    /// <c>CollectionExtensions.GetValueOrDefault</c> definition.
    /// </summary>
    public IrExpression? Rewrite(InvocationRewriteContext context)
    {
        if (context.Symbol is null)
            return null;

        var definition = (context.Symbol.ReducedFrom ?? context.Symbol).OriginalDefinition;

        return _entries.TryGetValue(definition, out var lower) ? lower(context) : null;
    }

    /// <summary>
    /// <c>decimal.Parse(s)</c> → <c>new Decimal(s)</c>. Covers every
    /// <c>Parse</c> overload — the runtime constructor accepts any
    /// of the BCL argument shapes (string, span, IFormatProvider).
    /// </summary>
    private void SeedDecimalParse(Compilation compilation)
    {
        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        foreach (
            var method in decimalType
                .GetMembers("Parse")
                .OfType<IMethodSymbol>()
                .Where(m => m.IsStatic && m.Parameters.Length >= 1)
        )
        {
            _entries[method.OriginalDefinition] = ctx =>
            {
                var arg = ctx.Recurse(ctx.Invocation.ArgumentList.Arguments[0].Expression);
                return new IrNewExpression(
                    new IrPrimitiveTypeRef(IrPrimitive.Decimal),
                    [new IrArgument(arg)]
                );
            };
        }
    }

    /// <summary>
    /// <c>Math.{Floor,Ceiling,Truncate,Abs}(decimal)</c> → receiver's
    /// instance method (<c>x.floor()</c> et al). <c>Math.Round(decimal)</c>
    /// is normalized to <c>decimal.Round</c> by <see cref="SeedRounding"/>
    /// instead, because decimal.js <c>round()</c> resolves a midpoint away
    /// from zero rather than to even. Decimal.js exposes
    /// the rounding family per-instance because <c>Math</c> in
    /// JavaScript only consumes <c>number</c>; routing through the
    /// receiver keeps the lowered call type-safe at the JS layer.
    /// </summary>
    private void SeedMathDecimal(Compilation compilation)
    {
        var mathType = compilation.GetTypeByMetadataName("System.Math");
        if (mathType is null)
            return;

        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);

        foreach (var (csharpName, jsName) in MathDecimalNames)
        {
            foreach (
                var method in mathType
                    .GetMembers(csharpName)
                    .OfType<IMethodSymbol>()
                    .Where(m =>
                        m.IsStatic
                        && m.Parameters.Length == 1
                        && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, decimalType)
                    )
            )
            {
                var receiverMethod = jsName;
                _entries[method.OriginalDefinition] = ctx =>
                {
                    var argSyntax = ctx.Invocation.ArgumentList.Arguments[0].Expression;
                    return new IrCallExpression(
                        new IrMemberAccess(ctx.Recurse(argSyntax), receiverMethod),
                        []
                    );
                };
            }
        }
    }

    private static readonly (string CSharp, string Js)[] MathDecimalNames =
    {
        ("Floor", "floor"),
        ("Ceiling", "ceil"),
        ("Truncate", "trunc"),
        ("Abs", "abs"),
    };
}
