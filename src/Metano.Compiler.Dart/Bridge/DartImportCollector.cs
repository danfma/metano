using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Dart.AST;

namespace Metano.Dart.Bridge;

/// <summary>
/// Walks the generated Dart AST for a single file and produces every <see cref="DartImport"/>
/// the file needs. Imports come from three sources, emitted in this order to mirror the
/// TypeScript backend (runtime helpers → cross-package barrels → relative siblings):
/// <list type="number">
///   <item>Runtime-helper imports derived from <see cref="IrRuntimeRequirement"/>
///   facts produced by <see cref="Compiler.Extraction.IrRuntimeRequirementScanner"/>
///   — currently <c>package:metano_runtime/metano_runtime.dart show …</c>.</item>
///   <item>Cross-package imports backed by <see cref="DartTypeOrigin"/>
///   (<c>import 'package:name/sub.dart';</c>).</item>
///   <item>Relative imports for sibling files in the same Dart package
///   (<c>import 'other.dart';</c>) — derived by matching named-type references
///   against <c>localTypeFiles</c>.</item>
/// </list>
/// The helper is pure: no compilation context, no instance state, the same call shape
/// runs unchanged across every file in the project.
/// </summary>
public static class DartImportCollector
{
    public static IReadOnlyList<DartImport> Collect(
        IReadOnlyList<DartTopLevel> statements,
        string currentFileName,
        IReadOnlyDictionary<string, string> localTypeFiles,
        IReadOnlySet<IrRuntimeRequirement>? runtimeRequirements = null
    )
    {
        var ctx = new WalkContext(
            new Dictionary<string, DartTypeOrigin>(StringComparer.Ordinal),
            localTypeFiles,
            currentFileName,
            new HashSet<string>(StringComparer.Ordinal)
        );

        foreach (var stmt in statements)
            WalkTopLevel(stmt, ctx);

        var imports = new List<DartImport>();

        if (runtimeRequirements is { Count: > 0 } reqs)
            imports.AddRange(IrRuntimeRequirementToDartImport.Convert(reqs));

        foreach (var origin in ctx.Origins.Values.OrderBy(o => o.Package).ThenBy(o => o.Path))
            imports.Add(new DartImport($"package:{origin.Package}/{origin.Path}.dart"));

        foreach (var relativeFile in ctx.RelativeImports.OrderBy(s => s, StringComparer.Ordinal))
            imports.Add(new DartImport(relativeFile));

        return imports;
    }

    /// <summary>
    /// Carries the mutable accumulators threaded through the walker. A record keeps the
    /// recursive descent's signature short and centralizes the four pieces of state the
    /// walker needs (cross-package origins, local type→file map, current file, relative
    /// imports already collected).
    /// </summary>
    private sealed record WalkContext(
        Dictionary<string, DartTypeOrigin> Origins,
        IReadOnlyDictionary<string, string> LocalTypeFiles,
        string CurrentFile,
        HashSet<string> RelativeImports
    );

    // ── AST walkers ───────────────────────────────────────────────────────
    // The walker covers the AST shape the bridge currently emits: classes,
    // module-level functions, and delegate-derived typedefs. Method bodies and
    // expression-level references aren't traversed yet — the body printer
    // resolves those identifiers directly. Future AST features (exception
    // types, dispatcher-style classes) will plug in additional cases here.

    private static void WalkTopLevel(DartTopLevel stmt, WalkContext ctx)
    {
        switch (stmt)
        {
            case DartClass cls:
                WalkClass(cls, ctx);
                break;
            case DartFunction fn:
                // [ExportedAsModule] static classes lower to top-level DartFunctions;
                // their parameter + return types still need to contribute imports.
                WalkType(fn.ReturnType, ctx);
                foreach (var p in fn.Parameters)
                    WalkType(p.Type, ctx);
                break;
            case DartTypedef td:
                // Delegate-derived typedefs alias a function signature; both the
                // return and parameter types pull imports. Type-parameter
                // constraints can also reference cross-package types, so walk
                // each `extends` bound here too.
                WalkType(td.Signature, ctx);
                if (td.TypeParameters is not null)
                    foreach (var tp in td.TypeParameters)
                        if (tp.Extends is not null)
                            WalkType(tp.Extends, ctx);
                break;
        }
    }

    private static void WalkClass(DartClass cls, WalkContext ctx)
    {
        if (cls.ExtendsType is not null)
            WalkType(cls.ExtendsType, ctx);
        if (cls.Implements is not null)
            foreach (var i in cls.Implements)
                WalkType(i, ctx);
        if (cls.Members is not null)
            foreach (var m in cls.Members)
                WalkMember(m, ctx);
        if (cls.Constructor is not null)
            foreach (var p in cls.Constructor.Parameters)
                if (p.Type is not null)
                    WalkType(p.Type, ctx);
    }

    private static void WalkMember(DartClassMember member, WalkContext ctx)
    {
        switch (member)
        {
            case DartField f:
                WalkType(f.Type, ctx);
                break;
            case DartGetter g:
                WalkType(g.ReturnType, ctx);
                break;
            case DartMethodSignature m:
                WalkType(m.ReturnType, ctx);
                foreach (var p in m.Parameters)
                    WalkType(p.Type, ctx);
                break;
        }
    }

    private static void WalkType(DartType type, WalkContext ctx)
    {
        switch (type)
        {
            case DartNamedType named:
                if (named.Origin is not null)
                {
                    var key = $"{named.Origin.Package}/{named.Origin.Path}";
                    ctx.Origins[key] = named.Origin;
                }
                else if (
                    ctx.LocalTypeFiles.TryGetValue(named.Name, out var otherFile)
                    && otherFile != ctx.CurrentFile
                )
                {
                    ctx.RelativeImports.Add(otherFile);
                }
                if (named.TypeArguments is not null)
                    foreach (var a in named.TypeArguments)
                        WalkType(a, ctx);
                break;
            case DartNullableType n:
                WalkType(n.Inner, ctx);
                break;
            case DartFunctionType f:
                WalkType(f.ReturnType, ctx);
                foreach (var p in f.Parameters)
                    WalkType(p.Type, ctx);
                break;
            case DartRecordType r:
                foreach (var e in r.Elements)
                    WalkType(e, ctx);
                break;
        }
    }
}
