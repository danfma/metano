using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript fast-path name of one overload of a dispatched group: the
/// group's emitted base name plus the suffix the core's naming policy computed
/// (spec 013 FR-001, contracts/naming.md § Stage 1, step 8): <c>add</c> +
/// <c>XY</c>, <c>now</c> + <c>Default</c>, <c>__add</c> + <c>MoneyInt32</c>.
/// </summary>
internal static class OverloadFastPathName
{
    public static string Of(string baseName, IrMethodDeclaration overload) =>
        baseName + SuffixOf(overload);

    private static string SuffixOf(IrMethodDeclaration overload) =>
        overload.OverloadName?.Suffix
        ?? throw new InvalidOperationException(
            $"Overload '{overload.Name}' of a dispatched group carries no overload name."
        );
}
