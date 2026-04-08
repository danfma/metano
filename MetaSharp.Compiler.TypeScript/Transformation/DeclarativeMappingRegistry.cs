using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

/// <summary>
/// One declarative mapping entry from <c>[MapMethod]</c> or <c>[MapProperty]</c>.
/// Either <see cref="JsName"/> (simple rename) or <see cref="JsTemplate"/> is set, never
/// both. The reader enforces the mutual exclusivity at registration time.
/// </summary>
public sealed record DeclarativeMappingEntry(string? JsName, string? JsTemplate)
{
    public bool HasTemplate => JsTemplate is not null;
}

/// <summary>
/// Index of declarative BCL mappings collected from all <c>[assembly: MapMethod]</c> and
/// <c>[assembly: MapProperty]</c> attributes visible to a Roslyn <see cref="Compilation"/>
/// (the current assembly + every referenced assembly).
///
/// Built once during <see cref="TypeTransformer.TransformAll"/> setup, stored on the
/// <see cref="TypeScriptTransformContext"/>, and consulted by <see cref="BclMapper"/>
/// before falling back to its hardcoded lowering rules.
///
/// Lookup is keyed by <c>(declaringType.OriginalDefinition, memberName)</c> so that an
/// entry registered as <c>typeof(List&lt;&gt;)</c> matches every closed instantiation like
/// <c>List&lt;int&gt;</c> or <c>List&lt;Money&gt;</c>.
/// </summary>
public sealed class DeclarativeMappingRegistry
{
    private readonly Dictionary<(INamedTypeSymbol Type, string Name), DeclarativeMappingEntry> _methods;
    private readonly Dictionary<(INamedTypeSymbol Type, string Name), DeclarativeMappingEntry> _properties;

    private DeclarativeMappingRegistry(
        Dictionary<(INamedTypeSymbol, string), DeclarativeMappingEntry> methods,
        Dictionary<(INamedTypeSymbol, string), DeclarativeMappingEntry> properties)
    {
        _methods = methods;
        _properties = properties;
    }

    /// <summary>
    /// Empty registry — used when there are no declarative mappings to honor (e.g., the
    /// compilation does not reference any assembly that defines them).
    /// </summary>
    public static DeclarativeMappingRegistry Empty { get; } = new(
        new Dictionary<(INamedTypeSymbol, string), DeclarativeMappingEntry>(SymbolNameKeyComparer.Instance),
        new Dictionary<(INamedTypeSymbol, string), DeclarativeMappingEntry>(SymbolNameKeyComparer.Instance));

    public int MethodCount => _methods.Count;
    public int PropertyCount => _properties.Count;

    /// <summary>
    /// Looks up a declarative method mapping by the containing type and the C# method name.
    /// The containing type is normalized to its <see cref="INamedTypeSymbol.OriginalDefinition"/>
    /// so closed generics resolve to the open-generic registration.
    /// </summary>
    public bool TryGetMethod(INamedTypeSymbol containingType, string methodName, out DeclarativeMappingEntry entry) =>
        _methods.TryGetValue((containingType.OriginalDefinition, methodName), out entry!);

    /// <summary>
    /// Looks up a declarative property mapping by the containing type and the C# property name.
    /// </summary>
    public bool TryGetProperty(INamedTypeSymbol containingType, string propertyName, out DeclarativeMappingEntry entry) =>
        _properties.TryGetValue((containingType.OriginalDefinition, propertyName), out entry!);

    /// <summary>
    /// Builds a registry by walking the compilation's own assembly and every referenced
    /// assembly, collecting their assembly-level <c>[MapMethod]</c> and <c>[MapProperty]</c>
    /// attributes. Mappings whose <c>DeclaringType</c> can't be resolved against the current
    /// compilation (e.g., a referenced assembly mentions a type the consumer doesn't ship)
    /// are silently skipped.
    /// </summary>
    public static DeclarativeMappingRegistry BuildFromCompilation(Compilation compilation)
    {
        var methods = new Dictionary<(INamedTypeSymbol, string), DeclarativeMappingEntry>(SymbolNameKeyComparer.Instance);
        var properties = new Dictionary<(INamedTypeSymbol, string), DeclarativeMappingEntry>(SymbolNameKeyComparer.Instance);

        // Walk the current assembly + every referenced assembly's attributes
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
                var attrName = attr.AttributeClass?.Name;
                if (attrName is "MapMethodAttribute")
                    TryRegister(attr, methods);
                else if (attrName is "MapPropertyAttribute")
                    TryRegister(attr, properties);
            }
        }

        return new DeclarativeMappingRegistry(methods, properties);
    }

    /// <summary>
    /// Reads one [MapMethod] / [MapProperty] attribute and inserts it into the target
    /// dictionary. Both attribute shapes are identical from the AttributeData perspective:
    /// the constructor takes <c>(Type, string)</c> and the optional named arguments are
    /// either <c>JsMethod</c>/<c>JsProperty</c> (rename) or <c>JsTemplate</c>.
    /// </summary>
    private static void TryRegister(
        AttributeData attr,
        Dictionary<(INamedTypeSymbol, string), DeclarativeMappingEntry> target)
    {
        if (attr.ConstructorArguments.Length < 2) return;
        if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol declaringType) return;
        if (attr.ConstructorArguments[1].Value is not string memberName) return;

        string? jsName = null;
        string? jsTemplate = null;
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "JsMethod":
                case "JsProperty":
                    jsName = named.Value.Value as string;
                    break;
                case "JsTemplate":
                    jsTemplate = named.Value.Value as string;
                    break;
            }
        }

        // At least one form must be present; if both are set, JsTemplate wins (it's
        // strictly more expressive than the rename shorthand).
        if (jsName is null && jsTemplate is null) return;

        var entry = new DeclarativeMappingEntry(jsName, jsTemplate);
        var key = (declaringType.OriginalDefinition, memberName);

        // Last write wins. The expected pattern is one declaration per (Type, member),
        // but if a consumer overrides a default mapping with their own, the consumer's
        // assembly is walked after the default assembly so their entry takes precedence.
        target[key] = entry;
    }

    /// <summary>
    /// Equality comparer for the (declaringType, memberName) lookup key. Uses
    /// <see cref="SymbolEqualityComparer.Default"/> for the symbol part so that two
    /// references to the same generic definition (e.g., from different syntax trees)
    /// hash and compare equal.
    /// </summary>
    private sealed class SymbolNameKeyComparer : IEqualityComparer<(INamedTypeSymbol Type, string Name)>
    {
        public static readonly SymbolNameKeyComparer Instance = new();

        public bool Equals((INamedTypeSymbol Type, string Name) x, (INamedTypeSymbol Type, string Name) y) =>
            SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && x.Name == y.Name;

        public int GetHashCode((INamedTypeSymbol Type, string Name) obj) =>
            unchecked(SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397 ^ obj.Name.GetHashCode());
    }
}
