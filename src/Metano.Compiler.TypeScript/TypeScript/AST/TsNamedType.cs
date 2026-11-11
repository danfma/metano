namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A named TypeScript type reference. <see cref="Origin"/> is non-null when the type
/// comes from a cross-package source — the type mapper attaches it at construction
/// time so the import collector can emit the right <c>import { Name } from "..."</c>
/// without re-resolving by name (avoiding ambiguity when two assemblies share simple
/// type names).
/// <para>
/// <see cref="RuntimeHelperBinding"/> is non-null when the named type is a
/// class-shaped <c>metano-runtime</c> helper (<c>HashSet&lt;T&gt;</c>,
/// <c>UUID</c>, <c>Grouping&lt;K, V&gt;</c>): the root binding prints deferred
/// like every helper reference, so the type agrees with the aliased import
/// when a user declaration claims the name (issue #484). The rest of a dotted
/// name stays compiler-authored.
/// </para>
/// <para>
/// <see cref="InstanceofTarget"/> is non-null when the bridge resolved the
/// value a runtime test of the type checks with <c>instanceof</c> (a BCL
/// exception's runtime class, the built-in <c>Error</c> for
/// <c>System.Exception</c>), so a structural guard tests it rather than
/// deciding again.
/// </para>
/// </summary>
public sealed record TsNamedType(
    string Name,
    IReadOnlyList<TsType>? TypeArguments = null,
    TsTypeOrigin? Origin = null,
    string? OriginalName = null,
    string? RuntimeHelperBinding = null,
    TsExpression? InstanceofTarget = null
) : TsType;
