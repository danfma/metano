using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Read-only façade onto the extractor's pure helper methods so
/// rewriters can build IR shapes (origins, params-spread tagging,
/// delegate-invoke probes) without holding a reference to the
/// extractor's mutable surface. The façade holds no mutable state of
/// its own — the delegates it captures are bound to extractor
/// instance methods, but any rewriter-specific mutable state (e.g.
/// inline-expansion recursion guards) lives on the rewriter that
/// owns it, never on this struct.
/// </summary>
/// <param name="ApplyParamsSpread">
/// Tags the trailing <see cref="IrArgument"/> with
/// <c>IsSpread = true</c> when a <c>params T[]</c> receives a
/// single <c>T[]</c>-typed argument at the call site. Bound to the
/// extractor's instance method so the semantic-model lookup stays
/// inside the extractor.
/// </param>
/// <param name="BuildOrigin">
/// Builds the cross-target <see cref="IrMemberOrigin"/> for a
/// symbol — Name overrides, External / NoContainer flags, enum
/// kinds, plain-object-instance method classification. Bound to the
/// extractor instance because origin building reads
/// <c>_target</c> for <c>[Name(Target, …)]</c> resolution.
/// </param>
/// <param name="NormalizeArguments">
/// Reorders named arguments into parameter-declaration order and
/// fills explicit-default slots for skipped optional parameters.
/// Bound to the extractor's instance method because the
/// normalization reads symbol metadata that the rewriters never
/// need to know about directly.
/// </param>
/// <param name="MapTypeRef">
/// Maps a Roslyn <see cref="ITypeSymbol"/> to the target's
/// <see cref="IrTypeRef"/> using the extractor's origin resolver
/// and active target language. Bound to the extractor instance
/// because the resolver + target fields are immutable but
/// extractor-owned.
/// </param>
/// <param name="Target">
/// The active <see cref="TargetLanguage"/> (or <see langword="null"/>
/// when the extractor runs target-agnostic). Rewriters that read
/// <c>[Name(Target, …)]</c> overrides forward this to
/// <see cref="SymbolHelper.GetNameOverride"/>.
/// </param>
// 5-field soft ceiling: `ApplyParamsSpread`/`BuildOrigin`/`NormalizeArguments`
// are argument-shape helpers; `MapTypeRef`/`Target` are target-resolution
// services. Adding a sixth field should trigger a split into two structs
// (e.g. `ArgumentShapeHelpers` + `TargetResolutionContext`) before the
// façade drifts into a god-interface.
public readonly record struct InvocationLoweringHelpers(
    Action<List<IrArgument>, IMethodSymbol?, SeparatedSyntaxList<ArgumentSyntax>> ApplyParamsSpread,
    Func<ISymbol?, IrMemberOrigin?> BuildOrigin,
    Func<IReadOnlyList<IrArgument>, IMethodSymbol, IReadOnlyList<IrArgument>> NormalizeArguments,
    Func<ITypeSymbol, IrTypeRef> MapTypeRef,
    TargetLanguage? Target
)
{
    /// <summary>
    /// True when <paramref name="method"/> is a delegate's synthetic
    /// <c>Invoke</c> member — the runtime shape JS / TS lowers to a
    /// plain call without the <c>.Invoke</c> indirection.
    /// </summary>
    public static bool IsDelegateInvoke(IMethodSymbol method) =>
        method.MethodKind == MethodKind.DelegateInvoke;

    /// <summary>
    /// True when the delegate's first parameter carries
    /// <c>[This]</c> — the receiver is rebound as JS <c>this</c> at
    /// the call site, so the simple <c>handler(args)</c> shortcut
    /// must NOT apply; the existing <c>delegate.call(receiver, …)</c>
    /// rewrite handles that case downstream.
    /// </summary>
    public static bool HasThisParameter(IMethodSymbol method) =>
        method.Parameters.Length > 0 && SymbolHelper.HasThis(method.Parameters[0]);
}
