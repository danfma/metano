using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// Identifies a transpilable type that lives in a referenced assembly (not the one
/// currently being compiled). Carries everything the import collector / type mapper
/// need to emit a cross-package import statement of the form
/// <c>import { Type } from "&lt;package&gt;/&lt;subpath&gt;"</c>:
///
/// <list type="bullet">
///   <item><see cref="Symbol"/> — the original Roslyn symbol so callers can disambiguate
///   by identity (two assemblies may declare types with the same simple name).</item>
///   <item><see cref="PackageName"/> — the value of <c>[assembly: EmitPackage(name)]</c>
///   on the source assembly. Used as the import path's package prefix.</item>
///   <item><see cref="AssemblyRootNamespace"/> — the root namespace of the source
///   assembly (longest common prefix of its transpilable types' namespaces). Each
///   assembly has its own root, so the subpath of a cross-assembly type is computed
///   relative to <em>its</em> root, not the consumer's.</item>
/// </list>
/// </summary>
public sealed record CrossAssemblyEntry(
    INamedTypeSymbol Symbol,
    string PackageName,
    string AssemblyRootNamespace);
