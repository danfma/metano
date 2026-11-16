using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Escape-hatch table for BCL call sites whose shape the declarative
/// <c>[MapMethod]</c> attribute can't express (today: <c>decimal</c>
/// constructor wrapping, <c>System.Math</c> static helpers that lower
/// to the receiver's instance method, and overloads normalized to a
/// canonical overload the registry can tell apart by argument count —
/// see the <c>Rounding</c>, <c>GetValueOrDefault</c> and <c>StringJoin</c>
/// partials — and calls retargeted by an argument's static type, which a
/// mapping cannot see once the argument converts to <c>object</c>; see the
/// <c>ObjectEquals</c> partial). Symbol-keyed via
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
    /// <summary>
    /// The arguments a lowered <c>decimal.Parse</c> call passes: the text
    /// alone, whichever culture-free overload was written.
    /// </summary>
    private const int TextParseArgumentCount = 1;

    private readonly Dictionary<
        IMethodSymbol,
        Func<InvocationRewriteContext, IrExpression?>
    > _entries;

    private readonly InvocationLoweringHelpers _helpers;
    private readonly Func<IMethodSymbol, int, bool>? _lowersByDeclarativeMapping;

    /// <summary>
    /// Builds the per-compilation table. The Roslyn semantic model
    /// resolves built-in methods (<c>decimal.Parse</c>, <c>Math.Round</c>)
    /// to symbols whose identity is stable inside one compilation
    /// but not across — the table is rebuilt per
    /// <see cref="IrExpressionExtractor"/> instance so the lookup
    /// keys always belong to the active compilation. The
    /// <paramref name="helpers"/> façade supplies origin building and
    /// type mapping to the overload-normalizing entries;
    /// <paramref name="lowersByDeclarativeMapping"/> is the target port
    /// (<see cref="IrExtractionContext.LowersByDeclarativeMapping"/>) through
    /// which a target's own mapping of a method takes precedence over an
    /// entry.
    /// </summary>
    public IntrinsicBclLoweringTable(
        Compilation compilation,
        InvocationLoweringHelpers helpers,
        Func<IMethodSymbol, int, bool>? lowersByDeclarativeMapping = null
    )
    {
        _helpers = helpers;
        _lowersByDeclarativeMapping = lowersByDeclarativeMapping;

        _entries = new Dictionary<IMethodSymbol, Func<InvocationRewriteContext, IrExpression?>>(
            SymbolEqualityComparer.Default
        );

        SeedDecimalParse(compilation);
        SeedMathDecimal(compilation);
        SeedRounding(compilation);
        SeedGetValueOrDefault(compilation);
        SeedListFind(compilation);
        SeedStringJoin(compilation);
        SeedHashCode(compilation);
        SeedTupleEquality(compilation);
    }

    /// <summary>
    /// Looks the call site up by its unreduced definition, so an
    /// extension call written as <c>dict.GetValueOrDefault(key)</c>
    /// matches the entry seeded for the static
    /// <c>CollectionExtensions.GetValueOrDefault</c> definition, and an
    /// override by the method it overrides at the root of its chain, so
    /// <c>record.GetHashCode()</c> matches the entry seeded for
    /// <c>object.GetHashCode</c>. An entry may still decline (return
    /// <c>null</c>).
    /// </summary>
    public IrExpression? Rewrite(InvocationRewriteContext context)
    {
        if (context.Symbol is null)
            return null;

        var definition = RootDefinition(context.Symbol.ReducedFrom ?? context.Symbol);

        return _entries.TryGetValue(definition, out var lower) ? lower(context) : null;
    }

    /// <summary>
    /// The open definition of the method <paramref name="method"/> overrides
    /// at the root of its override chain (itself when it overrides nothing).
    /// </summary>
    private static IMethodSymbol RootDefinition(IMethodSymbol method)
    {
        var root = method.OriginalDefinition;

        while (root.OverriddenMethod is { } overridden)
            root = overridden.OriginalDefinition;

        return root;
    }

    /// <summary>
    /// <c>decimal.Parse(s)</c> → <c>new Decimal(s)</c>. Covers every
    /// <c>Parse</c> overload — the runtime constructor accepts any
    /// of the BCL argument shapes (string, span, IFormatProvider).
    /// TODO(#549): this shape is decimal.js's, a TypeScript fact in the core. A target
    /// that maps <c>decimal.Parse</c> declaratively keeps the call for its
    /// mapping of the culture-free overloads, <c>(string)</c> and
    /// <c>(string, IFormatProvider)</c>, which the entry binds to the
    /// <c>(string)</c> one (TypeScript checks .NET's <c>NumberStyles.Number</c>
    /// grammar and throws <c>FormatException</c> through a runtime helper,
    /// #651); a <c>NumberStyles</c> or span overload keeps <c>new Decimal(s)</c>,
    /// whose grammar (decimal.js) accepts exponents as <c>NumberStyles.Float</c> does.
    /// </summary>
    private void SeedDecimalParse(Compilation compilation)
    {
        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var formatProvider = compilation.GetTypeByMetadataName("System.IFormatProvider");

        var parseOverloads = decimalType
            .GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .Where(m => m.IsStatic && m.Parameters.Length >= 1)
            .ToList();

        var textParse = parseOverloads.FirstOrDefault(m => TakesOnly(m, stringType));

        // The target's own mapping of `decimal.Parse(string)` takes the
        // culture-free overloads; asked once, as it does not depend on the call.
        var mapsTextParse =
            textParse is not null
            && _lowersByDeclarativeMapping?.Invoke(textParse, TextParseArgumentCount) is true;

        foreach (var method in parseOverloads)
        {
            var isCultureFree =
                TakesOnly(method, stringType) || TakesOnly(method, stringType, formatProvider);

            _entries[method.OriginalDefinition] =
                mapsTextParse && isCultureFree
                    ? context => TextParseCall(context, textParse!)
                    : DecimalConstruction;
        }
    }

    /// <summary>
    /// <c>decimal.Parse(text)</c> bound to the <c>(string)</c> overload: an
    /// <c>IFormatProvider</c> argument is dropped unextracted, as the
    /// invariant culture is the only one Metano formats in.
    /// </summary>
    private IrExpression TextParseCall(InvocationRewriteContext context, IMethodSymbol textParse)
    {
        var text = context.Recurse(context.Invocation.ArgumentList.Arguments[0].Expression);

        return new IrCallExpression(
            new IrMemberAccess(DecimalReceiver(context, textParse), textParse.Name),
            [new IrArgument(text)],
            Origin: _helpers.BuildOrigin(textParse)
        );
    }

    /// <summary>
    /// The receiver of a <c>decimal.Parse</c> call: the written one, extracted
    /// for the BCL export's origin, so a project type of the same name does not
    /// bind it (issue #477), under the type's own name, so a
    /// <c>using D = System.Decimal;</c> alias does not name the import.
    /// </summary>
    private static IrTypeReference DecimalReceiver(
        InvocationRewriteContext context,
        IMethodSymbol textParse
    )
    {
        var typeName = textParse.ContainingType.Name;

        var written = context.Invocation.Expression is MemberAccessExpressionSyntax access
            ? context.Recurse(access.Expression)
            : null;

        return written is IrTypeReference reference
            ? reference with
            {
                Name = typeName,
            }
            : new IrTypeReference(typeName);
    }

    private static IrExpression DecimalConstruction(InvocationRewriteContext context)
    {
        var argument = context.Recurse(context.Invocation.ArgumentList.Arguments[0].Expression);

        return new IrNewExpression(
            new IrPrimitiveTypeRef(IrPrimitive.Decimal),
            [new IrArgument(argument)]
        );
    }

    private static bool TakesOnly(IMethodSymbol method, params ITypeSymbol?[] parameterTypes) =>
        method.Parameters.Length == parameterTypes.Length
        && method
            .Parameters.Zip(parameterTypes)
            .All(pair => SymbolEqualityComparer.Default.Equals(pair.First.Type, pair.Second));

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
