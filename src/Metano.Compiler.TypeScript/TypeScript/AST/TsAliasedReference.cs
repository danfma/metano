namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A reference to an imported binding (a containerless function, a module
/// export, a cross-package export) read where a local binding of the same
/// name shadows it: the instance initializers a constructor stores run in the
/// constructor's scope, where a parameter or local may take the name (W10-1b).
/// The printer emits <see cref="LocalName"/>, and the import collector binds
/// the export under it (<c>import { compute as compute$1 }</c>), as it aliases
/// the runtime helpers a local shadows.
/// </summary>
/// <param name="Reference">The reference as the bridge built it.</param>
/// <param name="Name">The imported name it reads.</param>
/// <param name="LocalName">The alias the import binds and the read spells.</param>
public sealed record TsAliasedReference(TsExpression Reference, string Name, string LocalName)
    : TsExpression;
