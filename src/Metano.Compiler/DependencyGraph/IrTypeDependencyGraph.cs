using System.Collections.Immutable;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.DependencyGraph;

/// <summary>
/// Type-level dependency graph derived from an <see cref="IrCompilation"/>.
/// Maps each transpilable type's fully qualified name (FQN — namespace
/// + containing types + generic parameters, taken from the Roslyn
/// symbol) to the set of FQNs it references on its <em>signature
/// surface</em>: base type, interfaces, ctor / field / property /
/// method signatures (return type, parameters, generic constraints),
/// event types, and nested types. Method-body references (calls /
/// instantiations inside expressions) are <b>not</b> tracked yet —
/// those go through per-target bridges and stay out of the graph for
/// this slice; ADR-0018 captures the trade-off.
///
/// <para>
/// The graph is the shared backbone for:
/// </para>
/// <list type="bullet">
///   <item><b>Incremental compilation (#21):</b> "if type T was touched,
///   which types regenerate?". The cache walks the reverse graph to mark
///   transitive dependents dirty.</item>
///   <item><b>Watch mode (#18):</b> a file change resolves to the FQNs
///   declared in that file; the same reverse-walk yields the set of
///   files to re-emit.</item>
/// </list>
///
/// <para>
/// References come from the Roslyn symbols of each transpilable type.
/// Only types that themselves participate in the compilation's
/// transpilable set contribute edges — BCL types, types from a
/// referenced assembly, and primitives are dropped because the
/// incremental cache cannot regenerate code that does not live in this
/// project. Foreign assembly changes invalidate consumers via the
/// cache's separate metadata-hash key.
/// </para>
/// </summary>
public sealed class IrTypeDependencyGraph
{
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _outEdges;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _inEdges;

    private IrTypeDependencyGraph(
        IReadOnlyDictionary<string, IReadOnlySet<string>> outEdges,
        IReadOnlyDictionary<string, IReadOnlySet<string>> inEdges
    )
    {
        _outEdges = outEdges;
        _inEdges = inEdges;
    }

    /// <summary>FQNs of every type catalogued in the graph.</summary>
    public IEnumerable<string> AllTypes => _outEdges.Keys;

    /// <summary>
    /// Direct dependencies of <paramref name="typeFqn"/> — the types
    /// whose IR appears on its signature surface (see the type-level
    /// docstring for the exact reach). Method-body references are
    /// not included by this slice.
    /// </summary>
    public IReadOnlySet<string> DependenciesOf(string typeFqn) =>
        _outEdges.TryGetValue(typeFqn, out var deps) ? deps : EmptySet;

    /// <summary>
    /// Direct dependents of <paramref name="typeFqn"/> — the types
    /// that reference it. The reverse of <see cref="DependenciesOf"/>;
    /// drives incremental invalidation.
    /// </summary>
    public IReadOnlySet<string> DependentsOf(string typeFqn) =>
        _inEdges.TryGetValue(typeFqn, out var deps) ? deps : EmptySet;

    /// <summary>
    /// Transitive closure of <see cref="DependentsOf"/> — every type
    /// that reaches <paramref name="typeFqn"/> through any reference
    /// chain. The set excludes the seed itself; callers that need it
    /// should add it explicitly.
    /// </summary>
    public IReadOnlySet<string> TransitiveDependentsOf(string typeFqn) =>
        TransitiveClosure(typeFqn, DependentsOf);

    /// <summary>
    /// Transitive closure of <see cref="DependenciesOf"/>. Excludes
    /// the seed itself so callers can union it with the seed when
    /// they need an "everything this type touches" set.
    /// </summary>
    public IReadOnlySet<string> TransitiveDependenciesOf(string typeFqn) =>
        TransitiveClosure(typeFqn, DependenciesOf);

