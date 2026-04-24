using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Metano.Compiler;

/// <summary>
/// C# source frontend: opens a <c>.csproj</c> through
/// <see cref="MSBuildWorkspace"/>, runs Roslyn, and produces an
/// <see cref="IrCompilation"/> the downstream backends consume.
/// <para>
/// This is the first increment on the frontend / core split plan
/// (ADR-0013 follow-up). The loader is in place; the
/// <see cref="IrCompilation"/> fields beyond the basics are
/// deliberately left empty for now — the existing
/// <c>TypeTransformer</c> and <c>DartTransformer</c> still run their
/// own discovery + extraction off <see cref="LoadedCompilation"/>.
/// Subsequent PRs migrate that state onto <see cref="IrCompilation"/>
/// one field at a time and retire the escape hatch.
/// </para>
/// </summary>
public sealed class CSharpSourceFrontend : ISourceFrontend
{
    /// <inheritdoc />
    public string Name => "csharp";

    /// <inheritdoc />
    public Compilation? LoadedCompilation { get; private set; }

    /// <inheritdoc />
    public int LoadErrorCount { get; private set; }

    public async Task<IrCompilation> ExtractAsync(
        string projectPath,
        CancellationToken ct = default,
        TargetLanguage target = TargetLanguage.TypeScript
    )
    {
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        var diagnostics = new List<MetanoDiagnostic>();

        var (compilation, errorCount) = await LoadCompilationAsync(projectPath, diagnostics, ct);
        LoadedCompilation = compilation;
        LoadErrorCount = errorCount;

        return BuildIrCompilation(
            compilation,
            fallbackAssemblyName: assemblyName,
            diagnostics: diagnostics,
            target: target
        );
    }

    /// <summary>
    /// Builds an <see cref="IrCompilation"/> from an already-loaded Roslyn
    /// <see cref="Compilation"/>. Used by the test suite (and any future
    /// in-process caller) so the frontend's extraction can be exercised
    /// without going through <see cref="MSBuildWorkspace"/>. Mirrors the
    /// production path: stores the compilation on
    /// <see cref="LoadedCompilation"/>, resets <see cref="LoadErrorCount"/>
    /// to <c>0</c>, and returns the populated record.
    /// </summary>
    public IrCompilation ExtractFromCompilation(
        Compilation compilation,
        TargetLanguage target = TargetLanguage.TypeScript
    )
    {
        LoadedCompilation = compilation;
        LoadErrorCount = 0;
        return BuildIrCompilation(
            compilation,
            fallbackAssemblyName: compilation.AssemblyName ?? "",
            diagnostics: new List<MetanoDiagnostic>(),
            target: target
        );
    }

    private static IrCompilation BuildIrCompilation(
        Compilation? compilation,
        string fallbackAssemblyName,
        List<MetanoDiagnostic> diagnostics,
        TargetLanguage target
    )
    {
        // Fields beyond what's already populated stay empty during the
        // incremental migration. Downstream targets fall back to
        // `LoadedCompilation` for the rest until follow-up PRs wire them
        // onto `IrCompilation`.
        IReadOnlyDictionary<string, IrExternalImport> externalImports = compilation is null
            ? new Dictionary<string, IrExternalImport>(StringComparer.Ordinal)
            : BuildExternalImports(compilation, target, diagnostics);

        IReadOnlyDictionary<string, IrTypeOrigin> crossAssemblyOrigins;
        IReadOnlySet<string> assembliesNeedingEmitPackage;
        if (compilation is null)
        {
            crossAssemblyOrigins = new Dictionary<string, IrTypeOrigin>(StringComparer.Ordinal);
            assembliesNeedingEmitPackage = new HashSet<string>(StringComparer.Ordinal);
        }
        else
        {
            (crossAssemblyOrigins, assembliesNeedingEmitPackage) = BuildCrossAssemblyState(
                compilation,
                target
            );
        }

        var assemblyWideTranspile = compilation is not null && compilation.HasTranspileAssembly();

        var declarativeMappings = compilation is null
            ? default
            : BuildDeclarativeMappings(compilation, diagnostics);

        var typeNamesBySymbol = compilation is null
            ? null
            : BuildTypeNamesBySymbol(compilation, target, assemblyWideTranspile);

        var guardableTypeKeys = compilation is null
            ? null
            : BuildGuardableTypeKeys(compilation, assemblyWideTranspile);

        var transpilableTypes = compilation is null
            ? null
            : BuildTranspilableTypes(compilation, target, assemblyWideTranspile);

        var entryPoint = compilation is null
            ? null
            : BuildEntryPointInfo(compilation, assemblyWideTranspile);

        var transpilableTypeEntries = compilation is null
            ? null
            : BuildTranspilableTypeEntries(compilation, assemblyWideTranspile, entryPoint);

        if (compilation is not null)
        {
            ValidateOptionalAttribute(compilation, assemblyWideTranspile, diagnostics);
            ValidateDiscriminatorAttribute(compilation, assemblyWideTranspile, diagnostics);
            ValidateExternalAttribute(compilation, diagnostics);
            ValidateConstantAttribute(compilation, assemblyWideTranspile, diagnostics);
            ValidateInlineAttribute(compilation, diagnostics);
            ValidateThisAttribute(compilation, diagnostics);
        }

        return new IrCompilation(
            AssemblyName: compilation?.AssemblyName ?? fallbackAssemblyName,
            PackageName: compilation is null
                ? null
                : SymbolHelper.GetEmitPackage(
                    compilation.Assembly,
                    targetEnumValue: (int)target.ToEmitTarget()
                ),
            AssemblyWideTranspile: assemblyWideTranspile,
            Modules: Array.Empty<IrModule>(),
            ReferencedModules: Array.Empty<IrModule>(),
            CrossAssemblyOrigins: crossAssemblyOrigins,
            ExternalImports: externalImports,
            BclExports: compilation is null
                ? new Dictionary<string, IrBclExport>(StringComparer.Ordinal)
                : BuildBclExports(compilation),
            AssembliesNeedingEmitPackage: assembliesNeedingEmitPackage,
            Diagnostics: diagnostics,
            LocalRootNamespace: compilation is null
                ? ""
                : ComputeLocalRootNamespace(compilation, assemblyWideTranspile),
            DeclarativeMethodMappings: declarativeMappings.Methods,
            DeclarativePropertyMappings: declarativeMappings.Properties,
            ChainMethodsByWrapper: declarativeMappings.ChainMethodsByWrapper,
            TypeNamesBySymbol: typeNamesBySymbol,
            GuardableTypeKeys: guardableTypeKeys,
            TranspilableTypes: transpilableTypes,
            TranspilableTypeEntries: transpilableTypeEntries,
            EntryPoint: entryPoint
        );
    }

    /// <summary>
    /// Computes the longest common namespace prefix of the transpilable types
    /// declared in <paramref name="compilation"/>'s own assembly. Mirrors the
    /// target-side filter (<see cref="SymbolHelper.IsTranspilable"/>) so the
    /// prefix stays identical to what <c>TypeTransformer</c> used to compute
    /// inline — including internal <c>[Transpile]</c> types that would be
    /// missed by a public-only walker, and the synthetic Program type that
    /// <c>TypeTransformer.DiscoverTranspilableTypes</c> injects for C# 9+
    /// top-level statements under <c>[assembly: TranspileAssembly]</c>.
    /// </summary>
    private static string ComputeLocalRootNamespace(
        Compilation compilation,
        bool assemblyWideTranspile
    )
    {
        var currentAssembly = compilation.Assembly;
        var transpilable = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
            {
                if (SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
                    transpilable.Add(type);
            }
        );

        if (
            assemblyWideTranspile
            && TryGetTopLevelEntryPoint(compilation, out _, out var programType)
        )
            transpilable.Add(programType);

        return ComputeRootNamespaceFromTypes(transpilable);
    }

    /// <summary>
    /// Detects C# 9+ top-level statements and returns the compiler-synthesized
    /// entry-point method plus its containing type (usually <c>Program</c>)
    /// that <c>TypeTransformer</c> treats as transpilable under assembly-wide
    /// mode. Returns <c>false</c> when there are no global statements, when
    /// Roslyn reports no entry point, or when the program type opts out via
    /// <c>[ExportedAsModule]</c>. Callers that only need the containing type
    /// can discard <paramref name="entryPointMethod"/> with <c>out _</c>.
    /// </summary>
    private static bool TryGetTopLevelEntryPoint(
        Compilation compilation,
        out IMethodSymbol? entryPointMethod,
        out INamedTypeSymbol programType
    )
    {
        entryPointMethod = null;
        if (compilation is not CSharpCompilation csharpComp)
        {
            programType = null!;
            return false;
        }

        var hasGlobalStatements = compilation.SyntaxTrees.Any(t =>
            t.GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any()
        );
        if (!hasGlobalStatements)
        {
            programType = null!;
            return false;
        }

        var entryPoint = csharpComp.GetEntryPoint(CancellationToken.None);
        if (entryPoint?.ContainingType is not { } containingType)
        {
            programType = null!;
            return false;
        }

        if (SymbolHelper.HasExportedAsModule(containingType))
        {
            programType = null!;
            return false;
        }

        entryPointMethod = entryPoint;
        programType = containingType;
        return true;
    }

