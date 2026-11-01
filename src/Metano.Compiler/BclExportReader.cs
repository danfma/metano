using System.Runtime.CompilerServices;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// Reads the <c>[ExportFromBcl]</c> declarations in scope of a compilation: the BCL
/// types the toolchain binds to a JavaScript export (<c>decimal</c> → <c>Decimal</c> from
/// <c>decimal.js</c>). Referenced assemblies are read first and the current assembly
/// last, so a user-side declaration wins on conflict. The map is keyed by the type's
/// C# display string (<c>"decimal"</c>, not <c>"System.Decimal"</c>) and computed once
/// per compilation.
/// </summary>
public static class BclExportReader
{
    private static readonly ConditionalWeakTable<
        Compilation,
        IReadOnlyDictionary<string, IrBclExport>
    > Cache = new();

    public static IReadOnlyDictionary<string, IrBclExport> Read(Compilation compilation) =>
        Cache.GetValue(compilation, Build);

    /// <summary>Whether an <c>[ExportFromBcl]</c> in scope binds <paramref name="type"/>.</summary>
    public static bool IsExported(INamedTypeSymbol type, Compilation compilation) =>
        Read(compilation).ContainsKey(ExportKey(type));

    private static IReadOnlyDictionary<string, IrBclExport> Build(Compilation compilation)
    {
        var map = new Dictionary<string, IrBclExport>(StringComparer.Ordinal);

        foreach (var reference in compilation.References)
        {
            if (
                compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm
                && !SymbolEqualityComparer.Default.Equals(asm, compilation.Assembly)
            )
                CollectFromAssembly(asm, map);
        }

        CollectFromAssembly(compilation.Assembly, map);

        return map;
    }

    private static void CollectFromAssembly(
        IAssemblySymbol assembly,
        Dictionary<string, IrBclExport> sink
    )
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (ReadExport(attr) is { } entry)
                sink[entry.Key] = entry.Export;
        }
    }

    /// <summary>
    /// The export an <c>[ExportFromBcl]</c> attribute declares, keyed by its type's
    /// display string; <c>null</c> for any other attribute or one without an exported
    /// name.
    /// </summary>
    private static (string Key, IrBclExport Export)? ReadExport(AttributeData attribute)
    {
        if (attribute.AttributeClass?.Name is not ("ExportFromBclAttribute" or "ExportFromBcl"))
            return null;

        if (attribute.ConstructorArguments is not [{ Value: INamedTypeSymbol type }, ..])
            return null;

        var exportedName = NamedArgument(attribute, "ExportedName");

        if (string.IsNullOrEmpty(exportedName))
            return null;

        var fromPackage = NamedArgument(attribute, "FromPackage") ?? "";
        var version = NamedArgument(attribute, "Version");

        return (
            ExportKey(type),
            new IrBclExport(
                exportedName,
                fromPackage,
                string.IsNullOrEmpty(version) ? null : version
            )
        );
    }

    /// <summary>
    /// The map key of <paramref name="type"/>: the display string of its original
    /// definition, so an unbound <c>typeof(List&lt;&gt;)</c> argument and a constructed
    /// <c>List&lt;int&gt;</c> reference both key as <c>List&lt;T&gt;</c>.
    /// </summary>
    private static string ExportKey(INamedTypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString();

    private static string? NamedArgument(AttributeData attribute, string name) =>
        attribute
            .NamedArguments.FirstOrDefault(argument => argument.Key == name)
            .Value.Value?.ToString();
}
