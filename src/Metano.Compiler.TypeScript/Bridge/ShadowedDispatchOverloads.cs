using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Finds the overloads of a dispatcher whose runtime test is the same
/// JavaScript as the test of an overload tried before it (#778): two C#
/// signatures that lower to the same shape (<c>Func&lt;Action, T&gt;</c> and
/// <c>Action&lt;Action&gt;</c> are both a one-parameter function;
/// <c>JsArray&lt;T&gt;</c> and <c>T[]</c> are both an array). The later
/// branch can never run, so the dispatcher drops it and the emitter reports
/// the fast path a hand-written TypeScript caller must call instead. C# call
/// sites bind to the fast path directly (spec 013 FR-018) and are unaffected.
/// </summary>
internal static class ShadowedDispatchOverloads
{
    /// <summary>
    /// Each overload of <paramref name="sorted"/> (in dispatch order) whose
    /// test repeats another one's, with the overload that takes its calls: the
    /// earlier one, unless only the later one is <paramref name="preferred"/>
    /// (see <see cref="TakesValueReturningDelegate"/>).
    /// </summary>
    public static IReadOnlyList<ShadowedOverload<TOverload>> Find<TOverload>(
        IEnumerable<TOverload> sorted,
        Func<TOverload, TsExpression> testOf,
        Func<TOverload, bool> preferred
    )
        where TOverload : notnull
    {
        var printer = new Printer();
        var firstByTest = new Dictionary<string, TOverload>(StringComparer.Ordinal);
        var shadowed = new List<ShadowedOverload<TOverload>>();

        foreach (var overload in sorted)
        {
            var test = printer.PrintExpressionFragment(testOf(overload));

            if (!firstByTest.TryGetValue(test, out var kept))
                firstByTest[test] = overload;
            else if (!preferred(kept) && preferred(overload))
                shadowed.Add(Replace(firstByTest, test, kept, overload, shadowed));
            else
                shadowed.Add(new ShadowedOverload<TOverload>(overload, kept));
        }

        return shadowed;
    }

    /// <summary>
    /// Whether an overload takes a delegate that returns a value: of two
    /// overloads that differ only by delegate return type
    /// (<c>Func&lt;Action, T&gt;</c> and <c>Action&lt;Action&gt;</c>), the
    /// dispatcher keeps the one whose callback's result is not dropped.
    /// </summary>
    public static bool TakesValueReturningDelegate(IReadOnlyList<IrParameter> parameters) =>
        parameters.Any(parameter => ReturnsValue(parameter.Type));

    private static bool ReturnsValue(IrTypeRef type) =>
        type switch
        {
            IrNullableTypeRef nullable => ReturnsValue(nullable.Inner),
            IrFunctionTypeRef function => function.ReturnType
                is not IrPrimitiveTypeRef { Primitive: IrPrimitive.Void },
            _ => false,
        };

    /// <summary>
    /// Makes <paramref name="preferred"/> the overload kept for
    /// <paramref name="test"/>: <paramref name="replaced"/>, and every overload
    /// it took, go to it.
    /// </summary>
    private static ShadowedOverload<TOverload> Replace<TOverload>(
        Dictionary<string, TOverload> firstByTest,
        string test,
        TOverload replaced,
        TOverload preferred,
        List<ShadowedOverload<TOverload>> shadowed
    )
        where TOverload : notnull
    {
        firstByTest[test] = preferred;

        for (var index = 0; index < shadowed.Count; index++)
        {
            if (EqualityComparer<TOverload>.Default.Equals(shadowed[index].TakenBy, replaced))
                shadowed[index] = shadowed[index] with { TakenBy = preferred };
        }

        return new ShadowedOverload<TOverload>(replaced, preferred);
    }
}

/// <summary>An overload no dispatcher call reaches, and the overload whose identical test takes its calls.</summary>
internal sealed record ShadowedOverload<TOverload>(TOverload Overload, TOverload TakenBy);
