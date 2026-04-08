namespace MetaSharp.Annotations;

/// <summary>
/// Marks a static method whose body becomes the top-level executable code of the
/// generated TypeScript module instead of being emitted as a regular function. The
/// containing class must also be <see cref="ExportedAsModuleAttribute"/>.
///
/// <para>
/// This is independent of (and complements) <see cref="ExportedAsModuleAttribute"/>:
/// other static methods on the class still become exported functions; only the method
/// marked with <c>[ModuleEntryPoint]</c> has its body unwrapped at the file's top level.
/// </para>
///
/// <para><strong>Note:</strong> The transpiler logic that consumes this attribute is
/// not yet implemented. Currently this attribute exists only as a declaration so that
/// consumer code referencing it compiles. End-to-end behavior comes in a follow-up
/// commit.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ModuleEntryPointAttribute : Attribute;
