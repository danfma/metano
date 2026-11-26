using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript binding of an <c>[Import]</c> facade's local name (#555): the core
/// spells the facade by its <c>[Name]</c> or camelCased C# name
/// (<see cref="SymbolHelper.ImportFacadeLocalName"/>), which may be a JavaScript
/// reserved word (<c>Delete</c> → <c>delete</c>); the module binds it escaped
/// (<c>delete_</c>). The call site, the import line and the barrel claim pass read
/// this one rule, so they agree on the name.
/// </summary>
internal static class TsImportFacadeBinding
{
    public static string Of(string localName) => TypeScriptNaming.EscapeIfReserved(localName);

    /// <summary>
    /// A facade template (<c>delete($0)</c>, or <c>delete</c> for a method group) and
    /// its imports with every facade binding escaped; any other template is returned
    /// unchanged. A facade template starts with the local name it imports under.
    /// </summary>
    public static (string Template, IReadOnlyList<IrExternalImport> Imports) Escape(
        string template,
        IReadOnlyList<IrExternalImport>? imports
    )
    {
        if (imports is null)
            return (template, []);

        var escaped = new List<IrExternalImport>(imports.Count);

        foreach (var import in imports)
        {
            if (import.EmittedName is not { } local || Of(local) == local)
            {
                escaped.Add(import);

                continue;
            }

            if (IsSpelledAtStart(template, local))
                template = Of(local) + template[local.Length..];

            escaped.Add(import with { EmittedName = Of(local) });
        }

        return (template, escaped);
    }

    /// <summary>
    /// A facade template and its imports with every facade binding a
    /// constructor parameter or local shadows bound under its alias instead
    /// (<c>import { extCompute as extCompute$1 }</c>, W10-1b): the instance
    /// initializers a constructor stores run in its scope.
    /// </summary>
    public static (string Template, IReadOnlyList<IrExternalImport> Imports) Unshadowed(
        string template,
        IReadOnlyList<IrExternalImport> imports,
        IReadOnlyDictionary<string, string> shadowed
    )
    {
        var rebound = new List<IrExternalImport>(imports.Count);

        foreach (var import in imports)
        {
            var local = import.EmittedName ?? import.Name;

            if (!shadowed.TryGetValue(local, out var alias) || !IsSpelledAtStart(template, local))
            {
                rebound.Add(import);

                continue;
            }

            template = alias + template[local.Length..];
            rebound.Add(import with { EmittedName = alias });
        }

        return (template, rebound);
    }

    private static bool IsSpelledAtStart(string template, string local) =>
        template == local || template.StartsWith(local + "(", StringComparison.Ordinal);
}
