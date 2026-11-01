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
        Read(compilation).ContainsKey(type.OriginalDefinition.ToDisplayString());

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
}
