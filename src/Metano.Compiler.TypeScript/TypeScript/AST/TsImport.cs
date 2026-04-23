namespace Metano.TypeScript.AST;

/// <summary>
/// A TypeScript import statement.
/// <list type="bullet">
///   <item><see cref="IsDefault"/> = false → <c>import { A, B } from "from";</c>
///   (named imports). <see cref="Names"/> can list one or many.</item>
///   <item><see cref="IsDefault"/> = true → <c>import A from "from";</c> (default
///   import). <see cref="Names"/> must contain exactly one entry.</item>
/// </list>
/// Combine with <see cref="TypeOnly"/> for <c>import type</c> form. The two flags can
/// also combine: <c>import type A from "from";</c>.
///
/// <para><see cref="TypeOnlyNames"/> is a per-name set of names that should be
/// emitted with the inline <c>type</c> qualifier — used when <see cref="TypeOnly"/>
/// is false but some names are types and others are values, producing
/// <c>import { Foo, type Bar } from "from";</c>. When <see cref="TypeOnly"/> is true
/// the per-name set is irrelevant (the whole statement is type-only). The set
/// must be a subset of <see cref="Names"/>.</para>
/// </summary>
public sealed record TsImport(
    string[] Names,
    string From,
    bool TypeOnly = false,
    bool IsDefault = false,
    IReadOnlySet<string>? TypeOnlyNames = null,
    bool IsNamespace = false
) : TsTopLevel;
