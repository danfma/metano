using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Dart.AST;
using Metano.Dart.Bridge;
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
            // [NoEmit(TargetLanguage.Dart)] declares the type for C# use but
            // skips Dart file emission. It must not make it into the local
            // import map either — otherwise a consumer referencing the type
            // would emit `import 'foo.dart';` pointing at a file that never
            // got written, breaking the build.
            if (SymbolHelper.HasNoEmit(type, TargetLanguage.Dart))
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
            if (SymbolHelper.HasNoEmit(type, TargetLanguage.Dart))
                continue;
            // A duplicate simple name already surfaced a diagnostic; skip the
            // late-arriving sibling so we don't produce two files targeting the
            // same path (the OS would pick last-write-wins and the build graph
            // would silently lose one).
            if (!seenTypes.Contains(type))
                continue;

            var fileName = IrToDartNamingPolicy.ToFileName(GetEmittedTypeName(type));
            var statements = new List<DartTopLevel>();

            switch (type.TypeKind)
            {
                case TypeKind.Enum:
                    IrToDartEnumBridge.Convert(IrEnumExtractor.Extract(type), statements);
                    break;

                case TypeKind.Interface:
                    IrToDartInterfaceBridge.Convert(
                        IrInterfaceExtractor.Extract(type, target: TargetLanguage.Dart),
                        statements
                    );
                    break;

                case TypeKind.Class:
                case TypeKind.Struct:
                    // Static classes decorated with [ExportedAsModule] collapse to
                    // top-level Dart functions (the idiomatic utility-module shape)
                    // instead of a class of static methods.
                    if (type.IsStatic && SymbolHelper.HasExportedAsModule(type))
                    {
                        var functions = IrModuleFunctionExtractor.Extract(
                            type,
                            originResolver: null,
                            compilation: _compilation,
                            target: TargetLanguage.Dart
                        );
                        IrToDartModuleBridge.Convert(functions, statements);
                        break;
                    }
                    var classIr = IrClassExtractor.Extract(
                        type,
                        originResolver: null,
                        compilation: _compilation,
                        target: TargetLanguage.Dart
                    );
                    ReportOverloadDiagnostics(classIr, type.Name);
                    IrToDartClassBridge.Convert(classIr, statements);
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

            var imports = CollectImports(statements, fileName, localTypeFiles);
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
        if (SymbolHelper.HasNoTranspile(type))
            return false;
        if (SymbolHelper.HasTranspile(type))
            return true;
        if (assemblyWide && type.DeclaredAccessibility == Accessibility.Public)
            return true;
        return false;
    }

    private static string GetEmittedTypeName(INamedTypeSymbol type) =>
        SymbolHelper.GetNameOverride(type, TargetLanguage.Dart) ?? type.Name;

    // ── Imports ────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the generated Dart AST and produces:
    /// <list type="bullet">
    ///   <item>Cross-package imports (<c>import 'package:name/path.dart';</c>) from every
    ///   <see cref="DartTypeOrigin"/> attached to a named type.</item>
    ///   <item>Relative imports (<c>import 'other.dart';</c>) for references to other
    ///   types declared in the same package (and thus in sibling files).</item>
    /// </list>
    /// </summary>
    private static List<DartTopLevel> CollectImports(
        IReadOnlyList<DartTopLevel> statements,
        string currentFileName,
        IReadOnlyDictionary<string, string> localTypeFiles
    )
    {
        var origins = new Dictionary<string, DartTypeOrigin>();
        var relativeImports = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stmt in statements)
            WalkTopLevel(stmt, origins, localTypeFiles, currentFileName, relativeImports);

        var imports = new List<DartTopLevel>();

        foreach (var relativeFile in relativeImports.OrderBy(s => s, StringComparer.Ordinal))
            imports.Add(new DartImport(relativeFile));

        foreach (var origin in origins.Values.OrderBy(o => o.Package).ThenBy(o => o.Path))
            imports.Add(new DartImport($"package:{origin.Package}/{origin.Path}.dart"));

        return imports;
    }

    private static void WalkTopLevel(
        DartTopLevel stmt,
        Dictionary<string, DartTypeOrigin> origins,
        IReadOnlyDictionary<string, string> localTypeFiles,
        string currentFile,
        HashSet<string> relativeImports
    )
    {
        switch (stmt)
        {
            case DartClass cls:
                WalkClass(cls, origins, localTypeFiles, currentFile, relativeImports);
                break;
            case DartFunction fn:
                // [ExportedAsModule] static classes lower to top-level DartFunctions;
                // their parameter + return types still need to contribute imports.
                WalkType(fn.ReturnType, origins, localTypeFiles, currentFile, relativeImports);
                foreach (var p in fn.Parameters)
                    WalkType(p.Type, origins, localTypeFiles, currentFile, relativeImports);
                break;
        }
    }

    private static void WalkClass(
        DartClass cls,
        Dictionary<string, DartTypeOrigin> origins,
        IReadOnlyDictionary<string, string> localTypeFiles,
        string currentFile,
        HashSet<string> relativeImports
    )
    {
        if (cls.ExtendsType is not null)
            WalkType(cls.ExtendsType, origins, localTypeFiles, currentFile, relativeImports);
        if (cls.Implements is not null)
            foreach (var i in cls.Implements)
                WalkType(i, origins, localTypeFiles, currentFile, relativeImports);
        if (cls.Members is not null)
            foreach (var m in cls.Members)
                WalkMember(m, origins, localTypeFiles, currentFile, relativeImports);
        if (cls.Constructor is not null)
            foreach (var p in cls.Constructor.Parameters)
                if (p.Type is not null)
                    WalkType(p.Type, origins, localTypeFiles, currentFile, relativeImports);
    }

    private static void WalkMember(
        DartClassMember member,
        Dictionary<string, DartTypeOrigin> origins,
        IReadOnlyDictionary<string, string> localTypeFiles,
        string currentFile,
        HashSet<string> relativeImports
    )
    {
        switch (member)
        {
            case DartField f:
                WalkType(f.Type, origins, localTypeFiles, currentFile, relativeImports);
                break;
            case DartGetter g:
                WalkType(g.ReturnType, origins, localTypeFiles, currentFile, relativeImports);
                break;
            case DartMethodSignature m:
                WalkType(m.ReturnType, origins, localTypeFiles, currentFile, relativeImports);
                foreach (var p in m.Parameters)
                    WalkType(p.Type, origins, localTypeFiles, currentFile, relativeImports);
                break;
        }
    }

    private static void WalkType(
        DartType type,
        Dictionary<string, DartTypeOrigin> origins,
        IReadOnlyDictionary<string, string> localTypeFiles,
        string currentFile,
        HashSet<string> relativeImports
    )
    {
        switch (type)
        {
            case DartNamedType named:
                if (named.Origin is not null)
                {
                    var key = $"{named.Origin.Package}/{named.Origin.Path}";
                    origins[key] = named.Origin;
                }
                else if (
                    localTypeFiles.TryGetValue(named.Name, out var otherFile)
                    && otherFile != currentFile
                )
                {
                    relativeImports.Add(otherFile);
                }
                if (named.TypeArguments is not null)
                    foreach (var a in named.TypeArguments)
                        WalkType(a, origins, localTypeFiles, currentFile, relativeImports);
                break;
            case DartNullableType n:
                WalkType(n.Inner, origins, localTypeFiles, currentFile, relativeImports);
                break;
            case DartFunctionType f:
                WalkType(f.ReturnType, origins, localTypeFiles, currentFile, relativeImports);
                foreach (var p in f.Parameters)
                    WalkType(p.Type, origins, localTypeFiles, currentFile, relativeImports);
                break;
            case DartRecordType r:
                foreach (var e in r.Elements)
                    WalkType(e, origins, localTypeFiles, currentFile, relativeImports);
                break;
        }
    }
}