    /// <summary>
    /// Pre-resolves the target-facing emitted name for every type the
    /// backend may look up by Roslyn symbol. Covers transpilable types
    /// (top-level + nested) from the current assembly, transpilable types
    /// from referenced assemblies that declared <c>[EmitPackage]</c> for
    /// the active target, and <c>[Import]</c> placeholders from any public
    /// top-level symbol in scope. Types carrying <c>[NoTranspile]</c> or
    /// <c>[NoEmit(target)]</c> are excluded on both sides — the backend
    /// never asks for their name since they do not participate in emission.
    /// The resulting dictionary is keyed by
    /// <see cref="SymbolHelper.GetCrossAssemblyOriginKey"/> (the
    /// assembly-qualified stable full name) so two referenced assemblies
    /// that expose types with identical stable full names cannot collapse
    /// each other's target-name override. Backends resolve target names
    /// through a single dict lookup instead of duplicating
    /// <see cref="SymbolHelper.GetNameOverride"/> in every bridge.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildTypeNamesBySymbol(
        Compilation compilation,
        TargetLanguage target,
        bool assemblyWideTranspile
    )
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var currentAssembly = compilation.Assembly;

        void RegisterSymbol(INamedTypeSymbol type) =>
            map.TryAdd(
                type.GetCrossAssemblyOriginKey(),
                SymbolHelper.GetNameOverride(type, target) ?? type.Name
            );

        // Visits every top-level type and every nested type so
        // `[Name(target, …)]` overrides on nested declarations end up in
        // the map too. The caller's `register` decides which symbols
        // actually land in the dict.
        void Visit(INamedTypeSymbol type, Action<INamedTypeSymbol> register)
        {
            register(type);
            foreach (var nested in type.GetTypeMembers())
                Visit(nested, register);
        }

        // Current assembly — every transpilable type (matches the target-side
        // DiscoverTranspilableTypes + nested-emission filter) plus every
        // `[Import]` placeholder the public-only walker would see, PLUS
        // every `[NoEmit]` public type so `[Name(target, …)]` overrides on
        // ambient declarations propagate to references. `[NoEmit]` means
        // "no .ts file emits" — it does NOT mean "callers should hallucinate
        // the C# name at reference sites." Without this, a DOM binding stub
        // like `[NoEmit, Name("HTMLElement")] HtmlElement` would surface as
        // `HtmlElement` in generated TS, losing the rename that makes the
        // stub interoperate with lib.dom.d.ts.
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
                Visit(
                    type,
                    sym =>
                    {
                        if (
                            SymbolHelper.IsTranspilable(sym, assemblyWideTranspile, currentAssembly)
                        )
                            RegisterSymbol(sym);
                        else if (
                            sym.DeclaredAccessibility == Accessibility.Public
                            && (
                                SymbolHelper.GetImport(sym) is not null
                                || SymbolHelper.HasNoEmit(sym)
                                || SymbolHelper.HasNoEmit(sym, target)
                                || SymbolHelper.HasExternal(sym)
                                || SymbolHelper.HasErasable(sym)
                            )
                        )
                            RegisterSymbol(sym);
                    }
                )
        );

        // Referenced assemblies that opted into transpilation with an
        // [EmitPackage] for the active target. Mirrors the
        // BuildCrossAssemblyState filter so the same set of emittable
        // types ends up in the dict. `[NoTranspile]` types are excluded
        // (the producer explicitly opted out). `[NoEmit]` types ARE
        // registered so their `[Name]` overrides surface at reference
        // sites on the consumer side — the emission pipeline filters
        // them through its own paths (TranspilableTypeEntries,
        // GuardableTypeKeys), so the dict stays reference-resolution
        // scope. `[Import]` placeholders on the referenced side stay
        // registered under the same rationale.
        foreach (var (asm, _) in EnumerateTranspilableReferencedAssemblies(compilation, target))
        {
            CollectTopLevelTypes(
                asm.GlobalNamespace,
                type =>
                    Visit(
                        type,
                        sym =>
                        {
                            if (sym.DeclaredAccessibility != Accessibility.Public)
                                return;
                            if (SymbolHelper.HasNoTranspile(sym))
                                return;
                            RegisterSymbol(sym);
                        }
                    )
            );
        }

        return map;
    }

    /// <summary>
    /// Collects the assembly-qualified stable full name of every
    /// transpilable top-level type in the current assembly that declares
    /// <c>[GenerateGuard]</c>. Backends pair this set with their own
    /// guard-naming convention (TypeScript: <c>is{Name}</c>) to decide
    /// when a referenced identifier is actually a guard import, without
    /// re-reading the attribute at every call site. Current-assembly /
    /// top-level only — mirrors the top-level transpilable set the
    /// legacy <c>_guardNameToTypeMap</c> init loop walked, minus the
    /// synthetic Program type (C# 9+ top-level statements) which can
    /// never carry <c>[GenerateGuard]</c>.
    /// </summary>
    private static IReadOnlySet<string> BuildGuardableTypeKeys(
        Compilation compilation,
        bool assemblyWideTranspile
    )
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var currentAssembly = compilation.Assembly;

        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
            {
                if (!SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
                    return;
                if (!SymbolHelper.HasGenerateGuard(type))
                    return;
                keys.Add(type.GetCrossAssemblyOriginKey());
            }
        );

        return keys;
    }

    /// <summary>
    /// Projects every current-assembly top-level transpilable type to an
    /// <see cref="IrTranspilableTypeRef"/> and indexes it under both its
    /// C# source name and its target-facing TS name (when
    /// <c>[Name(target, …)]</c> diverges). Lets the backend resolve a
    /// bare identifier walked out of the generated AST into emit
    /// metadata — origin key, namespace, on-disk file name, string-enum
    /// flag — without going back to the Roslyn symbol table. Current
    /// assembly, top-level only, nested types excluded. The synthetic
    /// C# 9+ top-level <c>Program</c> type is included under
    /// <c>[assembly: TranspileAssembly]</c> so the target's import
    /// collector resolves module-entry references consistently with
    /// every other transpilable type.
    /// </summary>
    private static IReadOnlyDictionary<string, IrTranspilableTypeRef> BuildTranspilableTypes(
        Compilation compilation,
        TargetLanguage target,
        bool assemblyWideTranspile
    )
    {
        var map = new Dictionary<string, IrTranspilableTypeRef>(StringComparer.Ordinal);
        var currentAssembly = compilation.Assembly;

        void Register(INamedTypeSymbol type)
        {
            var tsName = SymbolHelper.GetNameOverride(type, target) ?? type.Name;
            var ns = type.ContainingNamespace.IsGlobalNamespace
                ? ""
                : type.ContainingNamespace.ToDisplayString();
            var emitInFile = SymbolHelper.GetEmitInFile(type);
            var fileBase = emitInFile is { Length: > 0 } ? emitInFile : tsName;
            var fileName = SymbolHelper.ToKebabCase(fileBase);
            var entry = new IrTranspilableTypeRef(
                Key: type.GetCrossAssemblyOriginKey(),
                TsName: tsName,
                Namespace: ns,
                FileName: fileName,
                IsStringEnum: SymbolHelper.HasStringEnum(type)
            );
            // Last-wins on bare-identifier collision — matches the legacy
            // `_transpilableTypeMap[...] = ...` assignments the target side
            // ran inline. Two top-level types in different namespaces with
            // the same simple name, or an alias that shadows another type's
            // source name, stay resolvable (to whichever registered last)
            // instead of silently dropping the second entry.
            map[type.Name] = entry;
            if (tsName != type.Name)
                map[tsName] = entry;
        }

        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
            {
                if (!SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
                    return;
                Register(type);
            }
        );

        if (
            assemblyWideTranspile
            && TryGetTopLevelEntryPoint(compilation, out _, out var programType)
        )
            Register(programType);

        return map;
    }

    /// <summary>
    /// Builds the ordered list of current-assembly top-level transpilable
    /// types the backend should emit. Replaces the syntax-tree walk
    /// <c>TypeTransformer.DiscoverTranspilableTypes</c> used inline on the
    /// target side. The C# 9+ synthetic <c>Program</c> type is appended
    /// when <paramref name="entryPoint"/> is non-null and
    /// <c>[assembly: TranspileAssembly]</c> is set — matching the retired
    /// target-side logic exactly, so the grouping / routing loop in
    /// <c>TransformAll</c> sees the same types in the same order.
    /// </summary>
    private static IReadOnlyList<IrTranspilableTypeEntry> BuildTranspilableTypeEntries(
        Compilation compilation,
        bool assemblyWideTranspile,
        IrEntryPointInfo? entryPoint
    )
    {
        var entries = new List<IrTranspilableTypeEntry>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var currentAssembly = compilation.Assembly;

        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
            {
                if (!SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
                    return;
                if (!seen.Add(type))
                    return;
                entries.Add(
                    new IrTranspilableTypeEntry(
                        Symbol: type,
                        Key: type.GetCrossAssemblyOriginKey(),
                        IsSyntheticProgram: false
                    )
                );
            }
        );

        // Synthetic Program appended last, matching the retired
        // DiscoverTranspilableTypes ordering. De-dupe against `seen` so an
        // explicit `[Transpile] class Program` doesn't collide with the
        // synthetic routing flag.
        if (entryPoint is not null && seen.Add(entryPoint.ContainingType))
        {
            entries.Add(
                new IrTranspilableTypeEntry(
                    Symbol: entryPoint.ContainingType,
                    Key: entryPoint.ContainingType.GetCrossAssemblyOriginKey(),
                    IsSyntheticProgram: true
                )
            );
        }

        return entries;
    }

    /// <summary>
    /// Walks every transpilable type in the current assembly and emits
    /// <c>MS0010</c> for any parameter or property that carries
    /// <c>[Optional]</c> (from <c>Metano.Annotations.TypeScript</c>) on a
    /// non-nullable type. The attribute is TS-specific but the check
    /// runs on every target because a misuse is a bug regardless of the
    /// active backend — ignoring it under Dart would let a broken
    /// attribute ship to the TS consumer later.
    /// </summary>
    private static void ValidateOptionalAttribute(
        Compilation compilation,
        bool assemblyWideTranspile,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var currentAssembly = compilation.Assembly;
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type => VisitTypeForOptional(type, assemblyWideTranspile, currentAssembly, diagnostics)
        );
    }

    private static void VisitTypeForOptional(
        INamedTypeSymbol type,
        bool assemblyWideTranspile,
        IAssemblySymbol currentAssembly,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (!SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
            return;

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol prop when prop.HasOptional():
                    if (!IsNullableType(prop.Type))
                        diagnostics.Add(OptionalOnNonNullableDiagnostic(prop, "property"));
                    break;
                case IMethodSymbol method:
                    foreach (var p in method.Parameters)
                    {
                        if (!p.HasOptional())
                            continue;
                        if (!IsNullableType(p.Type))
                            diagnostics.Add(OptionalOnNonNullableDiagnostic(p, "parameter"));
                    }
                    break;
            }
        }

        foreach (var nested in type.GetTypeMembers())
            VisitTypeForOptional(nested, assemblyWideTranspile, currentAssembly, diagnostics);
    }

    /// <summary>
    /// Mirrors the inclusion rules
    /// <c>TypeGuardBuilder.GetAllFieldsForGuard</c> applies to
    /// properties. Returns <c>false</c> for implicitly-declared,
    /// static, <c>private</c> / <c>internal</c> / <c>NotApplicable</c>
    /// accessibility, and <c>[Ignore(TypeScript)]</c>-suppressed
    /// members. Used by <c>ValidateDiscriminatorAttribute</c> to
    /// reject discriminators pointing at a member that won't appear
    /// in the emitted TS shape.
    /// </summary>
    private static bool IsGuardVisibleProperty(IPropertySymbol prop)
    {
        if (prop.IsImplicitlyDeclared)
            return false;
        if (prop.IsStatic)
            return false;
        if (SymbolHelper.HasIgnore(prop, TargetLanguage.TypeScript))
            return false;
        return prop.DeclaredAccessibility
            is Accessibility.Public
                or Accessibility.Protected
                or Accessibility.ProtectedOrInternal
                or Accessibility.ProtectedAndInternal;
    }

    private static bool IsNullableType(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated
        || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static MetanoDiagnostic OptionalOnNonNullableDiagnostic(ISymbol symbol, string kind) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.OptionalRequiresNullable,
            $"[Optional] on {kind} {FormatMemberPath(symbol)} requires a nullable C# type. The "
                + $"attribute relies on the TS consumer emitting 'undefined' collapsing to C# "
                + $"'null'; a non-nullable target cannot represent the absent case. Make the "
                + $"type nullable (e.g., 'string?').",
            symbol.Locations.FirstOrDefault()
        );

    /// <summary>
    /// Builds a human-readable path for the diagnostic message. For a
    /// property returns <c>TypeName.PropertyName</c>; for a parameter
    /// returns <c>TypeName.MethodName(paramName)</c> (or
    /// <c>TypeName(paramName)</c> when the containing method is a
    /// constructor). Avoids the <c>.ctor.paramName</c> shape the Roslyn
    /// <see cref="ISymbol.Name"/> pair would produce for constructor
    /// parameters, which is hard to act on.
    /// </summary>
    private static string FormatMemberPath(ISymbol symbol) =>
        symbol switch
        {
            IPropertySymbol prop => $"'{prop.ContainingType?.Name ?? string.Empty}.{prop.Name}'",
            IParameterSymbol { ContainingSymbol: IMethodSymbol method } p => method.MethodKind
            == MethodKind.Constructor
                ? $"'{method.ContainingType?.Name ?? string.Empty}({p.Name})'"
                : $"'{method.ContainingType?.Name ?? string.Empty}.{method.Name}({p.Name})'",
            _ => $"'{symbol.ContainingSymbol?.Name ?? string.Empty}.{symbol.Name}'",
        };

    /// <summary>
    /// Walks every transpilable type carrying
    /// <c>[Discriminator("FieldName")]</c> and emits <c>MS0011</c>
    /// when the referenced field is missing, not a <c>[StringEnum]</c>,
    /// or nullable. The short-circuit guard emission relies on a
    /// present non-null string-valued discriminant; without those
    /// guarantees the generated narrowing would silently miss-fire.
    /// Runs on every target (not just TypeScript) because the
    /// attribute is a validation failure regardless of active backend.
    /// </summary>
    private static void ValidateDiscriminatorAttribute(
        Compilation compilation,
        bool assemblyWideTranspile,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var currentAssembly = compilation.Assembly;
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type =>
                VisitTypeForDiscriminator(type, assemblyWideTranspile, currentAssembly, diagnostics)
        );
    }

    private static void VisitTypeForDiscriminator(
        INamedTypeSymbol type,
        bool assemblyWideTranspile,
        IAssemblySymbol currentAssembly,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (!SymbolHelper.IsTranspilable(type, assemblyWideTranspile, currentAssembly))
            return;

        var fieldName = SymbolHelper.GetDiscriminatorFieldName(type);
        if (fieldName is not null)
        {
            var property = type.GetMembers(fieldName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is null)
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidDiscriminator,
                        $"[Discriminator(\"{fieldName}\")] on '{type.Name}' refers to a property "
                            + $"that doesn't exist on the type. Add a "
                            + $"'{fieldName}' property typed as a [StringEnum], or remove the "
                            + $"attribute.",
                        type.Locations.FirstOrDefault()
                    )
                );
            }
            else if (!IsGuardVisibleProperty(property))
            {
                // The guard emits `v.<field>` access unconditionally when
                // the attribute is present. A private / internal / static
                // / implicitly-declared / [Ignore(TS)] member isn't part
                // of the TS shape the guard walks, so the short-circuit
                // would access a phantom field and reject every payload.
                // Match the inclusion rules
                // (TypeGuardBuilder.IsGuardVisible + static/implicit/
                // Ignore filter) so the discriminator can only point at
                // a member that actually surfaces in the emitted
                // interface.
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidDiscriminator,
                        $"[Discriminator(\"{fieldName}\")] on '{type.Name}' references "
                            + $"'{type.Name}.{fieldName}', which isn't guard-visible (private, "
                            + $"static, implicit, or [Ignore(TypeScript)]-ed). The discriminator "
                            + $"must be a public instance property that participates in the "
                            + $"emitted TS shape so the guard can narrow against it.",
                        property.Locations.FirstOrDefault()
                    )
                );
            }
            else if (IsNullableType(property.Type))
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidDiscriminator,
                        $"[Discriminator(\"{fieldName}\")] on '{type.Name}' references a "
                            + $"nullable field. The discriminant must be present on every "
                            + $"instance so the guard can narrow without a null guard of its "
                            + $"own. Make '{type.Name}.{fieldName}' non-nullable.",
                        property.Locations.FirstOrDefault()
                    )
                );
            }
            else if (!SymbolHelper.HasStringEnum(property.Type))
            {
                // Check after nullability so a nullable StringEnum
                // (which shouldn't reach here but could via unusual
                // patterns) gets the more actionable nullable
                // diagnostic instead.
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidDiscriminator,
                        $"[Discriminator(\"{fieldName}\")] on '{type.Name}' references "
                            + $"'{type.Name}.{fieldName}', which is not a [StringEnum]. The "
                            + $"short-circuit guard relies on a string-valued discriminant so "
                            + $"the narrowing compiles to a direct literal comparison — mark "
                            + $"the enum with [StringEnum] or pick a different field.",
                        property.Locations.FirstOrDefault()
                    )
                );
            }
        }

        foreach (var nested in type.GetTypeMembers())
            VisitTypeForDiscriminator(nested, assemblyWideTranspile, currentAssembly, diagnostics);
    }

    /// <summary>
    /// Walks the current assembly and emits <c>MS0012</c> for every
    /// misuse of <c>[External]</c> (from
    /// <c>Metano.Annotations.TypeScript</c>): applied to a non-static
    /// class, or combined with <c>[Transpile]</c>. Runs regardless of
    /// target since the attribute's invariants are semantic (the
    /// transpiler cannot simultaneously honor "no emission" and "full
    /// emission" on the same type).
    /// </summary>
    private static void ValidateExternalAttribute(
        Compilation compilation,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var currentAssembly = compilation.Assembly;
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type => VisitTypeForExternal(type, diagnostics)
        );
    }

    private static void VisitTypeForExternal(
        INamedTypeSymbol type,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (type.HasExternal())
        {
            if (!type.IsStatic)
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidExternal,
                        $"[External] on '{type.Name}' requires a static class. The attribute "
                            + $"marks a stub for runtime globals — non-static types have no "
                            + $"static surface to declare. Mark the class 'static' or remove "
                            + $"[External].",
                        type.Locations.FirstOrDefault()
                    )
                );
            }
            if (SymbolHelper.HasTranspile(type))
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidExternal,
                        $"[External] on '{type.Name}' conflicts with [Transpile]. "
                            + $"[External] marks a stub for runtime globals (no emission); "
                            + $"[Transpile] asks for full emission. Pick one — ambient "
                            + $"bindings drop [Transpile], emitted helpers drop [External].",
                        type.Locations.FirstOrDefault()
                    )
                );
            }
        }

        if (SymbolHelper.HasErasable(type))
        {
            if (!type.IsStatic)
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidErasable,
                        $"[Erasable] on '{type.Name}' requires a static class. The attribute "
                            + $"marks a class whose scope vanishes at the call site — "
                            + $"non-static types carry instance state that cannot be erased. "
                            + $"Mark the class 'static' or remove [Erasable].",
                        type.Locations.FirstOrDefault()
                    )
                );
            }
            if (SymbolHelper.HasTranspile(type))
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidErasable,
                        $"[Erasable] on '{type.Name}' conflicts with [Transpile]. "
                            + $"[Erasable] asks for no file emission and call-site scope "
                            + $"erasure; [Transpile] asks for full emission. Pick one.",
                        type.Locations.FirstOrDefault()
                    )
                );
            }
        }

        foreach (var nested in type.GetTypeMembers())
            VisitTypeForExternal(nested, diagnostics);
    }

    /// <summary>
    /// Validates <c>[Inline]</c> from <c>Metano.Annotations</c>. The
    /// attribute's contract is enforced at declaration time:
    /// <list type="bullet">
    ///   <item>Fields must be <c>static readonly</c> with an
    ///   initializer. Instance fields, mutable fields, and fields
    ///   without an initializer have nothing to substitute at the
    ///   call site.</item>
    ///   <item>Properties must be <c>static</c> and expose an
    ///   expression-bodied getter (either the property's own
    ///   <c>=&gt;</c> form or an expression-bodied <c>get</c>
    ///   accessor). Block-bodied getters fall outside the covered
    ///   substitution surface and are deferred to a follow-up
    ///   slice.</item>
    ///   <item>Recursion between <c>[Inline]</c> members
    ///   (<c>A =&gt; B</c>, <c>B =&gt; A</c>, or <c>A =&gt; A</c>)
    ///   is detected via a DFS over each member's initializer and
    ///   surfaces before extraction runs, so downstream lowering
    ///   never sees a chain that would recurse indefinitely.</item>
    /// </list>
    /// All violations raise <c>MS0016 InvalidInline</c>.
    /// </summary>
    private static void ValidateInlineAttribute(
        Compilation compilation,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var currentAssembly = compilation.Assembly;
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type => VisitTypeForInline(type, diagnostics)
        );
        DetectInlineCycles(compilation, diagnostics);
    }

    /// <summary>
    /// Collects every <c>[Inline]</c> member in the compilation and
    /// walks the transitive dependency graph induced by their
    /// initializers. Any member whose chain revisits itself emits
    /// <c>MS0016</c> at the cycle's source.
    /// </summary>
    private static void DetectInlineCycles(
        Compilation compilation,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var inlineMembers = new List<ISymbol>();
        CollectTopLevelTypes(
            compilation.Assembly.GlobalNamespace,
            type => CollectInlineMembers(type, inlineMembers)
        );

        foreach (var member in inlineMembers)
        {
            var visiting = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (DetectCycle(member, compilation, visiting))
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidInline,
                        $"[Inline] on {FormatMemberPath(member)} forms a cycle through "
                            + $"another [Inline] member. Substitution would recurse "
                            + $"indefinitely; break the chain or drop [Inline] on one "
                            + $"of the participants.",
                        member.Locations.FirstOrDefault()
                    )
                );
            }
        }
    }

    private static void CollectInlineMembers(INamedTypeSymbol type, List<ISymbol> sink)
    {
        foreach (var member in type.GetMembers())
        {
            if (!SymbolHelper.HasInline(member))
                continue;
            if (member is IFieldSymbol or IPropertySymbol)
                sink.Add(member);
        }
        foreach (var nested in type.GetTypeMembers())
            CollectInlineMembers(nested, sink);
    }

    private static bool DetectCycle(
        ISymbol start,
        Compilation compilation,
        HashSet<ISymbol> visiting
    )
    {
        if (!visiting.Add(start))
            return true;
        try
        {
            var initializer = TryFindInlineInitializerExpression(start);
            if (initializer is null)
                return false;
            // Cross-assembly guard: an `[Inline]` member pulled from
            // a referenced assembly carries a SyntaxTree that belongs
            // to the declaring compilation, not ours. The cycle walk
            // cannot inspect initializers outside the current
            // compilation — skip them. A follow-up slice covers the
            // cross-assembly case.
            if (!compilation.ContainsSyntaxTree(initializer.SyntaxTree))
                return false;
            var semanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);
            foreach (var identifier in initializer.DescendantNodesAndSelf().OfType<NameSyntax>())
            {
                var referenced = semanticModel.GetSymbolInfo(identifier).Symbol;
                if (referenced is null || !SymbolHelper.HasInline(referenced))
                    continue;
                if (DetectCycle(referenced, compilation, visiting))
                    return true;
            }
            return false;
        }
        finally
        {
            visiting.Remove(start);
        }
    }

    private static ExpressionSyntax? TryFindInlineInitializerExpression(ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax())
            {
                case VariableDeclaratorSyntax declarator
                    when declarator.Initializer?.Value is { } fieldInit:
                    return fieldInit;
                case PropertyDeclarationSyntax prop
                    when prop.ExpressionBody?.Expression is { } arrow:
                    return arrow;
                case PropertyDeclarationSyntax prop
                    when prop.AccessorList?.Accessors is { } accessors:
                    foreach (var accessor in accessors)
                    {
                        if (
                            accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
                            && accessor.ExpressionBody?.Expression is { } body
                        )
                            return body;
                    }
                    break;
            }
        }
        return null;
    }

    private static void VisitTypeForInline(
        INamedTypeSymbol type,
        List<MetanoDiagnostic> diagnostics
    )
    {
        foreach (var member in type.GetMembers())
        {
            if (!SymbolHelper.HasInline(member))
                continue;

            switch (member)
            {
                case IFieldSymbol field:
                    if (!field.IsStatic || !field.IsReadOnly)
                    {
                        diagnostics.Add(
                            new MetanoDiagnostic(
                                MetanoDiagnosticSeverity.Error,
                                DiagnosticCodes.InvalidInline,
                                $"[Inline] on field {FormatMemberPath(field)} requires a "
                                    + $"'static readonly' field with an initializer. "
                                    + $"Instance or mutable fields cannot satisfy the "
                                    + $"substitution contract — the value must be fixed.",
                                field.Locations.FirstOrDefault()
                            )
                        );
                        break;
                    }
                    if (!HasFieldInitializer(field))
                    {
                        diagnostics.Add(
                            new MetanoDiagnostic(
                                MetanoDiagnosticSeverity.Error,
                                DiagnosticCodes.InvalidInline,
                                $"[Inline] on field {FormatMemberPath(field)} requires an "
                                    + $"initializer. Without one there is no expression "
                                    + $"to substitute at the call site.",
                                field.Locations.FirstOrDefault()
                            )
                        );
                    }
                    break;
                case IPropertySymbol property:
                    if (!property.IsStatic)
                    {
                        diagnostics.Add(
                            new MetanoDiagnostic(
                                MetanoDiagnosticSeverity.Error,
                                DiagnosticCodes.InvalidInline,
                                $"[Inline] on property {FormatMemberPath(property)} requires "
                                    + $"a 'static' property. Instance properties cannot be "
                                    + $"substituted at the call site without a receiver.",
                                property.Locations.FirstOrDefault()
                            )
                        );
                        break;
                    }
                    if (!HasExpressionBodiedGetter(property))
                    {
                        diagnostics.Add(
                            new MetanoDiagnostic(
                                MetanoDiagnosticSeverity.Error,
                                DiagnosticCodes.InvalidInline,
                                $"[Inline] on property {FormatMemberPath(property)} requires "
                                    + $"an expression-bodied getter ('=> expression' or "
                                    + $"'get => expression'). Block-bodied accessors are "
                                    + $"outside the covered substitution surface.",
                                property.Locations.FirstOrDefault()
                            )
                        );
                    }
                    break;
                default:
                    diagnostics.Add(
                        new MetanoDiagnostic(
                            MetanoDiagnosticSeverity.Error,
                            DiagnosticCodes.InvalidInline,
                            $"[Inline] on {FormatMemberPath(member)} applies only to "
                                + $"'static readonly' fields and 'static' properties with "
                                + $"an expression-bodied getter.",
                            member.Locations.FirstOrDefault()
                        )
                    );
                    break;
            }
        }

        foreach (var nested in type.GetTypeMembers())
            VisitTypeForInline(nested, diagnostics);
    }

    private static bool HasFieldInitializer(IFieldSymbol field)
    {
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax() is VariableDeclaratorSyntax declarator
                && declarator.Initializer is not null
            )
                return true;
        }
        return false;
    }

    private static bool HasExpressionBodiedGetter(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax decl)
                continue;
            if (decl.ExpressionBody is not null)
                return true;
            if (decl.AccessorList is { } accessorList)
            {
                foreach (var accessor in accessorList.Accessors)
                {
                    if (
                        accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
                        && accessor.ExpressionBody is not null
                    )
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Validates <c>[This]</c> from <c>Metano.Annotations</c>. The
    /// attribute may decorate only the first parameter of a
    /// delegate or method, and never a <c>ref</c> / <c>out</c> /
    /// <c>params</c> slot — any other shape surfaces
    /// <c>MS0018 InvalidThis</c>. The pass walks every type in the
    /// current assembly: custom delegate types (the invoke
    /// method's parameters) and ordinary methods alike.
    /// </summary>
    private static void ValidateThisAttribute(
        Compilation compilation,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var currentAssembly = compilation.Assembly;
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type => VisitTypeForThis(type, diagnostics)
        );
    }

    private static void VisitTypeForThis(INamedTypeSymbol type, List<MetanoDiagnostic> diagnostics)
    {
        // Delegate types expose their parameter list via the synthesized
        // Invoke method — a plain `.GetMembers()` walk over
        // IMethodSymbol covers both ordinary methods and delegate
        // invokes in one pass.
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol method)
                ValidateMethodParametersForThis(method, diagnostics);
        }
        foreach (var nested in type.GetTypeMembers())
            VisitTypeForThis(nested, diagnostics);
    }

    private static void ValidateMethodParametersForThis(
        IMethodSymbol method,
        List<MetanoDiagnostic> diagnostics
    )
    {
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            if (!parameter.HasThis())
                continue;
            if (i != 0)
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidThis,
                        $"[This] on parameter '{parameter.Name}' of "
                            + $"{FormatMemberPath(method)} is only valid on the first "
                            + $"positional parameter. The attribute promotes the first "
                            + $"slot to the synthetic JavaScript 'this' receiver; later "
                            + $"parameters cannot take that role.",
                        parameter.Locations.FirstOrDefault()
                    )
                );
                continue;
            }
            if (
                parameter.RefKind is RefKind.Ref or RefKind.Out or RefKind.RefReadOnly
                || parameter.IsParams
            )
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.InvalidThis,
                        $"[This] on parameter '{parameter.Name}' of "
                            + $"{FormatMemberPath(method)} cannot be combined with "
                            + $"'ref' / 'out' / 'params'. The receiver is passed by "
                            + $"value at the JavaScript boundary.",
                        parameter.Locations.FirstOrDefault()
                    )
                );
            }
        }
    }

    /// <summary>
    /// Validates <c>[Constant]</c> from <c>Metano.Annotations</c>.
    /// Two checks:
    /// <list type="bullet">
    ///   <item>Fields decorated with <c>[Constant]</c> must be
    ///   <c>const</c> or <c>static readonly</c> with an initializer
    ///   Roslyn reduces to a constant
    ///   (<see cref="IFieldSymbol.HasConstantValue"/> for <c>const</c>,
    ///   <see cref="SemanticModel.GetConstantValue(SyntaxNode, CancellationToken)"/>
    ///   applied to the <c>readonly</c> initializer). Mutable fields
    ///   are rejected so downstream lowering can trust the value.</item>
    ///   <item>Every call site whose target method exposes a
    ///   <c>[Constant]</c> parameter must pass a constant-valued
    ///   argument. Literal tokens, <c>const</c> fields/locals, and
    ///   references to a <c>[Constant]</c>-decorated field
    ///   (validated above) all qualify. Call-site walk covers
    ///   <see cref="InvocationExpressionSyntax"/>,
    ///   <see cref="ObjectCreationExpressionSyntax"/>,
    ///   <see cref="ImplicitObjectCreationExpressionSyntax"/>, and
    ///   <see cref="ConstructorInitializerSyntax"/>
    ///   (<c>: this(...)</c> / <c>: base(...)</c>) so every
    ///   constructor path is covered.</item>
    /// </list>
    /// Both failures raise <c>MS0014 InvalidConstant</c>. The checks
    /// run once per compilation and span every syntax tree reachable
    /// from the current assembly so referenced-assembly consumers
    /// still surface their own violations.
    /// </summary>
    private static void ValidateConstantAttribute(
        Compilation compilation,
        bool assemblyWideTranspile,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var currentAssembly = compilation.Assembly;

        // Field-level check: decorated field initializer must reduce
        // to a Roslyn constant.
        CollectTopLevelTypes(
            currentAssembly.GlobalNamespace,
            type => VisitTypeForConstantFields(type, compilation, diagnostics)
        );

        // Call-site check: walk every invocation + object-creation
        // + constructor-initializer across the compilation's syntax
        // trees and verify args passed to `[Constant]` parameters
        // are constant-valued.
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                ValidateConstantCallSite(
                    invocation.ArgumentList,
                    semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol,
                    semanticModel,
                    compilation,
                    diagnostics
                );

            foreach (
                var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            )
                ValidateConstantCallSite(
                    creation.ArgumentList,
                    semanticModel.GetSymbolInfo(creation).Symbol as IMethodSymbol,
                    semanticModel,
                    compilation,
                    diagnostics
                );

            foreach (
                var creation in root.DescendantNodes()
                    .OfType<ImplicitObjectCreationExpressionSyntax>()
            )
                ValidateConstantCallSite(
                    creation.ArgumentList,
                    semanticModel.GetSymbolInfo(creation).Symbol as IMethodSymbol,
                    semanticModel,
                    compilation,
                    diagnostics
                );

            // Constructor chaining: `: this(...)` and `: base(...)`
            // surface as ConstructorInitializerSyntax. The target
            // constructor's `[Constant]` parameters must still
            // receive literal-valued args.
            foreach (
                var initializer in root.DescendantNodes().OfType<ConstructorInitializerSyntax>()
            )
                ValidateConstantCallSite(
                    initializer.ArgumentList,
                    semanticModel.GetSymbolInfo(initializer).Symbol as IMethodSymbol,
                    semanticModel,
                    compilation,
                    diagnostics
                );
        }
    }

    private static void VisitTypeForConstantFields(
        INamedTypeSymbol type,
        Compilation compilation,
        List<MetanoDiagnostic> diagnostics
    )
    {
        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol field || !field.HasConstant())
                continue;
            if (IsFieldInitializerConstant(field, compilation))
                continue;

            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.InvalidConstant,
                    $"[Constant] on field {FormatMemberPath(field)} requires a 'const' "
                        + $"field or a 'readonly' field whose initializer is itself a "
                        + $"compile-time constant (literal token or 'const' reference). "
                        + $"Mutable fields cannot carry [Constant] — the value must be "
                        + $"fixed for downstream lowering to trust it.",
                    field.Locations.FirstOrDefault()
                )
            );
        }

        foreach (var nested in type.GetTypeMembers())
            VisitTypeForConstantFields(nested, compilation, diagnostics);
    }

    private static bool IsFieldInitializerConstant(IFieldSymbol field, Compilation compilation)
    {
        // `const` fields always satisfy the contract — Roslyn only
        // accepts them when the initializer folds to a primitive.
        if (field.HasConstantValue)
            return true;

        // Mutable fields are rejected. A field that can be reassigned
        // after construction cannot guarantee a compile-time value at
        // every read site, which defeats the purpose of [Constant]
        // as a narrowing/inlining safety net.
        if (!field.IsReadOnly)
            return false;

        // `readonly` fields need a syntax probe: if the declaration's
        // initializer reduces to a Roslyn constant in its own
        // SemanticModel, the value is known at compile time and the
        // attribute's contract is satisfied.
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not VariableDeclaratorSyntax declarator)
                continue;
            if (declarator.Initializer?.Value is not { } initializer)
                continue;
            var semanticModel = compilation.GetSemanticModel(declarator.SyntaxTree);
            if (semanticModel.GetConstantValue(initializer).HasValue)
                return true;
        }
        return false;
    }

    private static void ValidateConstantCallSite(
        BaseArgumentListSyntax? argumentList,
        IMethodSymbol? target,
        SemanticModel semanticModel,
        Compilation compilation,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (argumentList is null || target is null)
            return;
        if (!target.Parameters.Any(p => p.HasConstant()))
            return;

        var arguments = argumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var parameter = ResolveParameter(argument, arguments, i, target);
            if (parameter is null || !parameter.HasConstant())
                continue;
            if (IsConstantArgument(argument.Expression, semanticModel, compilation))
                continue;

            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.InvalidConstant,
                    $"Argument passed to [Constant] parameter '{parameter.Name}' of "
                        + $"{FormatMemberPath(target)} is not a compile-time constant. "
                        + $"Use a literal, a 'const' field/local, or a reference to a "
                        + $"[Constant]-decorated 'readonly' field whose initializer is "
                        + $"itself constant.",
                    argument.GetLocation()
                )
            );
        }
    }

    /// <summary>
    /// Accepts as "constant" either a Roslyn-reducible expression
    /// (literal token, <c>const</c> field/local) or a reference to a
    /// field carrying <c>[Constant]</c>. The second path piggybacks
    /// on <see cref="IsFieldInitializerConstant"/>: if a
    /// <c>readonly</c> field qualifies for the field-level
    /// validation, it is safe to pass on as a <c>[Constant]</c>
    /// argument. Roslyn's <see cref="SemanticModel.GetConstantValue"/>
    /// does not fold <c>readonly</c> field references, so the extra
    /// resolution happens here rather than in the core check.
    /// </summary>
    private static bool IsConstantArgument(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        Compilation compilation
    )
    {
        if (semanticModel.GetConstantValue(expression).HasValue)
            return true;
        if (
            semanticModel.GetSymbolInfo(expression).Symbol is IFieldSymbol field
            && field.HasConstant()
            && IsFieldInitializerConstant(field, compilation)
        )
            return true;
        return false;
    }

    private static IParameterSymbol? ResolveParameter(
        ArgumentSyntax argument,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int positionalIndex,
        IMethodSymbol target
    )
    {
        // Named argument: look the parameter up by its identifier.
        if (argument.NameColon?.Name.Identifier.ValueText is { } named)
            return target.Parameters.FirstOrDefault(p => p.Name == named);
        // Positional argument: map by position, clamping at the last
        // parameter so variadic (`params`) slots resolve to the array
        // parameter rather than walking off the end.
        if (target.Parameters.Length == 0)
            return null;
        var index = Math.Min(positionalIndex, target.Parameters.Length - 1);
        return target.Parameters[index];
    }

    /// <summary>
    /// Detects the C# 9+ top-level-statement entry point and returns the
    /// synthesized method + containing type. Returns <c>null</c> when
    /// <c>[assembly: TranspileAssembly]</c> is absent, the compilation has
    /// no <c>GlobalStatementSyntax</c>, Roslyn reports no entry point, or
    /// the containing type opts out via <c>[ExportedAsModule]</c>. Reuses
    /// <see cref="TryGetTopLevelEntryPoint"/> so the detection logic stays
    /// in one place — a single <c>GetEntryPoint</c> call services every
    /// caller.
    /// </summary>
    private static IrEntryPointInfo? BuildEntryPointInfo(
        Compilation compilation,
        bool assemblyWideTranspile
    )
    {
        if (!assemblyWideTranspile)
            return null;
        if (!TryGetTopLevelEntryPoint(compilation, out var entryPointMethod, out var programType))
            return null;

        return new IrEntryPointInfo(Method: entryPointMethod!, ContainingType: programType);
    }

    /// <summary>
    /// Reads <c>[assembly: MapMethod]</c> / <c>[assembly: MapProperty]</c>
    /// declarations from the current assembly plus every referenced
    /// assembly and indexes them by
    /// (<see cref="SymbolHelper.GetStableFullName"/>, member-name). Also
    /// computes the per-wrapper set of mapped JS method names so the target
    /// can recognize already-wrapped receivers. Mirrors the legacy
    /// <c>DeclarativeMappingRegistry.BuildFromCompilation</c> attribute
    /// walk, dropping the symbol-keyed dictionaries whose Roslyn consumers
    /// retired with ADR-0013.
    /// </summary>
    private static (
        IReadOnlyDictionary<
            (string DeclaringTypeFullName, string MemberName),
            IReadOnlyList<DeclarativeMappingEntry>
        > Methods,
        IReadOnlyDictionary<
            (string DeclaringTypeFullName, string MemberName),
            DeclarativeMappingEntry
        > Properties,
        IReadOnlyDictionary<string, IReadOnlySet<string>> ChainMethodsByWrapper
    ) BuildDeclarativeMappings(Compilation compilation, List<MetanoDiagnostic> diagnostics)
    {
        var methods = new Dictionary<(string, string), List<DeclarativeMappingEntry>>();
        var properties = new Dictionary<(string, string), DeclarativeMappingEntry>();

        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                assemblies.Add(assembly);
        }

        foreach (var assembly in assemblies)
        {
            foreach (var attr in assembly.GetAttributes())
            {
                switch (attr.AttributeClass?.Name)
                {
                    case "MapMethodAttribute":
                        TryRegisterMapping(attr, "JsMethod", methods, diagnostics);
                        break;
                    case "MapPropertyAttribute":
                        TryRegisterProperty(attr, "JsProperty", properties, diagnostics);
                        break;
                }
            }
        }

        var chainMethodsByWrapper = new Dictionary<string, HashSet<string>>();
        foreach (var entries in methods.Values)
        {
            foreach (var entry in entries)
            {
                if (entry.WrapReceiver is null || entry.JsName is null)
                    continue;
                if (!chainMethodsByWrapper.TryGetValue(entry.WrapReceiver, out var set))
                {
                    set = [];
                    chainMethodsByWrapper[entry.WrapReceiver] = set;
                }
                set.Add(entry.JsName);
            }
        }

        var readonlyMethods = methods.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DeclarativeMappingEntry>)kv.Value
        );
        var readonlyChains = chainMethodsByWrapper.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<string>)kv.Value
        );

        return (readonlyMethods, properties, readonlyChains);
    }

    private static void TryRegisterMapping(
        AttributeData attr,
        string renameNamedArg,
        Dictionary<(string, string), List<DeclarativeMappingEntry>> target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var key = ReadMappingKey(attr);
        if (key is null)
            return;

        var entry = ReadMappingEntry(attr, renameNamedArg, diagnostics);
        if (entry is null)
            return;

        if (!target.TryGetValue(key.Value, out var list))
        {
            list = [];
            target[key.Value] = list;
        }
        list.Add(entry);
    }

    private static void TryRegisterProperty(
        AttributeData attr,
        string renameNamedArg,
        Dictionary<(string, string), DeclarativeMappingEntry> target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var key = ReadMappingKey(attr);
        if (key is null)
            return;

        var entry = ReadMappingEntry(attr, renameNamedArg, diagnostics);
        if (entry is null)
            return;

        target[key.Value] = entry;
    }

    private static (string DeclaringTypeFullName, string MemberName)? ReadMappingKey(
        AttributeData attr
    )
    {
        if (attr.ConstructorArguments.Length < 2)
            return null;
        if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol declaringType)
            return null;
        if (attr.ConstructorArguments[1].Value is not string memberName)
            return null;
        return (declaringType.GetStableFullName(), memberName);
    }

    private static DeclarativeMappingEntry? ReadMappingEntry(
        AttributeData attr,
        string renameNamedArg,
        List<MetanoDiagnostic> diagnostics
    )
    {
        string? jsName = null;
        string? jsTemplate = null;
        string? whenArg0StringEquals = null;
        string? wrapReceiver = null;
        string? runtimeImports = null;
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case var k when k == renameNamedArg:
                    jsName = named.Value.Value as string;
                    break;
                case "JsTemplate":
                    jsTemplate = named.Value.Value as string;
                    break;
                case "WhenArg0StringEquals":
                    whenArg0StringEquals = named.Value.Value as string;
                    break;
                case "WrapReceiver":
                    wrapReceiver = named.Value.Value as string;
                    break;
                case "RuntimeImports":
                    runtimeImports = named.Value.Value as string;
                    break;
            }
        }

        if (jsName is null && jsTemplate is null)
            return null;

        // JsName and JsTemplate are mutually exclusive — template takes
        // precedence (it's the more specific lowering form). Surface MS0004
        // so a user who meant only one of the two can spot the conflict.
        if (jsName is not null && jsTemplate is not null)
        {
            var target =
                attr.ConstructorArguments.Length >= 2
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol owner
                && attr.ConstructorArguments[1].Value is string memberName
                    ? $"{owner.ToDisplayString()}.{memberName}"
                    : attr.AttributeClass?.ToDisplayString() ?? "<unknown>";
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.ConflictingAttributes,
                    $"Declarative mapping on '{target}' sets both "
                        + $"{renameNamedArg} ('{jsName}') and JsTemplate ('{jsTemplate}') — "
                        + $"these are mutually exclusive. Keeping JsTemplate and ignoring "
                        + $"{renameNamedArg}.",
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                )
            );
            jsName = null;
        }

        return new DeclarativeMappingEntry(
            jsName,
            jsTemplate,
            whenArg0StringEquals,
            wrapReceiver,
            runtimeImports
        );
    }

    /// <summary>
    /// Longest common namespace prefix across <paramref name="types"/>,
    /// skipping types declared in the global namespace so a single
    /// unnamespaced entry cannot collapse the shared root on its own.
    /// </summary>
    private static string ComputeRootNamespaceFromTypes(IEnumerable<INamedTypeSymbol> types)
    {
        var namespaces = new List<string>();
        foreach (var type in types)
        {
            if (type.ContainingNamespace.IsGlobalNamespace)
                continue;
            namespaces.Add(type.ContainingNamespace.ToDisplayString());
        }
        return NamespaceUtilities.FindCommonPrefix(namespaces);
    }

    /// <summary>
    /// Walks <see cref="Compilation.References"/> and partitions every
    /// referenced assembly that opted into transpilation
    /// (<c>[TranspileAssembly]</c>) into one of two buckets:
    /// <list type="bullet">
    ///   <item>If it also declares <c>[EmitPackage]</c> for the active
    ///   target, every public top-level type that is not <c>[Import]</c>,
    ///   <c>[NoEmit]</c>, or <c>[NoTranspile]</c> contributes an
    ///   <see cref="IrTypeOrigin"/> keyed by
    ///   <see cref="SymbolHelper.GetCrossAssemblyOriginKey(ITypeSymbol)"/>.</item>
    ///   <item>Otherwise the assembly's metadata name is collected so the
    ///   target can later raise <see cref="DiagnosticCodes.CrossPackageResolution"/>
    ///   (MS0007) at the consumer site.</item>
    /// </list>
    /// <paramref name="target"/> drives the per-target <c>[NoEmit]</c>
    /// filter so a Dart-specific <c>[NoEmit]</c> cannot poison the
    /// TypeScript origin table (and vice versa).
    /// </summary>
    private static (
        IReadOnlyDictionary<string, IrTypeOrigin> CrossAssemblyOrigins,
        IReadOnlySet<string> AssembliesNeedingEmitPackage
    ) BuildCrossAssemblyState(Compilation compilation, TargetLanguage target)
    {
        var origins = new Dictionary<string, IrTypeOrigin>(StringComparer.Ordinal);
        var needingPackage = new HashSet<string>(StringComparer.Ordinal);

        foreach (
            var (asm, packageInfo) in EnumerateTranspilableReferencedAssemblies(
                compilation,
                target,
                onTranspilableWithoutEmitPackage: name => needingPackage.Add(name)
            )
        )
        {
            var assemblyTypes = new List<INamedTypeSymbol>();
            CollectTopLevelTypes(
                asm.GlobalNamespace,
                type =>
                {
                    if (type.DeclaredAccessibility == Accessibility.Public)
                        assemblyTypes.Add(type);
                }
            );

            // Filter the same way the legacy CollectTypesFromNamespace does — anything
            // that wouldn't be discovered for emission must not influence the assembly
            // root namespace either, otherwise an [NoEmit] type sitting in an unrelated
            // namespace would silently shrink the prefix used for import subpaths.
            var emittedAssemblyTypes = assemblyTypes
                .Where(type =>
                    SymbolHelper.GetImport(type) is null
                    && !SymbolHelper.HasNoTranspile(type)
                    && !SymbolHelper.HasNoEmit(type, target)
                )
                .ToList();

            var rootNamespace = ComputeRootNamespaceFromTypes(emittedAssemblyTypes);

            foreach (var type in emittedAssemblyTypes)
            {
                origins[type.GetCrossAssemblyOriginKey()] = new IrTypeOrigin(
                    PackageId: packageInfo.Name,
                    Namespace: type.ContainingNamespace.IsGlobalNamespace
                        ? null
                        : type.ContainingNamespace.ToDisplayString(),
                    AssemblyRootNamespace: rootNamespace,
                    VersionHint: packageInfo.Version
                );
            }
        }

        return (origins, needingPackage);
    }

    /// <summary>
    /// Walks every public top-level type in the current compilation plus
    /// every referenced assembly that opted into transpilation
    /// (<c>[TranspileAssembly]</c>) and declared an <c>[EmitPackage]</c>
    /// for the active target, surfacing those carrying
    /// <c>[Import("name", from)]</c> as <see cref="IrExternalImport"/>
    /// entries keyed by the C# type's simple source name <em>and</em> by
    /// the per-target <c>[Name(target, …)]</c> alias when it differs —
    /// both keys point at the same <see cref="IrExternalImport"/> value so
    /// the backend can resolve imports by whichever name it is holding
    /// (source, emitted, or either after rename). Retires the duplicate
    /// walker that used to live in <c>TypeTransformer.RegisterTsNameAliases</c>.
    /// The local assembly is walked first so on collision its entry wins,
    /// matching the legacy ordering in
    /// <c>TypeTransformer.DiscoverCrossAssemblyTypes</c>.
    /// <para>
    /// Conflict policy: first mapping wins and any divergent
    /// re-registration produces a
    /// <see cref="DiagnosticCodes.AmbiguousConstruct"/> warning surfaced
    /// through <see cref="IrCompilation.Diagnostics"/>.
    /// </para>
    /// </summary>
    private static Dictionary<string, IrExternalImport> BuildExternalImports(
        Compilation compilation,
        TargetLanguage target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var map = new Dictionary<string, IrExternalImport>(StringComparer.Ordinal);

        AddImportsFromAssembly(compilation.Assembly, target, map, diagnostics);

        foreach (var (asm, _) in EnumerateTranspilableReferencedAssemblies(compilation, target))
            AddImportsFromAssembly(asm, target, map, diagnostics);

        return map;
    }

    /// <summary>
    /// Yields every referenced assembly (excluding the one being compiled)
    /// that opted into transpilation via <c>[TranspileAssembly]</c> and
    /// declared an <c>[EmitPackage]</c> for the active target. When
    /// <paramref name="onTranspilableWithoutEmitPackage"/> is supplied it
    /// receives the metadata name of every assembly that opted in but
    /// omitted <c>[EmitPackage]</c> — callers that need to surface that
    /// case (MS0007 bookkeeping in
    /// <see cref="BuildCrossAssemblyState"/>) pass a collector; callers
    /// that just want the fully-configured set (import aggregation) leave
    /// it null.
    /// </summary>
    private static IEnumerable<(
        IAssemblySymbol Assembly,
        SymbolHelper.EmitPackageInfo PackageInfo
    )> EnumerateTranspilableReferencedAssemblies(
        Compilation compilation,
        TargetLanguage target,
        Action<string>? onTranspilableWithoutEmitPackage = null
    )
    {
        var emitTargetValue = (int)target.ToEmitTarget();
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                continue;
            if (SymbolEqualityComparer.Default.Equals(asm, compilation.Assembly))
                continue;

            var hasTranspileAssembly = asm.GetAttributes()
                .Any(a =>
                    a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly"
                );
            if (!hasTranspileAssembly)
                continue;

            var packageInfo = SymbolHelper.GetEmitPackageInfo(asm, emitTargetValue);
            if (packageInfo is null)
            {
                onTranspilableWithoutEmitPackage?.Invoke(asm.Name);
                continue;
            }

            yield return (asm, packageInfo);
        }
    }

    /// <summary>
    /// Walks <paramref name="assembly"/>'s public top-level types and
    /// registers any <c>[Import]</c>-annotated entry into
    /// <paramref name="map"/> under the C# source name plus, when a
    /// <c>[Name(target, …)]</c> alias for the active
    /// <paramref name="target"/> differs from the source name, that alias
    /// too. Both keys point at the same <see cref="IrExternalImport"/>
    /// value so the backend can look the import up by whichever name it
    /// is holding. A <c>[Name(…)]</c> targeted at any language other than
    /// <paramref name="target"/> is deliberately ignored on this pass.
    /// Mirrors the legacy <c>TypeTransformer.RegisterExternalImportMapping</c>
    /// conflict policy (first mapping wins; collisions produce MS0003).
    /// </summary>
    private static void AddImportsFromAssembly(
        IAssemblySymbol assembly,
        TargetLanguage target,
        Dictionary<string, IrExternalImport> map,
        List<MetanoDiagnostic> diagnostics
    )
    {
        CollectTopLevelTypes(
            assembly.GlobalNamespace,
            type =>
            {
                if (type.DeclaredAccessibility != Accessibility.Public)
                    return;

                var import = SymbolHelper.GetImport(type);
                if (import is null)
                    return;

                var entry = new IrExternalImport(
                    Name: import.Name,
                    From: import.From,
                    IsDefault: import.AsDefault,
                    Version: import.Version
                );

                // Register under the C# source name first so it remains
                // the canonical lookup key for callers that only know the
                // C# identifier.
                TryRegisterImport(type.Name, entry, type, map, diagnostics);

                // Additionally register the per-target [Name(target, …)]
                // alias so the backend can resolve an import by the
                // emitted identifier without re-reading Roslyn. Skip when
                // the alias collapses to the source name (no-op) or is
                // absent.
                var targetName = SymbolHelper.GetNameOverride(type, target);
                if (targetName is not null && targetName != type.Name)
                    TryRegisterImport(targetName, entry, type, map, diagnostics);
            }
        );
    }

    /// <summary>
    /// Inserts <paramref name="entry"/> into <paramref name="map"/> under
    /// <paramref name="key"/>, enforcing the first-wins conflict policy:
    /// a value-equal re-registration is a silent no-op (the same
    /// <see cref="IrExternalImport"/> can legitimately land twice — once
    /// under the source name and once under its per-target alias), while
    /// any divergent re-registration is reported as MS0003 and dropped.
    /// </summary>
    private static void TryRegisterImport(
        string key,
        IrExternalImport entry,
        INamedTypeSymbol owner,
        Dictionary<string, IrExternalImport> map,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (!map.TryGetValue(key, out var existing))
        {
            map[key] = entry;
            return;
        }

        if (existing == entry)
            return;

        diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousConstruct,
                $"External import name collision for '{key}'. Keeping "
                    + $"'{existing.From}' ('{existing.Name}') and ignoring "
                    + $"conflicting mapping from '{owner.ToDisplayString()}' to "
                    + $"'{entry.From}' ('{entry.Name}').",
                owner.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// Recursively walks every top-level type (i.e. non-nested) declared
    /// under <paramref name="ns"/>, regardless of accessibility, and
    /// passes each to <paramref name="visit"/>. Callers that only want
    /// public types (BCL-wide transpile, cross-assembly emission) filter
    /// via the visitor; callers that need to see internal
    /// <c>[Transpile]</c> types (local root-namespace computation) leave
    /// the visitor unfiltered.
    /// </summary>
    private static void CollectTopLevelTypes(INamespaceSymbol ns, Action<INamedTypeSymbol> visit)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    CollectTopLevelTypes(childNs, visit);
                    break;
                case INamedTypeSymbol type when type.ContainingType is null:
                    visit(type);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads <c>[ExportFromBcl]</c> declarations from every assembly in scope
    /// (referenced first, current assembly last so user-side overrides win on
    /// conflict) and returns them keyed by the BCL type's display string.
    /// Mirrors the legacy <c>TypeTransformer.LoadBclExportMappings</c>; the
    /// target-side copy stays in place until the contract flip migrates
    /// consumers onto the IR.
    /// </summary>
    private static Dictionary<string, IrBclExport> BuildBclExports(Compilation compilation)
    {
        var map = new Dictionary<string, IrBclExport>(StringComparer.Ordinal);

        foreach (var reference in compilation.References)
        {
            if (
                compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm
                && !SymbolEqualityComparer.Default.Equals(asm, compilation.Assembly)
            )
                CollectBclExportsFromAssembly(asm, map);
        }
        CollectBclExportsFromAssembly(compilation.Assembly, map);

        return map;
    }

    private static void CollectBclExportsFromAssembly(
        IAssemblySymbol assembly,
        Dictionary<string, IrBclExport> sink
    )
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("ExportFromBclAttribute" or "ExportFromBcl"))
                continue;

            if (attr.ConstructorArguments.Length == 0)
                continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol typeArg)
                continue;

            var exportedName = "";
            var fromPackage = "";
            string? version = null;

            foreach (var namedArg in attr.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "ExportedName":
                        exportedName = namedArg.Value.Value?.ToString() ?? "";
                        break;
                    case "FromPackage":
                        fromPackage = namedArg.Value.Value?.ToString() ?? "";
                        break;
                    case "Version":
                        var raw = namedArg.Value.Value?.ToString();
                        version = string.IsNullOrEmpty(raw) ? null : raw;
                        break;
                }
            }

            if (exportedName.Length == 0)
                continue;

            sink[typeArg.ToDisplayString()] = new IrBclExport(exportedName, fromPackage, version);
        }
    }

    /// <summary>
    /// Opens the project via <see cref="MSBuildWorkspace"/> and returns
    /// the resulting <see cref="Compilation"/>. Mirrors the previous
    /// <c>TranspilerHost.LoadCompilationAsync</c>: progress and errors
    /// also go to stdout/stderr so the CLI trace stays unchanged, but
    /// every failure is additionally appended to <paramref name="diagnostics"/>
    /// as a <see cref="DiagnosticCodes.FrontendLoadFailure"/> entry so
    /// the host (and any future programmatic caller) can react via
    /// <see cref="IrCompilation.Diagnostics"/>.
    /// </summary>
    private static async Task<(Compilation? Compilation, int ErrorCount)> LoadCompilationAsync(
        string projectPath,
        List<MetanoDiagnostic> diagnostics,
        CancellationToken ct
    )
    {
        if (!File.Exists(projectPath))
        {
            var message = $"Project not found: {projectPath}";
            Console.Error.WriteLine(message);
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.FrontendLoadFailure,
                    message
                )
            );
            return (null, 1);
        }

        Console.WriteLine($"Metano: Loading project {Path.GetFileName(projectPath)}...");

        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                var message = $"Workspace error: {e.Diagnostic.Message}";
                Console.Error.WriteLine($"  {message}");
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.FrontendLoadFailure,
                        message
                    )
                );
            }
        });

        Console.WriteLine("  Opening MSBuild project...");
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);

        Console.WriteLine("  Project loaded.");
        Console.WriteLine("  Creating Roslyn compilation...");
        var compilation = await project.GetCompilationAsync(ct);

        Console.WriteLine("  Compilation created.");

        if (compilation is null)
        {
            const string message = "Failed to compile project.";
            Console.Error.WriteLine(message);
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.FrontendLoadFailure,
                    message
                )
            );
            return (null, 1);
        }

        var roslynErrors = compilation
            .GetDiagnostics(ct)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (roslynErrors.Count > 0)
        {
            Console.Error.WriteLine($"Compilation has {roslynErrors.Count} error(s):");
            foreach (var error in roslynErrors.Take(10))
            {
                Console.Error.WriteLine($"  {error}");
            }

            foreach (var error in roslynErrors)
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Error,
                        DiagnosticCodes.FrontendLoadFailure,
                        error.GetMessage(),
                        error.Location
                    )
                );
            }

            return (null, roslynErrors.Count);
        }

        return (compilation, 0);
    }
}
