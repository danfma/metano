namespace Metano.TypeScript.AST;

/// <summary>
/// Origin metadata attached to a <see cref="TsNamedType"/> when its source symbol comes
/// from a referenced (cross-assembly) package. The <see cref="Transformation.TypeMapper"/>
/// resolves this at type-mapping time so the import collector emits the correct
/// <c>import { Foo } from "&lt;package&gt;/&lt;subpath&gt;"</c> (or directly from
/// <c>&lt;package&gt;</c> when <see cref="SubPath"/> is empty) without ever performing a
/// name-based lookup (which would be ambiguous when two assemblies expose types with the
/// same simple name).
/// </summary>
/// <param name="PackageName">The npm package name from the source assembly's
/// <c>[assembly: EmitPackage(name)]</c>.</param>
/// <param name="SubPath">The package-relative namespace barrel subpath (no <c>./</c>
/// prefix, no <c>.ts</c> suffix). Empty means the package root barrel. Example:
/// <c>"domain"</c>.</param>
/// <param name="IsDefault">When true, the import statement uses default-import syntax
/// (<c>import Foo from "..."</c>) instead of named-import syntax. Reserved for the
/// case where the source file's binding is exported as default — currently no
/// Metano-emitted type uses default export, but the slot is plumbed for forward
/// compatibility with <c>[Import(..., AsDefault = true)]</c>.</param>
public sealed record TsTypeOrigin(string PackageName, string SubPath, bool IsDefault = false);
