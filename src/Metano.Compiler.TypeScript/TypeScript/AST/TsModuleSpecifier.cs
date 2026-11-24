namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// The module an import or re-export names. Importers record <em>what</em> they
/// import; the text of the specifier is chosen once, when the file is printed, by
/// <see cref="Transformation.ModuleSpecifierRenderer"/> (spec 004 Amendment A1).
/// </summary>
public abstract record TsModuleSpecifier;

/// <summary>
/// A module outside the generated package — an npm package, a package subpath,
/// a BCL mapping or an <c>[Import]</c> source — printed verbatim.
/// </summary>
public sealed record TsPackageModule(string Specifier) : TsModuleSpecifier;

/// <summary>
/// A generated module of the same package, named by its file key: the
/// package-relative path without extension (<c>shared-kernel/user-id</c>; a leaf
/// barrel is <c>shared-kernel/index</c>). Rendered relative to the importing file.
/// </summary>
public sealed record TsLocalModule(string FileKey) : TsModuleSpecifier;
