using Metano.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler;

/// <summary>
/// Target-agnostic helpers for reading Metano attributes from Roslyn symbols and
/// performing common name conversions used by the file system layout (kebab-case).
///
/// Methods that are TypeScript/JavaScript-specific (camelCase identifiers, JS reserved
/// words, [Emit] string templates) live in the TypeScript target instead.
/// </summary>
public static class SymbolHelper
{
    /// <summary>
    /// Stable display format used when the same type symbol must produce the
    /// same string in two unrelated places — for instance, both
    /// <c>DeclarativeMappingRegistry</c> and <c>IrExpressionExtractor</c> key
    /// off the type's "full name" to join a BCL mapping with the call site
    /// that needs it. Pinning the format here keeps the round-trip
    /// deterministic across Roslyn versions and display-option defaults.
    /// </summary>
    public static readonly SymbolDisplayFormat StableTypeFullNameFormat =
        SymbolDisplayFormat.CSharpErrorMessageFormat;

    /// <summary>
    /// Returns the type's open-generic full name in a format stable enough
    /// to use as a dictionary key across the IR pipeline (registry build /
    /// IR origin extraction).
    /// </summary>
    public static string GetStableFullName(this ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString(StableTypeFullNameFormat);

    /// <summary>
    /// Returns the key used by
    /// <see cref="IR.IrCompilation.CrossAssemblyOrigins"/> to store an
    /// <see cref="IR.IrTypeOrigin"/> for a type that lives in a referenced
    /// transpilable assembly. The key is assembly-qualified
    /// (<c>"{assemblyName}:{stableFullName}"</c>) so two referenced
    /// assemblies that happen to expose types with identical stable full
    /// names cannot silently clobber each other's origin entry.
    /// </summary>
    public static string GetCrossAssemblyOriginKey(this ITypeSymbol type) =>
        $"{type.ContainingAssembly?.Name ?? string.Empty}:{type.GetStableFullName()}";

    public static bool HasAttribute(this ISymbol symbol, string attributeName)
    {
        return symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name == attributeName
                || a.AttributeClass?.Name == attributeName + "Attribute"
            );
    }

    /// <summary>
    /// Reads a symbol's <c>[Name]</c> override. Multiple <c>[Name]</c>
    /// attributes can coexist on the same symbol — at most one untargeted
    /// plus at most one per <see cref="TargetLanguage"/> — so resolution
    /// picks the best match:
    /// <list type="number">
    ///   <item>A <c>[Name(target, "…")]</c> with a matching target wins.</item>
    ///   <item>Otherwise the untargeted <c>[Name("…")]</c> (if any).</item>
    ///   <item>Otherwise <c>null</c>.</item>
    /// </list>
    /// </summary>
    public static string? GetNameOverride(this ISymbol symbol, TargetLanguage? target = null)
    {
        string? untargeted = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("NameAttribute" or "Name"))
                continue;
            if (attr.ConstructorArguments.Length == 0)
                continue;

            // Two constructor shapes exist: (string name) → untargeted; and
            // (TargetLanguage target, string name) → per-target. Roslyn surfaces
            // the enum as its backing integer value in ConstructorArguments.
            if (
                attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string onlyName
            )
            {
                untargeted = onlyName;
                continue;
            }

