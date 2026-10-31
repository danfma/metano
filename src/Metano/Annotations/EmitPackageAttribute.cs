namespace Metano.Annotations;

/// <summary>
/// Declares the target-package identity of the current C# assembly when transpiled.
/// Read by the Metano compiler to (a) write the <c>name</c> field of the generated
/// <c>package.json</c> as an authoritative source, and (b) resolve cross-assembly type
/// references from a consumer project to <c>import { … } from "&lt;package&gt;/…"</c>
/// statements.
///
/// <para>
/// Multiple instances are allowed, one per <see cref="EmitTarget"/> — the C# project can
/// thus be transpiled to several languages, each with its own ecosystem-correct package
/// name:
/// </para>
///
/// <code>
/// [assembly: EmitPackage("sample-todo")]                                  // JavaScript (default)
/// [assembly: EmitPackage("sample_todo", EmitTarget.JavaScript)]           // explicit form
/// </code>
///
/// <para>
/// The attribute is <em>optional</em>. A library project that wants to be referenced
/// by other transpiled projects MUST declare it; an application/program project that
/// only consumes other packages doesn't need it. When a consumer references a type from
/// an assembly that lacks the attribute for the active target, the compiler emits
/// MS0007 at the consumer site.
/// </para>
/// </summary>
/// <param name="name">The package name in the target ecosystem (e.g., <c>"sample-todo"</c>
/// or <c>"@scope/name"</c> for npm).</param>
/// <param name="target">Which compiler target this declaration applies to.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class EmitPackageAttribute(string name, EmitTarget target = EmitTarget.JavaScript)
    : Attribute
{
    public string Name { get; } = name;
    public EmitTarget Target { get; } = target;

    /// <summary>
    /// Optional npm version specifier (e.g., <c>workspace:*</c> for sibling projects in
    /// a Bun monorepo, or <c>^1.2.3</c> for a published package). When set, this value
    /// is written verbatim to consumer projects' <c>package.json#dependencies</c>
    /// instead of the assembly's <c>Identity.Version</c>. The C# project's
    /// <c>&lt;AssemblyVersion&gt;</c> defaults to <c>1.0.0.0</c>, which formats as
    /// <c>^1.0.0</c> — fine for published packages but wrong for workspace siblings,
    /// where the consumer needs <c>workspace:*</c> to resolve through the monorepo
    /// linker. Setting this property explicitly resolves the ambiguity.
    /// </summary>
    public string Version { get; init; } = "";

    /// <summary>
    /// Optional C# namespace prefix stripped from the package's public subpaths
    /// (e.g. <c>"VideoStudio.Domain"</c>). With it, the barrel of
    /// <c>VideoStudio.Domain.Episodes</c> is exported as <c>./episodes</c> instead of
    /// <c>./video-studio/domain/episodes</c>, the barrel of exactly
    /// <c>VideoStudio.Domain</c> becomes the package root (<c>.</c>), and consumers
    /// import <c>"&lt;package&gt;/episodes"</c>. Only the exported subpaths change:
    /// generated files stay under their full-namespace folders and the internal
    /// <c>#/*</c> alias is untouched. Matching is by whole namespace segment, so
    /// <c>VideoStudio.DomainEvents</c> is not under <c>VideoStudio.Domain</c>.
    /// Empty (the default) keeps the full-namespace subpaths.
    /// </summary>
    public string SubpathRoot { get; init; } = "";
}
