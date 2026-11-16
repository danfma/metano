using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The named bindings several imports of one module merge into, keyed by
/// (name, alias): <c>Decimal</c> and <c>Decimal as Money</c> are two bindings of
/// one export, and both survive. Each binding keeps its own type-only flag,
/// which a value occurrence of that same binding clears.
/// </summary>
internal sealed class ImportBindings
{
    private readonly List<(string Name, string? Alias)> _order = [];
    private readonly Dictionary<(string Name, string? Alias), bool> _typeOnly = [];

    public void Add(TsImport import)
    {
        foreach (var name in import.Names)
            Add(name, import.Aliases?.GetValueOrDefault(name), TypeOnlyIn(import, name));

        foreach (var extra in import.ExtraAliases ?? [])
            Add(extra.Original, extra.Local, import.TypeOnly || extra.TypeOnly);
    }

    public void Add(string name, string? alias, bool typeOnly)
    {
        var key = (name, alias);

        if (_typeOnly.TryGetValue(key, out var known))
        {
            _typeOnly[key] = known && typeOnly;
            return;
        }

        _order.Add(key);
        _typeOnly[key] = typeOnly;
    }

    /// <summary>
    /// One import of every binding: a name is listed once, plainly when it is
    /// bound plainly, else under its first alias; every other alias of it is an
    /// extra binding (<c>import { Decimal, Decimal as Money }</c>).
    /// </summary>
    public TsImport ToImport(string from)
    {
        var names = new List<string>();
        var typeOnlyNames = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var extraAliases = new List<TsImportAlias>();

        foreach (var group in _order.GroupBy(binding => binding.Name))
        {
            // The plain binding when there is one, else the first alias (OrderBy is stable).
            var listed = group.OrderBy(binding => binding.Alias is not null).First();

            names.Add(group.Key);

            if (_typeOnly[listed])
                typeOnlyNames.Add(group.Key);

            if (listed.Alias is { } alias)
                aliases[group.Key] = alias;

            extraAliases.AddRange(
                group
                    .Where(binding => binding != listed)
                    .Select(binding => new TsImportAlias(
                        binding.Name,
                        binding.Alias!,
                        _typeOnly[binding]
                    ))
            );
        }

        var allTypeOnly = _typeOnly.Values.All(typeOnly => typeOnly);

        ImportCollector.SortValuesFirst(names, typeOnlyNames);

        return new TsImport(
            names.ToArray(),
            from,
            TypeOnly: allTypeOnly,
            TypeOnlyNames: !allTypeOnly && typeOnlyNames.Count > 0 ? typeOnlyNames : null,
            Aliases: aliases.Count > 0 ? aliases : null,
            ExtraAliases: extraAliases.Count > 0 ? extraAliases : null
        );
    }

    private static bool TypeOnlyIn(TsImport import, string name) =>
        import.TypeOnly
        || (import.TypeOnlyNames is not null && import.TypeOnlyNames.Contains(name));
}
