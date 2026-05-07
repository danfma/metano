using Metano.Annotations;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Reads <c>[ImportAlias]</c> attributes from C# 11 file-scoped class
/// carriers and projects them into a per-source-file map the TS bridge
/// merges with the user's <c>using</c> aliases (Layer A) and the
/// auto-synthesized fallback (Stage 2). Carriers are detected via
/// <see cref="INamedTypeSymbol.IsFileLocal"/> on every named type in the
/// compilation; matching by file path then keys the resulting alias map
/// to the source file the carrier lives in. The carrier itself is
/// excluded from the transpilable type set so it never emits.
///
/// <para>
/// Two attribute shapes:
/// </para>
/// <list type="bullet">
///   <item><c>[ImportAlias(typeof(T), "Alias")]</c> — single-type pin.</item>
///   <item><c>[ImportAlias(Suffix = "Widget", Types = [typeof(A), typeof(B)])]</c> — bulk
///     suffix application.</item>
/// </list>
/// <para>
/// Each attribute may carry a <c>Target = TargetLanguage.X</c> filter; entries
/// whose target diverges from the active backend are dropped during projection.
/// </para>
/// </summary>
public static class ImportAliasResolver
{
    public static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > BuildPerFileAliases(Compilation compilation, TargetLanguage target)
    {
        var perFile = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var carrier in EnumerateFileLocalCarriers(compilation.GlobalNamespace))
        {
            var filePath = carrier.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath;
            if (filePath is null)
                continue;

            foreach (var attribute in carrier.GetAttributes())
            {
                if (
                    attribute.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    != "Metano.Annotations"
                )
                    continue;
                if (attribute.AttributeClass.Name is not ("ImportAliasAttribute" or "ImportAlias"))
                    continue;

                if (!MatchesTarget(attribute, target))
                    continue;

                if (!perFile.TryGetValue(filePath, out var aliases))
                {
                    aliases = new Dictionary<string, string>(StringComparer.Ordinal);
                    perFile[filePath] = aliases;
                }
                ApplyAttribute(attribute, aliases);
            }
        }

        return perFile.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, string>)kv.Value,
            StringComparer.Ordinal
        );
    }

    public static UsingAliasScope? Merge(
        UsingAliasScope? layerA,
        IReadOnlyDictionary<string, string>? autoSynthesized,
        IReadOnlyDictionary<string, string>? layerB
    )
    {
        if (
            layerA is null
            && (autoSynthesized is null || autoSynthesized.Count == 0)
            && (layerB is null || layerB.Count == 0)
        )
            return null;

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (autoSynthesized is not null)
            foreach (var (canonical, alias) in autoSynthesized)
                merged[canonical] = alias;
        if (layerA is not null)
            foreach (var (canonical, alias) in layerA.CanonicalToAlias)
                merged[canonical] = alias;
        if (layerB is not null)
            foreach (var (canonical, alias) in layerB)
                merged[canonical] = alias;
        return UsingAliasScope.Create(merged);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateFileLocalCarriers(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    foreach (var nested in EnumerateFileLocalCarriers(ns))
                        yield return nested;
                    break;

                case INamedTypeSymbol { IsFileLocal: true } type:
                    yield return type;
                    break;
            }
        }
    }

    private static bool MatchesTarget(AttributeData attribute, TargetLanguage target)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key != "Target")
                continue;
            if (named.Value.Value is int targetValue)
                return targetValue == (int)target;
            return true;
        }
        if (attribute.ConstructorArguments.Length >= 1)
        {
            var first = attribute.ConstructorArguments[0];
            if (
                first.Type is INamedTypeSymbol { Name: "TargetLanguage" }
                && first.Value is int targetValue
            )
                return targetValue == (int)target;
        }
        return true;
    }

    private static void ApplyAttribute(AttributeData attribute, Dictionary<string, string> aliases)
    {
        var ctorArgs = attribute.ConstructorArguments;
        if (
            ctorArgs.Length == 2
            && ctorArgs[0].Value is INamedTypeSymbol singleType
            && ctorArgs[1].Value is string singleAlias
        )
        {
            aliases[singleType.Name] = singleAlias;
            return;
        }

        if (
            ctorArgs.Length == 3
            && ctorArgs[1].Value is INamedTypeSymbol targetedType
            && ctorArgs[2].Value is string targetedAlias
        )
        {
            aliases[targetedType.Name] = targetedAlias;
            return;
        }

        string? suffix = null;
        IReadOnlyList<INamedTypeSymbol>? bulkTypes = null;
        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Suffix" when named.Value.Value is string suffixValue:
                    suffix = suffixValue;
                    break;
                case "Types" when !named.Value.IsNull:
                    bulkTypes = named
                        .Value.Values.Select(v => v.Value)
                        .OfType<INamedTypeSymbol>()
                        .ToList();
                    break;
            }
        }

        if (suffix is not null && bulkTypes is { Count: > 0 })
        {
            foreach (var type in bulkTypes)
                aliases[type.Name] = type.Name + suffix;
        }
    }
}
