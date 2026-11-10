namespace Metano.Tests;

/// <summary>
/// The exact <c>MS0063</c> texts, shared by the test classes that assert
/// them.
/// </summary>
internal static class AmbiguousDispatchMessages
{
    private const string TranspiledCalls =
        "calls transpiled from C# in this compilation use the overload's own fast path and are "
        + "not affected.";

    private const string BrandedOperatorApplications =
        "an operator or conversion of a branded type applied in C# is not lowered to these "
        + "functions (#599).";

    /// <summary>The branded 64-bit ambiguity of a constructor, method or function group.</summary>
    public static string BrandedSixtyFourBit(string members) =>
        BrandedSixtyFourBit(members, TranspiledCalls);

    /// <summary>The same ambiguity in an operator or conversion group of a branded type.</summary>
    public static string BrandedSixtyFourBitOperators(string members) =>
        BrandedSixtyFourBit(members, BrandedOperatorApplications);

    /// <summary>A catch-all overload tried before the concrete one it shadows.</summary>
    public static string CatchAll(string catchAll, string shadowed) =>
        $"Overload '{catchAll}' accepts any argument at runtime, so the TypeScript dispatcher "
        + $"tries it before '{shadowed}', whose argument counts it overlaps: a call C# binds to "
        + "one of them can reach it instead (ambiguous runtime dispatch). Static call-site "
        + "dispatch (#25) will resolve it; until then, give the overloads distinct arities or "
        + "constrain the type parameter.";

    private static string BrandedSixtyFourBit(string members, string transpiledCallsNote) =>
        $"Overloads {members} share one runtime test: a branded 64-bit type ([Branded] or "
        + "[InlineWrapper] over long, ulong, nint, nuint or BigInteger) lowers to a bigint, so "
        + "its dispatcher arm accepts every value the long, ulong and BigInteger arms accept. "
        + "A call that reaches the dispatcher (hand-written TypeScript, or a consumer reading "
        + $"this type from a DLL or package) can run another overload than the one C# binds; "
        + $"{transpiledCallsNote} To make the dispatcher unambiguous, give the overloads distinct "
        + "arities or a distinct non-64-bit parameter.";
}
