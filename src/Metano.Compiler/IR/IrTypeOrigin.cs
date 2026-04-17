namespace Metano.Compiler.IR;

/// <summary>
/// Identifies the cross-assembly origin of a type. This is a logical package identifier,
/// not a target-specific import path. Each backend resolves this to its own module/import
/// system (e.g., TypeScript: <c>import from "package/subpath"</c>,
/// Dart: <c>import 'package:name/path.dart'</c>).
/// </summary>
/// <param name="PackageId">Logical package identifier (from <c>[EmitPackage]</c>).</param>
/// <param name="Namespace">Namespace of the referenced type (C# namespace hierarchy).</param>
/// <param name="AssemblyRootNamespace">Root namespace of the producing assembly; backends
/// combine this with <see cref="Namespace"/> to compute a package-relative path for imports.</param>
/// <param name="VersionHint">Version specifier, if declared. Backends use this for
/// dependency manifest generation (e.g., <c>package.json</c>).</param>
public sealed record IrTypeOrigin(
    string PackageId,
    string? Namespace = null,
    string? AssemblyRootNamespace = null,
    string? VersionHint = null
);
