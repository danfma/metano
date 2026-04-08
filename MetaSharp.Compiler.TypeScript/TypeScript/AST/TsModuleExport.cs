namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A trailing module-level export statement that references a previously declared
/// binding by name. Produced by <c>[ExportVarFromBody]</c> with <c>InPlace = false</c>.
///
/// <list type="bullet">
///   <item><see cref="IsDefault"/> = true → <c>export default name;</c></item>
///   <item><see cref="IsDefault"/> = false → <c>export { name };</c></item>
/// </list>
///
/// Kept as a distinct top-level node (rather than reusing <see cref="TsReExport"/>)
/// because it has no source module — it re-exports a local binding from the same file.
/// </summary>
public sealed record TsModuleExport(string Name, bool IsDefault) : TsTopLevel;
