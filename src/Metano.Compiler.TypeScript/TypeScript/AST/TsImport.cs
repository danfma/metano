namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript import statement.
/// <list type="bullet">
///   <item><see cref="IsDefault"/> = false and <see cref="IsNamespace"/> = false →
///   <c>import { A, B } from "from";</c> (named imports).
///   <see cref="Names"/> can list one or many.</item>
///   <item><see cref="IsDefault"/> = true → <c>import A from "from";</c> (default
///   import). <see cref="Names"/> must contain exactly one entry.</item>
///   <item><see cref="IsNamespace"/> = true → <c>import * as Alias from "from";</c>
///   (namespace import). <see cref="Names"/> must contain exactly one entry
///   (the alias); <see cref="IsDefault"/> and <see cref="TypeOnlyNames"/> must
///   stay at their defaults — they don't combine with <c>import * as</c>.
///   <see cref="TypeOnly"/> may combine (<c>import type * as A from "from";</c>).
///   Emitted by <c>--namespace-barrels</c> to bind each subpath under a local
///   alias so the aggregation block can re-export it via
///   <see cref="TsExportImportAlias"/>.</item>
/// </list>
/// Combine with <see cref="TypeOnly"/> for <c>import type</c> form. The two flags can
/// also combine: <c>import type A from "from";</c>.
///
/// <para><see cref="TypeOnlyNames"/> is a per-name set of names that should be
/// emitted with the inline <c>type</c> qualifier — used when <see cref="TypeOnly"/>
/// is false but some names are types and others are values, producing
/// <c>import { Foo, type Bar } from "from";</c>. When <see cref="TypeOnly"/> is true
/// the per-name set is irrelevant (the whole statement is type-only). The set
/// must be a subset of <see cref="Names"/>. Only meaningful when
/// <see cref="IsDefault"/> and <see cref="IsNamespace"/> are both false.</para>
/// </summary>
public sealed record TsImport(
    string[] Names,
    TsModuleSpecifier From,
    bool TypeOnly = false,
    bool IsDefault = false,
    IReadOnlySet<string>? TypeOnlyNames = null,
    bool IsNamespace = false,
    IReadOnlyDictionary<string, string>? Aliases = null,
    IReadOnlyList<TsImportAlias>? ExtraAliases = null
) : TsTopLevel;

/// <summary>
/// A further <c>Original as Local</c> binding of a name a <see cref="TsImport"/>
/// already lists (<c>import { Decimal, Decimal as Money }</c>): one export bound
/// under its own name and a <c>using</c> alias in one file.
/// </summary>
public sealed record TsImportAlias(string Original, string Local, bool TypeOnly);
