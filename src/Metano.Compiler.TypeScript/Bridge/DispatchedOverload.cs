using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What the core recorded on every overload of a dispatched group (spec 013
/// FR-001): its suffix and its C# signature. Reading either from a declaration
/// outside a dispatched group is a pipeline bug, so it throws.
/// </summary>
internal static class DispatchedOverload
{
    public static string SuffixOf(IrMethodDeclaration overload) =>
        Required(overload.OverloadName?.Suffix, overload.Name, "an overload name");

    // A dispatched constructor always carries its signature text; the
    // fallback only labels the error raised when it does not.
    public static string SuffixOf(IrConstructorDeclaration overload) =>
        Required(
            overload.OverloadName?.Suffix,
            overload.SignatureText ?? "constructor",
            "an overload name"
        );

    /// <summary>The method as C# declares it: <c>From(decimal amount)</c>.</summary>
    public static string SignatureOf(IrMethodDeclaration overload) =>
        Required(overload.SignatureText, overload.Name, "a signature text");

    /// <summary>The constructor as C# declares it: <c>Money(decimal amount)</c>.</summary>
    public static string SignatureOf(IrConstructorDeclaration overload) =>
        Required(overload.SignatureText, "constructor", "a signature text");

    private static string Required(string? value, string? overload, string what) =>
        value
        ?? throw new InvalidOperationException(
            $"Overload '{overload}' of a dispatched group carries no {what}."
        );
}
