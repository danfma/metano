using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The import aliases of shadowed references (<see cref="TsAliasedReference"/>,
/// W10-1b): the reference is collected as if no local bound its name, and the
/// import line that brings the name in also binds it under the alias.
/// </summary>
public sealed partial class ImportCollector
{
    private void CollectFromAliasedReference(TsAliasedReference aliased, ImportCollectionSink sink)
    {
        sink.ReferenceAliases[aliased.Name] = aliased.LocalName;

        if (aliased.Reference is TsIdentifier identifier)
            CollectFromBareIdentifier(identifier.Name, sink);
        else
            CollectFromExpression(aliased.Reference, sink);
    }

    /// <summary>
    /// <paramref name="imports"/> with every aliased name also bound under its
    /// alias (<c>import { compute, compute as compute$1 }</c>) on the named
    /// import line that brings it in. A name no import brings in is declared
    /// in this module, where no import can rebind it: that reports MS0001.
    /// </summary>
    private List<TsImport> WithReferenceAliases(
        List<TsImport> imports,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<TsTopLevel> statements
    )
    {
        if (aliases.Count is 0)
            return imports;

        var named = imports.Where(import => !import.IsDefault && !import.IsNamespace).ToList();

        foreach (var name in aliases.Keys.Where(name => !named.Any(i => i.Names.Contains(name))))
            ReportUnaliasableShadow(name, statements);

        return
        [
            .. imports.Select(import =>
                named.Contains(import) ? WithReferenceAliases(import, aliases) : import
            ),
        ];
    }

    /// <summary>
    /// Reports a shadowed read no import can alias at the binding that shadows
    /// it: the constructor parameter of that name in a class of this module, else
    /// the constructor.
    /// </summary>
    private void ReportUnaliasableShadow(string name, IReadOnlyList<TsTopLevel> statements) =>
        _context.ReportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                $"An instance initializer reads '{name}', which this module declares, inside a "
                    + "constructor whose parameter or local of the same name shadows it. Rename "
                    + "the parameter or local in C#.",
                ShadowingBindingOf(name, statements)
            )
        );

    private Microsoft.CodeAnalysis.Location? ShadowingBindingOf(
        string name,
        IReadOnlyList<TsTopLevel> statements
    ) =>
        statements
            .OfType<TsClass>()
            .Select(tsClass =>
                _context.ConstructorScopeBindings.GetValueOrDefault((tsClass.Name, name))
            )
            .FirstOrDefault(location => location is not null);

    private static TsImport WithReferenceAliases(
        TsImport import,
        IReadOnlyDictionary<string, string> aliases
    )
    {
        var extra = import
            .Names.Where(aliases.ContainsKey)
            .Select(name => new TsImportAlias(name, aliases[name], TypeOnly: false))
            .ToList();

        return extra.Count is 0
            ? import
            : import with
            {
                ExtraAliases = [.. import.ExtraAliases ?? [], .. extra],
            };
    }
}
