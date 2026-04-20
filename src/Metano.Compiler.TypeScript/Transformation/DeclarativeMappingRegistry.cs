using Metano.Compiler.IR;

namespace Metano.Transformation;

/// <summary>
/// IR-driven index of declarative BCL mappings surfaced through
/// <see cref="IrCompilation.DeclarativeMethodMappings"/> /
/// <see cref="IrCompilation.DeclarativePropertyMappings"/> /
/// <see cref="IrCompilation.ChainMethodsByWrapper"/>. Built once per
/// transpile run by <see cref="CSharpSourceFrontend"/> from
/// <c>[assembly: MapMethod]</c> / <c>[assembly: MapProperty]</c>
/// attributes on the current and referenced assemblies, consumed by the
/// TypeScript target's <c>IrToTsBclMapper</c> before its hardcoded
/// lowering rules.
///
/// Lookup is keyed by
/// (<see cref="SymbolHelper.GetStableFullName"/>, member-name) so entries
/// registered against an open generic (e.g.,
/// <c>System.Collections.Generic.List&lt;T&gt;</c>) match every closed
/// instantiation from the IR's <c>IrMemberOrigin</c>.
/// </summary>
public sealed class DeclarativeMappingRegistry
{
    private readonly IReadOnlyDictionary<
        (string FullName, string Name),
        IReadOnlyList<DeclarativeMappingEntry>
    > _methodsByFullName;
    private readonly IReadOnlyDictionary<
        (string FullName, string Name),
        DeclarativeMappingEntry
    > _propertiesByFullName;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _chainMethodsByWrapper;

    // Returned by GetChainMethodNames on a miss so the hot wrap-receiver
    // detection path does not allocate a fresh HashSet every call.
    private static readonly IReadOnlySet<string> EmptyChainMethods = new HashSet<string>();

    private DeclarativeMappingRegistry(
        IReadOnlyDictionary<
            (string, string),
            IReadOnlyList<DeclarativeMappingEntry>
        > methodsByFullName,
        IReadOnlyDictionary<(string, string), DeclarativeMappingEntry> propertiesByFullName,
        IReadOnlyDictionary<string, IReadOnlySet<string>> chainMethodsByWrapper
    )
    {
        _methodsByFullName = methodsByFullName;
        _propertiesByFullName = propertiesByFullName;
        _chainMethodsByWrapper = chainMethodsByWrapper;
    }

    /// <summary>
    /// Empty registry — used when there are no declarative mappings to
    /// honor (the compilation does not reference any assembly that
    /// defines them).
    /// </summary>
    public static DeclarativeMappingRegistry Empty { get; } =
        new(
            new Dictionary<(string, string), IReadOnlyList<DeclarativeMappingEntry>>(),
            new Dictionary<(string, string), DeclarativeMappingEntry>(),
            new Dictionary<string, IReadOnlySet<string>>()
        );

    /// <summary>
    /// Wraps the IR's declarative mapping tables into the registry shape
    /// consumed by the TypeScript target's BCL mapper. Null tables on the
    /// IR are treated as empty so callers can drop in the registry
    /// without pre-checking.
    /// </summary>
    public static DeclarativeMappingRegistry FromIr(IrCompilation ir) =>
        new(
            ir.DeclarativeMethodMappings
                ?? new Dictionary<(string, string), IReadOnlyList<DeclarativeMappingEntry>>(),
            ir.DeclarativePropertyMappings
                ?? new Dictionary<(string, string), DeclarativeMappingEntry>(),
            ir.ChainMethodsByWrapper ?? new Dictionary<string, IReadOnlySet<string>>()
        );

    /// <summary>
    /// Returns the set of JS method names registered for a given
    /// <c>WrapReceiver</c> value. Used by the BCL mapper to recognize
    /// "already wrapped" sources when applying a mapping with a wrapping
    /// spec — if the receiver is a call whose callee property matches one
    /// of these names, no re-wrapping is needed.
    /// </summary>
    public IReadOnlySet<string> GetChainMethodNames(string wrapReceiver) =>
        _chainMethodsByWrapper.TryGetValue(wrapReceiver, out var set) ? set : EmptyChainMethods;

    /// <summary>
    /// Test-only factory: builds a registry directly from hand-written
    /// full-name-keyed dictionaries, bypassing the IR. Used by the
    /// <c>IrToTsBclMapper</c> tests to exercise individual mapping shapes
    /// without spinning up a full Roslyn compilation.
    /// </summary>
    internal static DeclarativeMappingRegistry CreateForTests(
        IReadOnlyDictionary<(string FullName, string Name), DeclarativeMappingEntry> methods,
        IReadOnlyDictionary<(string FullName, string Name), DeclarativeMappingEntry>? properties =
            null
    )
    {
        var methodsByFullName =
            new Dictionary<(string, string), IReadOnlyList<DeclarativeMappingEntry>>();
        foreach (var (key, entry) in methods)
            methodsByFullName[key] = new[] { entry };

        var propertiesByFullName = new Dictionary<(string, string), DeclarativeMappingEntry>();
        if (properties is not null)
            foreach (var (key, entry) in properties)
                propertiesByFullName[key] = entry;

        var chainMethodsMutable = new Dictionary<string, HashSet<string>>();
        foreach (var entry in methods.Values)
        {
            if (entry.WrapReceiver is null || entry.JsName is null)
                continue;
            if (!chainMethodsMutable.TryGetValue(entry.WrapReceiver, out var set))
            {
                set = [];
                chainMethodsMutable[entry.WrapReceiver] = set;
            }
            set.Add(entry.JsName);
        }

        var chainMethods = chainMethodsMutable.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<string>)kv.Value
        );

        return new DeclarativeMappingRegistry(
            methodsByFullName,
            propertiesByFullName,
            chainMethods
        );
    }

    /// <summary>
    /// Keys on the declaring type's fully-qualified original-definition
    /// name (e.g., <c>System.Collections.Generic.List&lt;T&gt;</c>) so the
    /// BCL mapper can look up entries straight from an
    /// <c>IrMemberOrigin</c> without holding a Roslyn symbol.
    /// </summary>
    public bool TryGetMethodsByFullName(
        string declaringTypeFullName,
        string methodName,
        out IReadOnlyList<DeclarativeMappingEntry> entries
    )
    {
        if (_methodsByFullName.TryGetValue((declaringTypeFullName, methodName), out var list))
        {
            entries = list;
            return true;
        }
        entries = [];
        return false;
    }

    /// <summary>
    /// Looks up a declarative property mapping by the declaring type's
    /// stable full name and the property name. Mirrors
    /// <see cref="TryGetMethodsByFullName"/> for properties.
    /// </summary>
    public bool TryGetPropertyByFullName(
        string declaringTypeFullName,
        string propertyName,
        out DeclarativeMappingEntry entry
    ) => _propertiesByFullName.TryGetValue((declaringTypeFullName, propertyName), out entry!);
}
