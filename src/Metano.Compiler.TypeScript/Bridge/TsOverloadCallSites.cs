using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The claim results a generated call site resolves an overload identity
/// through (spec 013 DECIDED-18): a <c>new</c> lowered to a factory and a
/// <c>: base(...)</c> lowered to a selector spell the names the declaring
/// class's own emission claimed, in whichever file or package the call is.
/// <para>
/// <c>TypeTransformer</c> publishes the transform's
/// <see cref="TsOverloadClaimsRegistry"/> for the duration of one transform,
/// as it publishes its other per-transform lowering state. Every bridge that
/// lowers an expression reaches it — class members, module functions,
/// operators, plain objects, entry points — without threading it through
/// each. <see cref="AsyncLocal{T}"/> keeps parallel transforms apart; outside
/// a transform nothing resolves, so a call keeps its dispatcher form.
/// </para>
/// </summary>
internal static class TsOverloadCallSites
{
    private static readonly AsyncLocal<TsOverloadClaimsRegistry?> _claims = new();

    /// <summary>The claim results of the running transform; <c>null</c> outside one.</summary>
    internal static TsOverloadClaimsRegistry? Claims
    {
        get => _claims.Value;
        set => _claims.Value = value;
    }

    /// <summary>
    /// The claimed selector and factory of a constructor overload;
    /// <c>null</c> outside a transform or when the declaring type is not
    /// visible to it.
    /// </summary>
    public static TsConstructorOverloadNames? ConstructorNamesOf(IrOverloadRef overload) =>
        Claims?.ConstructorNamesOf(overload);
}
