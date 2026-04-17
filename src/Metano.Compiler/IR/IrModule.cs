namespace Metano.Compiler.IR;

/// <summary>
/// The top-level compilation unit in the IR. Represents a single file/module
/// worth of type declarations and module-level functions.
///
/// An <see cref="IrModule"/> does NOT carry:
/// <list type="bullet">
///   <item>Import statements — imports are a target concern resolved by the backend.</item>
///   <item>File paths — file layout is a target concern.</item>
///   <item>Export flags — export policy is target-specific.</item>
/// </list>
/// </summary>
/// <param name="Name">Module name (derived from the C# namespace).</param>
/// <param name="Namespace">The C# namespace this module originates from.</param>
/// <param name="Types">Type declarations in this module.</param>
/// <param name="Functions">Module-level functions (from <c>[ExportedAsModule]</c> static classes
/// or <c>[ModuleEntryPoint]</c> methods).</param>
/// <param name="RuntimeRequirements">Runtime helpers needed by this module. Populated
/// during extraction and consumed by the backend for import generation.</param>
public sealed record IrModule(
    string Name,
    string? Namespace,
    IReadOnlyList<IrTypeDeclaration> Types,
    IReadOnlyList<IrModuleFunction>? Functions = null,
    IReadOnlySet<IrRuntimeRequirement>? RuntimeRequirements = null
);

/// <summary>
/// A top-level function within a module (not a class method).
/// Produced from <c>[ExportedAsModule]</c> static methods or
/// <c>[ModuleEntryPoint]</c> method bodies. Backends use
/// <see cref="Attributes"/> to honor overrides like <c>[Name]</c> and
/// <see cref="TypeParameters"/> to emit the correct generic signature.
/// </summary>
public sealed record IrModuleFunction(
    string Name,
    IReadOnlyList<IrParameter> Parameters,
    IrTypeRef ReturnType,
    IReadOnlyList<IrStatement>? Body,
    IrMethodSemantics Semantics,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrAttribute>? Attributes = null
);