    private IReadOnlySet<string> TransitiveClosure(
        string typeFqn,
        Func<string, IReadOnlySet<string>> step
    )
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(typeFqn);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var next in step(current))
            {
                if (visited.Add(next))
                    stack.Push(next);
            }
        }
        // Cycles (A → B → A) re-add the seed via a neighbor; strip
        // it back out so the documented "excludes the seed" contract
        // holds even when the graph contains cycles.
        visited.Remove(typeFqn);
        return visited;
    }

    /// <summary>
    /// Shared empty-set sentinel returned by lookups that miss. Held
    /// as <see cref="ImmutableHashSet{T}.Empty"/> so a caller cannot
    /// downcast and mutate the singleton out from under everyone else.
    /// </summary>
    private static readonly IReadOnlySet<string> EmptySet = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Builds the graph from the transpilable type entries of an
    /// <see cref="IrCompilation"/>. References to types outside the
    /// transpilable set (BCL types, primitives, types from other
    /// assemblies) are skipped because they cannot be regenerated
    /// from this compilation's incremental cache — the cache
    /// invalidates consumers of those via assembly-metadata hashes
    /// instead.
    /// </summary>
    public static IrTypeDependencyGraph Build(IrCompilation compilation)
    {
        var entries = compilation.TranspilableTypeEntries ?? Array.Empty<IrTranspilableTypeEntry>();
        var ownTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            ownTypes.Add(QualifiedName(entry.Symbol));

        var outEdges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var fqn = QualifiedName(entry.Symbol);
            var deps = new HashSet<string>(StringComparer.Ordinal);
            CollectFromType(entry.Symbol, deps, ownTypes, fqn);
            outEdges[fqn] = deps;
        }

        var inEdges = ReverseEdges(outEdges);
        return new IrTypeDependencyGraph(
            outEdges.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlySet<string>)kv.Value,
                StringComparer.Ordinal
            ),
            inEdges
        );
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReverseEdges(
        Dictionary<string, HashSet<string>> outEdges
    )
    {
        var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (consumer, deps) in outEdges)
        {
            foreach (var dep in deps)
            {
                if (!reverse.TryGetValue(dep, out var dependents))
                {
                    dependents = new HashSet<string>(StringComparer.Ordinal);
                    reverse[dep] = dependents;
                }
                dependents.Add(consumer);
            }
        }
        return reverse.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<string>)kv.Value,
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// Canonical FQN used as the graph key. Built from
    /// <see cref="ISymbol.OriginalDefinition"/> so a constructed
    /// reference like <c>List&lt;User&gt;</c> resolves to the same
    /// key as the type's own definition. The display format keeps
    /// the namespace + every containing type (so nested types like
    /// <c>Outer.Inner</c> stay distinct) and the type-parameter
    /// names (so generic arities don't collapse — <c>Foo</c> and
    /// <c>Foo&lt;T&gt;</c> are different keys).
    /// </summary>
    public static string QualifiedName(INamedTypeSymbol symbol) =>
        symbol.OriginalDefinition.ToDisplayString(QualifiedNameFormat);

    private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable
    );

    // -- Roslyn symbol walking -----------------------------------------------

    private static void CollectFromType(
        INamedTypeSymbol symbol,
        HashSet<string> acc,
        IReadOnlySet<string> ownTypes,
        string selfFqn
    )
    {
        if (symbol.BaseType is { } baseType)
            AddIfTranspilable(baseType, acc, ownTypes, selfFqn);
        foreach (var iface in symbol.Interfaces)
            AddIfTranspilable(iface, acc, ownTypes, selfFqn);

        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field:
                    CollectFromTypeRef(field.Type, acc, ownTypes, selfFqn);
                    break;
                case IPropertySymbol property:
                    CollectFromTypeRef(property.Type, acc, ownTypes, selfFqn);
                    break;
                case IMethodSymbol method:
                    CollectFromMethod(method, acc, ownTypes, selfFqn);
                    break;
                case IEventSymbol evt:
                    CollectFromTypeRef(evt.Type, acc, ownTypes, selfFqn);
                    break;
                case INamedTypeSymbol nested:
                    CollectFromType(nested, acc, ownTypes, selfFqn);
                    break;
            }
        }
    }

    private static void CollectFromMethod(
        IMethodSymbol method,
        HashSet<string> acc,
        IReadOnlySet<string> ownTypes,
        string selfFqn
    )
    {
        CollectFromTypeRef(method.ReturnType, acc, ownTypes, selfFqn);
        foreach (var p in method.Parameters)
            CollectFromTypeRef(p.Type, acc, ownTypes, selfFqn);
        foreach (var tp in method.TypeParameters)
        foreach (var constraint in tp.ConstraintTypes)
            CollectFromTypeRef(constraint, acc, ownTypes, selfFqn);
    }

    private static void CollectFromTypeRef(
        ITypeSymbol type,
        HashSet<string> acc,
        IReadOnlySet<string> ownTypes,
        string selfFqn
    )
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                AddIfTranspilable(named, acc, ownTypes, selfFqn);
                foreach (var arg in named.TypeArguments)
                    CollectFromTypeRef(arg, acc, ownTypes, selfFqn);
                break;
            case IArrayTypeSymbol array:
                CollectFromTypeRef(array.ElementType, acc, ownTypes, selfFqn);
                break;
            // Pointer / dynamic / type-parameter references contribute
            // nothing the cache can act on — skip them.
        }
    }

    private static void AddIfTranspilable(
        INamedTypeSymbol symbol,
        HashSet<string> acc,
        IReadOnlySet<string> ownTypes,
        string selfFqn
    )
    {
        // Roll the reference up to its top-level containing type so a
        // dependency on a nested type attributes back to the unit of
        // invalidation the cache actually emits (the cache regenerates
        // the top-level entry's output file as a whole, including
        // every nested type co-located with it).
        var topLevel = symbol;
        while (topLevel.ContainingType is not null)
            topLevel = topLevel.ContainingType;

        var fqn = QualifiedName(topLevel);
        if (fqn == selfFqn || !ownTypes.Contains(fqn))
            return;
        acc.Add(fqn);
    }
}
