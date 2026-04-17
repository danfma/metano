namespace Metano.Compiler.IR;

/// <summary>
/// An external JS/TS module dependency declared on a C# type via
/// <c>[Import(name, from)]</c>, with optional <c>AsDefault</c> and
/// <c>Version</c> attribute properties. The frontend resolves every
/// <c>[Import]</c>-annotated type once during extraction and indexes the
/// result by the type's simple name + the emitted (target) name so
/// backends can emit the import without re-walking Roslyn.
/// </summary>
/// <param name="Name">Identifier bound in the generated source (may be the
/// default export alias).</param>
/// <param name="From">Module specifier (e.g. npm package name or relative
/// path).</param>
/// <param name="IsDefault">When <c>true</c>, the import is the module's
/// default export.</param>
/// <param name="Version">Optional semver specifier the backend merges into
/// its package manifest (npm <c>dependencies</c>, pub <c>dependencies</c>,
/// …).</param>
public sealed record IrExternalImport(string Name, string From, bool IsDefault, string? Version);
