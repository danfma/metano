namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript <c>export import Alias = Target;</c> statement used
/// to re-export an imported identifier under another name or nested
/// path — <c>--namespace-barrels</c> emits these to mirror the C#
/// namespace hierarchy in the root barrel. The statement may appear
/// at file scope OR as a member of a
/// <see cref="TsNamespaceDeclaration"/>. Single-segment leaves
/// (e.g., <c>shared-kernel</c>) emit at file scope;
/// multi-segment paths nest inside <c>export namespace</c> blocks.
/// </summary>
public sealed record TsExportImportAlias(string Alias, string Target) : TsTopLevel;
