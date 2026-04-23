namespace Metano.TypeScript.AST;

/// <summary>
/// A TypeScript <c>export import Alias = Target;</c> statement. Used
/// inside a namespace block to re-export an imported identifier under
/// a nested path — <c>--namespace-barrels</c> emits these to mirror
/// the C# namespace hierarchy in the root barrel. Only valid inside a
/// <see cref="TsNamespaceDeclaration"/>.
/// </summary>
public sealed record TsExportImportAlias(string Alias, string Target) : TsTopLevel;
