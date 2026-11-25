using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// The non-public types of an <c>[assembly: TranspileAssembly]</c> assembly that emitted
/// code reaches (#770, decision W9-9): a type referenced from the signature or body of a
/// transpiled type, directly or through another reached type, is transpiled too, so the
/// output never names a symbol that is not generated. A nested type is reached through its
/// outermost container, which carries it. <c>[Ignore]</c>, <c>[External]</c>,
/// <c>[JsCallable]</c> and file-local types stay out (their references are reported by
/// <see cref="IgnoredReferenceRule"/> and <see cref="UnemittedTypeReferenceRule"/>).
/// <para>
/// The walk is target-neutral: it skips only members no target emits, so a reference
/// that one target drops still reaches its type (an unused module, never a missing one).
/// Computed once per source assembly.
/// </para>
/// </summary>
internal sealed class InternalTypeReachability : ITranspiledReferenceRule
{
    private static readonly ConditionalWeakTable<
        IAssemblySymbol,
        IReadOnlySet<INamedTypeSymbol>
    > ReachedByAssembly = new();

    private readonly IAssemblySymbol _assembly;
    private readonly HashSet<INamedTypeSymbol> _reached = new(SymbolEqualityComparer.Default);
    private readonly Queue<INamedTypeSymbol> _pending = new();

    private InternalTypeReachability(IAssemblySymbol assembly) => _assembly = assembly;

    /// <summary>
    /// Whether emitted code of <paramref name="type"/>'s own assembly reaches it. Only a
    /// source <c>[assembly: TranspileAssembly]</c> assembly answers: a referenced package's
    /// internals are never reached from its consumer.
    /// </summary>
    public static bool IsReached(INamedTypeSymbol type) =>
        type.ContainingAssembly is ISourceAssemblySymbol assembly
        && assembly.HasTranspileAssembly()
        && ReachedByAssembly.GetValue(assembly, Compute).Contains(type.OriginalDefinition);

    public void CheckType(INamedTypeSymbol type, Location location) => Reach(type);

    public void CheckMember(ISymbol member, Location location)
    {
        if (member.ContainingType is { } owner)
            Reach(owner);
    }

    private static IReadOnlySet<INamedTypeSymbol> Compute(IAssemblySymbol assembly)
    {
        var compilation = ((ISourceAssemblySymbol)assembly).Compilation;
        var reachability = new InternalTypeReachability(assembly);
        var walker = TranspiledReferenceWalker.ForReachability(compilation, reachability);

        CSharpSourceFrontend.CollectTopLevelTypes(assembly.GlobalNamespace, type =>
        {
            if (SymbolHelper.IsTranspilableAsDeclared(type, assembly))
                walker.Walk(type);
        });

        while (reachability._pending.TryDequeue(out var reached))
            walker.Walk(reached);

        return reachability._reached;
    }

    private void Reach(INamedTypeSymbol type)
    {
        var container = OutermostContainer(type.OriginalDefinition);

        if (IsCandidate(container) && _reached.Add(container))
            _pending.Enqueue(container);
    }

    private static INamedTypeSymbol OutermostContainer(INamedTypeSymbol type)
    {
        var outermost = type;

        while (outermost.ContainingType is { } container)
            outermost = container;

        return outermost;
    }

    /// <summary>
    /// A user-declared type of this assembly that is not transpiled as declared and that a
    /// target can emit.
    /// </summary>
    private bool IsCandidate(INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _assembly)
        && type is { IsImplicitlyDeclared: false, IsAnonymousType: false, IsFileLocal: false }
        && !string.IsNullOrEmpty(type.Name)
        && type.Locations.Any(location => location.IsInSource)
        && !SymbolHelper.HasIgnore(type)
        && !SymbolHelper.HasExternal(type)
        && !SymbolHelper.HasJsCallable(type)
        && !SymbolHelper.IsTranspilableAsDeclared(type, _assembly);
}
