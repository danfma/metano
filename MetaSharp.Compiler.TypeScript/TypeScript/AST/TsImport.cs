namespace MetaSharp.TypeScript.AST;

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
/// </summary>
public sealed record TsImport(
    string[] Names,
    string From,
    bool TypeOnly = false,
    bool IsDefault = false) : TsTopLevel;
