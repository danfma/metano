using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Normalizes the <c>Math.Round</c> / <c>decimal.Round</c> family to an
/// overload the declarative <c>[MapMethod]</c> registry can tell apart by
/// argument count: <c>Round(x, digits)</c> and <c>Round(x, mode)</c> both take
/// two arguments, so the mode-only form gains an explicit zero digit count.
/// The output stays target-agnostic IR; the backends lower the canonical call
/// through their own mappings.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>
    /// <list type="bullet">
    ///   <item><c>Math.Round(double, MidpointRounding)</c> →
    ///   <c>Math.Round(double, 0, MidpointRounding)</c>.</item>
    ///   <item><c>Math.Round(decimal…)</c> → <c>decimal.Round(decimal…)</c>,
    ///   whose <c>(decimal, MidpointRounding)</c> overload becomes
    ///   <c>(decimal, 0, MidpointRounding)</c> as well.</item>
    /// </list>
    /// </summary>
    private void SeedRounding(Compilation compilation)
    {
        var mathType = compilation.GetTypeByMetadataName("System.Math");
        var midpointType = compilation.GetTypeByMetadataName("System.MidpointRounding");
        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        var anyDecimalRoundOverload = RoundOverloads(decimalType).FirstOrDefault();

        if (mathType is null || midpointType is null || anyDecimalRoundOverload is null)
            return;

        var doubleType = compilation.GetSpecialType(SpecialType.System_Double);

        foreach (var method in RoundOverloads(mathType).Where(m => TakesValueOf(m, decimalType)))
        {
            _entries[method.OriginalDefinition] = ctx =>
                BuildRoundCall(ctx, anyDecimalRoundOverload, midpointType);
        }

        foreach (
            var method in RoundOverloads(mathType)
                .Where(m => TakesValueOf(m, doubleType) && TakesModeWithoutDigits(m, midpointType))
        )
        {
            _entries[method.OriginalDefinition] = ctx => BuildRoundCall(ctx, method, midpointType);
        }

        foreach (
            var method in RoundOverloads(decimalType)
                .Where(m => TakesModeWithoutDigits(m, midpointType))
        )
        {
            _entries[method.OriginalDefinition] = ctx => BuildRoundCall(ctx, method, midpointType);
        }
    }

    /// <summary>
    /// Rebuilds the call against <paramref name="canonical"/>'s declaring
    /// type, inserting a zero digit count ahead of the mode when the source
    /// overload takes a mode but no digits. Any overload of the canonical
    /// type works: the backends key their mapping on declaring type and name,
    /// and the argument count picks the overload there.
    /// </summary>
    private IrExpression BuildRoundCall(
        InvocationRewriteContext ctx,
        IMethodSymbol canonical,
        ITypeSymbol midpointType
    )
    {
        var arguments = ArgumentValues(ctx, BoundInvocation(ctx));

        if (TakesModeWithoutDigits(ctx.Symbol!, midpointType))
        {
            arguments.Insert(1, new IrLiteral(0, IrLiteralKind.Int32));
        }

        var target = new IrMemberAccess(
            new IrTypeReference(canonical.ContainingType.Name),
            canonical.Name
        );

        return new IrCallExpression(
            target,
            arguments.Select(a => new IrArgument(a)).ToList(),
            Origin: _helpers.BuildOrigin(canonical)
        );
    }

    private static IEnumerable<IMethodSymbol> RoundOverloads(INamedTypeSymbol type) =>
        type.GetMembers("Round").OfType<IMethodSymbol>().Where(m => m.IsStatic);

    private static bool TakesValueOf(IMethodSymbol method, ITypeSymbol valueType) =>
        SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, valueType);

    private static bool TakesModeWithoutDigits(IMethodSymbol method, ITypeSymbol midpointType) =>
        method.Parameters.Length == 2
        && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, midpointType);
}