            if (
                attr.ConstructorArguments.Length >= 2
                && attr.ConstructorArguments[0].Value is int targetValue
                && attr.ConstructorArguments[1].Value is string perTargetName
                && target is TargetLanguage wanted
                && (int)wanted == targetValue
            )
            {
                // Exact match — early return, no need to scan further.
                return perTargetName;
            }
        }
        return untargeted;
    }

    public static bool HasTranspile(this ISymbol symbol) => HasAttribute(symbol, "Transpile");

    public static bool HasStringEnum(this ISymbol symbol) => HasAttribute(symbol, "StringEnum");

    public static bool HasFlags(this ISymbol symbol) =>
        HasAttribute(symbol, "Flags") || HasAttribute(symbol, "FlagsAttribute");

    /// <summary>
    /// Backwards-compatible overload: returns true when <em>any</em> <c>[Ignore]</c>
    /// is present, targeted or not. Callers that know which backend they are
    /// emitting for should prefer the target-aware overload below so a
    /// <c>[Ignore(TargetLanguage.Dart)]</c> doesn't silently suppress a member
    /// on the TS side.
    /// </summary>
    public static bool HasIgnore(this ISymbol symbol) => HasIgnore(symbol, target: null);

    /// <summary>
    /// Target-aware <c>[Ignore]</c> lookup. Returns true when either an
    /// untargeted <c>[Ignore]</c> is present or a <c>[Ignore(target)]</c> for
    /// the given <paramref name="target"/>. Per-target occurrences for a
    /// different target are treated as absent — they do not suppress the
    /// member on the current target.
    /// </summary>
    public static bool HasIgnore(this ISymbol symbol, TargetLanguage? target) =>
        HasTargetableFlag(symbol, "Ignore", target);

    /// <summary>
    /// Shared matcher for per-target "flag" attributes (<c>[Ignore]</c>,
    /// <c>[NoEmit]</c>, …) that carry only an optional <see cref="TargetLanguage"/>.
    /// <para>Match rules:</para>
    /// <list type="bullet">
    ///   <item>An <em>untargeted</em> occurrence (<c>[Attr]</c>) satisfies every
    ///   caller, regardless of <paramref name="target"/>.</item>
    ///   <item>A <em>targeted</em> occurrence (<c>[Attr(target)]</c>) only satisfies
    ///   callers passing the exact same <paramref name="target"/>. A caller
    ///   passing <c>null</c> (target-agnostic queries such as the legacy
    ///   <see cref="IsTranspilable(this ISymbol, bool, IAssemblySymbol?)"/>)
    ///   does <b>not</b> match a targeted occurrence — otherwise
    ///   <c>[NoEmit(TargetLanguage.Dart)]</c> would suppress TS discovery too.</item>
    /// </list>
    /// </summary>
    private static bool HasTargetableFlag(
        ISymbol symbol,
        string attributeShortName,
        TargetLanguage? target
    )
    {
        var attributeName = attributeShortName + "Attribute";
        foreach (var attr in symbol.GetAttributes())
        {
            if (
                attr.AttributeClass?.Name != attributeShortName
                && attr.AttributeClass?.Name != attributeName
            )
                continue;

            // Untargeted form — constructor with no args. Matches every caller.
            if (attr.ConstructorArguments.Length == 0)
                return true;

            // Targeted form — single TargetLanguage arg (surfaces as int).
            // Only matches a non-null caller that asked for the same target;
            // target-null callers fall through so a Dart-specific flag cannot
            // poison TS discovery paths (see IsTranspilable).
            if (
                attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is int targetValue
                && target is TargetLanguage wanted
                && (int)wanted == targetValue
            )
                return true;
        }
        return false;
    }

    public static bool HasModule(this ISymbol symbol) => HasAttribute(symbol, "Module");

    public static bool HasExportedAsModule(this ISymbol symbol) =>
        HasAttribute(symbol, "ExportedAsModule");

    public static bool HasImport(this ISymbol symbol) => HasAttribute(symbol, "Import");

    public static bool HasEmit(this ISymbol symbol) => HasAttribute(symbol, "Emit");

    public static bool HasGenerateGuard(this ISymbol symbol) =>
        HasAttribute(symbol, "GenerateGuard");

    public static bool HasNoTranspile(this ISymbol symbol) => HasAttribute(symbol, "NoTranspile");

    public static bool HasNoEmit(this ISymbol symbol) => HasNoEmit(symbol, target: null);

    /// <summary>
    /// Target-aware <c>[NoEmit]</c> lookup — same shape as
    /// <see cref="HasIgnore(this ISymbol, TargetLanguage?)"/>.
    /// </summary>
    public static bool HasNoEmit(this ISymbol symbol, TargetLanguage? target) =>
        HasTargetableFlag(symbol, "NoEmit", target);

    public static bool HasModuleEntryPoint(this ISymbol symbol) =>
        HasAttribute(symbol, "ModuleEntryPoint");

    public static bool HasPlainObject(this ISymbol symbol) => HasAttribute(symbol, "PlainObject");

    /// <summary>
    /// Reads <c>[Optional]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. TS-specific
    /// attribute — callers outside the TS target should treat
    /// <c>true</c> as a no-op (the field stays nullable either way).
    /// Matches on the fully-qualified namespace so the unrelated
    /// <c>System.Runtime.InteropServices.OptionalAttribute</c> (which
    /// shares the same short name and is used by COM interop) is not
    /// mistaken for the Metano variant.
    /// </summary>
    public static bool HasOptional(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("OptionalAttribute" or "Optional")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    == "Metano.Annotations.TypeScript"
            );

    /// <summary>
    /// Reads <c>[External]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. TS-specific
    /// attribute marking the symbol as runtime-provided — no
    /// declaration is emitted for it. On a class, call-site access
    /// keeps the class-qualified form (scope-erasure lives on
    /// <see cref="HasErasable"/>). On a member, the declaration is
    /// suppressed but access goes through whatever enclosing
    /// expression holds it. Namespace-qualified match so unrelated
    /// <c>[External]</c> attributes from other libraries are not
    /// mistaken for the Metano variant.
    /// </summary>
    public static bool HasExternal(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("ExternalAttribute" or "External")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    == "Metano.Annotations.TypeScript"
            );

    /// <summary>
    /// Reads <c>[Erasable]</c> from <c>Metano.Annotations</c>. Static
    /// class whose scope vanishes at every call site — no
    /// <c>.ts</c> file, and static member access drops the enclosing
    /// class name (<c>HtmlElementType.Div</c> → <c>Div</c>). Members
    /// inside emit per their own attributes (plain body → top-level
    /// function, <c>[External]</c> → ambient, <c>[Emit]</c> → template,
    /// <c>[Inline]</c> → expansion, <c>[Ignore]</c> → dropped).
    /// Subsumes <c>[ExportedAsModule]</c> (deprecated) and fixes the
    /// latent call-site flatten bug.
    /// </summary>
    public static bool HasErasable(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is ("ErasableAttribute" or "Erasable")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
            );

    /// <summary>
    /// Reads <c>[Discriminator("FieldName")]</c> from the
    /// <c>Metano.Annotations.TypeScript</c> namespace. Returns the
    /// discriminant field name (original C# casing) when the attribute
    /// is present, or <c>null</c> otherwise. Namespace-qualified match
    /// so unrelated <c>[Discriminator]</c> attributes from other
    /// libraries cannot be mistaken for the Metano variant. Callers
    /// outside the TypeScript target should treat a non-null result as
    /// a no-op (Dart / Kotlin have no equivalent narrowing
    /// convention).
    /// </summary>
    public static string? GetDiscriminatorFieldName(this ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Where(a =>
                a.AttributeClass?.Name is ("DiscriminatorAttribute" or "Discriminator")
                && a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    == "Metano.Annotations.TypeScript"
            )
            .Select(a =>
                a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null
            )
            .FirstOrDefault(s => s is not null);

    /// <summary>
    /// Reads the file name from <c>[EmitInFile("name")]</c> on a type symbol, or null
    /// when the attribute isn't present (in which case the type takes its own name as
    /// the file).
    /// </summary>
    public static string? GetEmitInFile(this ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "EmitInFileAttribute" or "EmitInFile");
        if (attr is null || attr.ConstructorArguments.Length == 0)
            return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    /// <summary>
    /// Reads <c>[ExportVarFromBody("name", AsDefault = ?, InPlace = ?)]</c> from a method
    /// symbol. Returns null when the attribute isn't present.
    /// </summary>
    public static ExportVarFromBodyInfo? GetExportVarFromBody(this ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.Name is "ExportVarFromBodyAttribute" or "ExportVarFromBody"
            );
        if (attr is null)
            return null;

        var name =
            attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value?.ToString()
                : null;
        if (name is null)
            return null;

        var asDefault = false;
        var inPlace = false;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "AsDefault" && named.Value.Value is bool ad)
                asDefault = ad;
            else if (named.Key == "InPlace" && named.Value.Value is bool ip)
                inPlace = ip;
        }

        return new ExportVarFromBodyInfo(name, asDefault, inPlace);
    }

    public sealed record ExportVarFromBodyInfo(string Name, bool AsDefault, bool InPlace);

    /// <summary>
    /// Reads the <c>[assembly: EmitPackage("name", target)]</c> declaration from
    /// <paramref name="assembly"/> for the requested <paramref name="target"/>. Returns
    /// the package info (name + optional version override) on a match, or <c>null</c>
    /// when no matching attribute exists. Multiple <c>[EmitPackage]</c> instances are
    /// supported (one per target); the first one whose <c>Target</c> matches wins.
    /// </summary>
    /// <param name="targetEnumValue">Integer value of the EmitTarget enum (matches the
    /// underlying value the attribute was constructed with). Pass 0 for JavaScript.</param>
    public static EmitPackageInfo? GetEmitPackageInfo(IAssemblySymbol assembly, int targetEnumValue)
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("EmitPackageAttribute" or "EmitPackage"))
                continue;

            // Constructor: (string name, EmitTarget target = JavaScript)
            if (attr.ConstructorArguments.Length == 0)
                continue;
            var name = attr.ConstructorArguments[0].Value as string;
            if (string.IsNullOrEmpty(name))
                continue;

            // Target arg may be omitted (default = JavaScript = 0) or present.
            var target = 0;
            if (attr.ConstructorArguments.Length > 1 && attr.ConstructorArguments[1].Value is int t)
                target = t;
            if (target != targetEnumValue)
                continue;

            string? version = null;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Version" && named.Value.Value is string v && v.Length > 0)
                    version = v;
            }

            return new EmitPackageInfo(name, version);
        }
        return null;
    }

    /// <summary>
    /// Convenience overload that returns just the package name (or null) for callers
    /// that don't care about the version override.
    /// </summary>
    public static string? GetEmitPackage(IAssemblySymbol assembly, int targetEnumValue) =>
        GetEmitPackageInfo(assembly, targetEnumValue)?.Name;

    public sealed record EmitPackageInfo(string Name, string? Version);

    /// <summary>
    /// Returns <c>true</c> when the compilation declares
    /// <c>[assembly: TranspileAssembly]</c>. Checks the semantic model
    /// first (covers MSBuild-driven projects) and falls back to walking
    /// the syntax trees for inline test compilations whose attribute may
    /// not yet appear on <see cref="IAssemblySymbol.GetAttributes"/>.
    /// Single source of truth for both the legacy
    /// <c>TypeTransformer</c> and the IR <c>CSharpSourceFrontend</c>.
    /// </summary>
    public static bool HasTranspileAssembly(this Compilation compilation)
    {
        var hasSemanticAttr = compilation
            .Assembly.GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly"
            );
        if (hasSemanticAttr)
            return true;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            foreach (var attrList in root.DescendantNodes().OfType<AttributeListSyntax>())
            {
                if (attrList.Target?.Identifier.Text != "assembly")
                    continue;

                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (
                        name
                        is "TranspileAssembly"
                            or "TranspileAssemblyAttribute"
                            or "Metano.TranspileAssembly"
                            or "Metano.TranspileAssemblyAttribute"
                    )
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool HasInlineWrapper(this ISymbol symbol) =>
        HasAttribute(symbol, "InlineWrapper");

    /// <summary>
    /// Determines if a type should be transpiled, considering:
    /// 1. [NoTranspile] → always excluded
    /// 2. [NoEmit] → excluded from transpilation, but the type is still discoverable
    ///    via Roslyn semantic model so user code can reference it. The transpiler
    ///    won't generate a .ts file or import it from anywhere — it's an ambient
    ///    declaration over an external library shape.
    /// 3. [Transpile] → always included
    /// 4. assemblyWideTranspile + public → included
    /// </summary>
    public static bool IsTranspilable(
        ISymbol symbol,
        bool assemblyWideTranspile = false,
        IAssemblySymbol? currentAssembly = null
    )
    {
        if (HasNoTranspile(symbol))
            return false;
        if (HasNoEmit(symbol))
            return false;
        // `[External]` and `[Erasable]` are emission-scope "no emit".
        // Both mark the class as something the compiler must not
        // produce a .ts file for (runtime-provided vs. compile-time
        // sugar, respectively). Kept separate from `[NoEmit]` so the
        // attribute semantics stay explicit at the source-code layer;
        // the effect on discovery is identical.
        if (HasExternal(symbol))
            return false;
        if (HasErasable(symbol))
            return false;
        if (HasTranspile(symbol))
            return true;
        // Assembly-wide: only for types in the current compilation's assembly (not BCL/referenced assemblies)
        if (
            assemblyWideTranspile
            && symbol.DeclaredAccessibility == Accessibility.Public
            && (
                currentAssembly is null
                || SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, currentAssembly)
            )
        )
            return true;
        return false;
    }

    /// <summary>
    /// Reads <c>[Import("name", from: "module", AsDefault = ?, Version = ?)]</c> from
    /// a symbol.
    /// </summary>
    public static ImportInfo? GetImport(this ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "ImportAttribute" or "Import");

        if (attr is null)
            return null;

        var name =
            attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value?.ToString()
                : null;
        var from =
            attr.ConstructorArguments.Length > 1
                ? attr.ConstructorArguments[1].Value?.ToString()
                : null;

        if (name is null || from is null)
            return null;

        var asDefault = false;
        string? version = null;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "AsDefault" && named.Value.Value is bool ad)
                asDefault = ad;
            else if (named.Key == "Version" && named.Value.Value is string v && v.Length > 0)
                version = v;
        }

        return new ImportInfo(name, from, asDefault, version);
    }

    public sealed record ImportInfo(
        string Name,
        string From,
        bool AsDefault = false,
        string? Version = null
    );

    /// <summary>
    /// Converts PascalCase to kebab-case for file paths.
    /// Examples: "UserId" → "user-id", "InMemoryIssueRepository" → "in-memory-issue-repository",
    /// "IIssueRepository" → "i-issue-repository", "PageRequest" → "page-request".
    /// </summary>
    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                // Insert hyphen before any uppercase that follows a lowercase or digit,
                // OR before an uppercase that is followed by a lowercase (acronym boundary).
                var prev = name[i - 1];
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var prevIsLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                var nextIsLower = char.IsLower(next);
                if (prevIsLowerOrDigit || (char.IsUpper(prev) && nextIsLower))
                    sb.Append('-');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
