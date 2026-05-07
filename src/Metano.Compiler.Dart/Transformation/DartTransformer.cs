using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Dart.AST;
using Metano.Dart.Bridge;
using Metano.Compiler.Mappings;
using Microsoft.CodeAnalysis;

namespace Metano.Dart.Transformation;

/// <summary>
/// Orchestrates Dart emission: reads the ordered transpilable-type list
/// off the shared <see cref="IrCompilation"/>, extracts each into IR,
/// routes to the appropriate Dart bridge, and assembles the resulting
/// <see cref="DartSourceFile"/>s with their imports.
/// <para>
/// This is a prototype — currently handles enums, interfaces, and plain class shapes
/// (no method bodies). Records/operators/exceptions need expression extraction, which
/// lands in Phase 5.
/// </para>
/// </summary>
public sealed class DartTransformer(IrCompilation ir, Compilation compilation)
{
    private readonly IrCompilation _ir = ir;
    private readonly Compilation _compilation = compilation;
    private readonly List<MetanoDiagnostic> _diagnostics = new();
    private readonly DartIrRewriter _rewriter = new(DeclarativeMappingRegistry.FromIr(ir));

    public IReadOnlyList<MetanoDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<DartSourceFile> TransformAll()
    {
        var files = new List<DartSourceFile>();
        var transpilable = DiscoverTranspilableTypes();

        // Map of emitted type name (PascalCase) → file name (snake_case.dart) so bridges
        // can emit relative imports for same-package references. Duplicate simple names
        // across namespaces (e.g., Admin.User and Billing.User) are currently unsupported
        // because the file name derives from the simple name alone; we surface a
        // diagnostic and keep the first occurrence so generation still completes for the
        // rest of the project. Proper namespace-qualified output paths are tracked as
        // a follow-up.
        var localTypeFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in transpilable)
        {
            // [Ignore(TargetLanguage.Dart)] declares the type for C# use but
            // skips Dart file emission. It must not make it into the local
            // import map either — otherwise a consumer referencing the type
            // would emit `import 'foo.dart';` pointing at a file that never
            // got written, breaking the build.
            if (SymbolHelper.HasIgnore(type, TargetLanguage.Dart))
                continue;

            var emittedName = GetEmittedTypeName(type);
            var fileName = IrToDartNamingPolicy.ToFileName(emittedName);
            if (localTypeFiles.ContainsKey(emittedName))
            {
                _diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.AmbiguousConstruct,
                        $"Dart target: two transpilable types share the simple name "
                            + $"'{emittedName}' (declared in different namespaces). The "
                            + $"current Dart backend derives the output file from the "
                            + $"simple name and cannot disambiguate. Keep a single "
                            + $"'{emittedName}' or add an [EmitInFile] / [Name] "
                            + $"attribute to rename one of them."
                    )
                );
                continue;
            }
            localTypeFiles[emittedName] = fileName;
            seenTypes.Add(type);
        }

        foreach (var type in transpilable)
        {
            if (SymbolHelper.HasIgnore(type, TargetLanguage.Dart))
                continue;
            // A duplicate simple name already surfaced a diagnostic; skip the
            // late-arriving sibling so we don't produce two files targeting the
            // same path (the OS would pick last-write-wins and the build graph
            // would silently lose one).
            if (!seenTypes.Contains(type))
                continue;

            var fileName = IrToDartNamingPolicy.ToFileName(GetEmittedTypeName(type));
            var statements = new List<DartTopLevel>();
            var runtimeRequirements = new HashSet<IrRuntimeRequirement>();

            switch (type.TypeKind)
            {
                case TypeKind.Enum:
                    var enumIr = IrEnumExtractor.Extract(type);
                    IrToDartEnumBridge.Convert(enumIr, statements);
                    ScanInto(runtimeRequirements, enumIr);
                    break;

                case TypeKind.Interface:
                    var interfaceIr = IrInterfaceExtractor.Extract(
                        type,
                        target: TargetLanguage.Dart
                    );
                    IrToDartInterfaceBridge.Convert(interfaceIr, statements);
                    ScanInto(runtimeRequirements, interfaceIr);
                    break;

                case TypeKind.Class:
                case TypeKind.Struct:
                    // Static classes decorated with [NoContainer] (or the legacy
                    // [ExportedAsModule] still tracked under the same predicate)
                    // collapse to top-level Dart functions (the idiomatic
                    // utility-module shape) instead of a class of static methods.
                    if (
                        type.IsStatic
                        && (
                            SymbolHelper.HasExportedAsModule(type)
                            || SymbolHelper.HasNoContainer(type)
                        )
                    )
                    {
                        var functions = IrModuleFunctionExtractor.Extract(
                            type,
                            originResolver: null,
                            compilation: _compilation,
                            target: TargetLanguage.Dart
                        );
                        IrToDartModuleBridge.Convert(functions, statements, _rewriter);
                        ScanFunctionsInto(runtimeRequirements, functions);
                        break;
                    }
                    var classIr = IrClassExtractor.Extract(
                        type,
                        originResolver: null,
                        compilation: _compilation,
                        target: TargetLanguage.Dart
                    );
                    ReportOverloadDiagnostics(classIr, type.Name);
                    IrToDartClassBridge.Convert(classIr, statements, _rewriter);
                    ScanInto(runtimeRequirements, classIr);
                    break;

                case TypeKind.Delegate:
                    var delegateIr = IrDelegateExtractor.Extract(type, target: TargetLanguage.Dart);
                    if (delegateIr is null)
                    {
                        _diagnostics.Add(
                            new MetanoDiagnostic(
                                MetanoDiagnosticSeverity.Warning,
                                DiagnosticCodes.UnsupportedFeature,
                                $"Dart target: failed to extract delegate IR for '{type.Name}'."
                            )
                        );
                        continue;
                    }
                    IrToDartDelegateBridge.Convert(delegateIr, statements);
                    ScanInto(runtimeRequirements, delegateIr);
                    break;

                default:
                    _diagnostics.Add(
                        new MetanoDiagnostic(
                            MetanoDiagnosticSeverity.Warning,
                            DiagnosticCodes.UnsupportedFeature,
                            $"Dart target: type kind '{type.TypeKind}' for '{type.Name}' "
                                + "is not yet supported."
                        )
                    );
                    continue;
            }

            var imports = DartImportCollector.Collect(
                statements,
                fileName,
                localTypeFiles,
                runtimeRequirements
            );
            statements.InsertRange(0, imports);
            files.Add(new DartSourceFile(fileName, statements));
        }

        return files;
    }

    // ── Discovery ─────────────────────────────────────────────────────────

    /// <summary>
    /// Flattens the frontend-owned ordered top-level transpilable-type
    /// list (<see cref="IrCompilation.TranspilableTypeEntries"/>) plus
    /// every nested transpilable type under each entry into a single
    /// emission list. The Dart prototype emits one file per type (no
    /// companion-namespace wrapping like the TS target), so nested types
    /// join the flat list here — <see cref="CollectNested"/> recurses
    /// into them using the frontend-reported
    /// <see cref="IrCompilation.AssemblyWideTranspile"/> flag.
    /// </summary>
    private List<INamedTypeSymbol> DiscoverTranspilableTypes()
    {
        var entries = _ir.TranspilableTypeEntries ?? Array.Empty<IrTranspilableTypeEntry>();
        var result = new List<INamedTypeSymbol>();
        foreach (var entry in entries)
        {
            result.Add(entry.Symbol);
            CollectNested(entry.Symbol, _ir.AssemblyWideTranspile, result);
        }
        return result;
    }

    /// <summary>
    /// Dart has no method overloading — two methods with the same name but
    /// different signatures can't coexist. The IR captures C# overload sets via
    /// <see cref="IrMethodDeclaration.Overloads"/>; when the Dart bridge sees
    /// one, we surface a diagnostic pointing the user at <c>[Name]</c> (rename)
    /// or optional-parameter refactoring and keep emitting the primary method
    /// only. Silently dropping the extras would hide a real semantic mismatch.
    /// </summary>
    private void ReportOverloadDiagnostics(IrClassDeclaration classIr, string typeName)
    {
        if (classIr.Members is null)
            return;
        foreach (var member in classIr.Members)
        {
            if (member is IrMethodDeclaration method && method.Overloads is { Count: > 0 })
            {
                _diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Warning,
                        DiagnosticCodes.UnsupportedFeature,
                        $"Dart target: '{typeName}.{method.Name}' has "
                            + $"{method.Overloads.Count + 1} overloads but Dart doesn't "
                            + "support method overloading. Emitting the primary signature "
                            + "only; rename the other overloads via [Name(\"...\")] or merge "
                            + "them into a single method with optional parameters."
                    )
                );
            }
        }
    }

    private static void CollectNested(
        INamedTypeSymbol type,
        bool assemblyWide,
        List<INamedTypeSymbol> acc
    )
    {
        foreach (var nested in type.GetTypeMembers())
        {
            if (IsTranspilable(nested, assemblyWide))
                acc.Add(nested);
            CollectNested(nested, assemblyWide, acc);
        }
    }

    private static bool IsTranspilable(INamedTypeSymbol type, bool assemblyWide)
    {
        if (SymbolHelper.HasIgnore(type))
            return false;
        if (SymbolHelper.HasTranspile(type))
            return true;
        if (assemblyWide && type.DeclaredAccessibility == Accessibility.Public)
            return true;
        return false;
    }

    private static string GetEmittedTypeName(INamedTypeSymbol type) =>
        SymbolHelper.GetNameOverride(type, TargetLanguage.Dart) ?? type.Name;

    // ── Runtime requirements ──────────────────────────────────────────────

    private static void ScanInto(HashSet<IrRuntimeRequirement> sink, IrTypeDeclaration declaration)
    {
        foreach (var req in IrRuntimeRequirementScanner.Scan(declaration))
            sink.Add(req);
    }

    /// <summary>
    /// Scans module-level functions for runtime requirements. Wraps the function
    /// list in a synthetic <see cref="IrModule"/> so the shared scanner — which
    /// only operates on type declarations or whole modules — can walk them.
    /// Tracking issue: add an <c>IrRuntimeRequirementScanner.Scan(IReadOnlyList&lt;IrModuleFunction&gt;)</c>
    /// overload so callers don't fabricate IR shells just to satisfy the entry point.
    /// </summary>
    private static void ScanFunctionsInto(
        HashSet<IrRuntimeRequirement> sink,
        IReadOnlyList<IrModuleFunction> functions
    )
    {
        var module = new IrModule(
            Name: string.Empty,
            Namespace: null,
            Types: Array.Empty<IrTypeDeclaration>(),
            Functions: functions
        );
        foreach (var req in IrRuntimeRequirementScanner.Scan(module))
            sink.Add(req);
    }
}
