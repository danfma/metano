using MetaSharp.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// Immutable shared state passed to every TypeScript-target transformer / builder during a
/// single transpilation run. Built once after the discovery + bootstrap phase of
/// <see cref="TypeTransformer.TransformAll"/> and read-only thereafter.
///
/// Holds:
/// <list type="bullet">
///   <item>The Roslyn <see cref="Compilation"/> + current assembly + assembly-wide transpile flag</item>
///   <item>The discovered transpilable type map (by both C# and TS name)</item>
///   <item>External import / BCL export / guard-name lookup tables</item>
///   <item>The <see cref="Transformation.PathNaming"/> helper carrying the project's root namespace</item>
///   <item>The <see cref="Transformation.DeclarativeMappingRegistry"/> with all <c>[MapMethod]</c>/<c>[MapProperty]</c> entries collected from referenced assemblies</item>
///   <item>A diagnostic reporter callback that drains into <c>TypeTransformer.Diagnostics</c></item>
/// </list>
///
/// Helpers and builders extracted from <see cref="TypeTransformer"/> take a single
/// <see cref="TypeScriptTransformContext"/> instead of growing parameter lists or
/// reaching into the transformer's private fields.
/// </summary>
public sealed class TypeScriptTransformContext(
    Compilation compilation,
    IAssemblySymbol? currentAssembly,
    bool assemblyWideTranspile,
    IReadOnlyDictionary<string, INamedTypeSymbol> transpilableTypeMap,
    IReadOnlyDictionary<string, (string Name, string From, bool IsDefault)> externalImportMap,
    IReadOnlyDictionary<string, (string ExportedName, string FromPackage)> bclExportMap,
    IReadOnlyDictionary<string, string> guardNameToTypeMap,
    PathNaming pathNaming,
    DeclarativeMappingRegistry declarativeMappings,
    Action<MetaSharpDiagnostic> reportDiagnostic)
{
    public Compilation Compilation { get; } = compilation;
    public IAssemblySymbol? CurrentAssembly { get; } = currentAssembly;
    public bool AssemblyWideTranspile { get; } = assemblyWideTranspile;
    public IReadOnlyDictionary<string, INamedTypeSymbol> TranspilableTypeMap { get; } = transpilableTypeMap;
    public IReadOnlyDictionary<string, (string Name, string From, bool IsDefault)> ExternalImportMap { get; } = externalImportMap;
    public IReadOnlyDictionary<string, (string ExportedName, string FromPackage)> BclExportMap { get; } = bclExportMap;
    public IReadOnlyDictionary<string, string> GuardNameToTypeMap { get; } = guardNameToTypeMap;
    public PathNaming PathNaming { get; } = pathNaming;
    public DeclarativeMappingRegistry DeclarativeMappings { get; } = declarativeMappings;
    public Action<MetaSharpDiagnostic> ReportDiagnostic { get; } = reportDiagnostic;

    /// <summary>
    /// Creates a configured <see cref="ExpressionTransformer"/> for the given semantic
    /// model. Centralized here so every caller produces an expression transformer with
    /// the same diagnostics + assembly-wide-transpile wiring.
    /// </summary>
    public ExpressionTransformer CreateExpressionTransformer(SemanticModel semanticModel) =>
        new(semanticModel)
        {
            AssemblyWideTranspile = AssemblyWideTranspile,
            CurrentAssembly = CurrentAssembly,
            ReportDiagnostic = ReportDiagnostic,
            DeclarativeMappings = DeclarativeMappings,
        };
}
