using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The self-typed static members of a compilation (spec 001 FR-113): a static method
/// of a CRTP-shaped class that builds its self type with <c>new TSelf()</c> from the
/// receiving class, instead of a hidden reified parameter.
/// </summary>
public sealed class SelfTypedMembers
{
    public static readonly SelfTypedMembers None = new(
        new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
        new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
        new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
        currentAssembly: null,
        TargetLanguage.TypeScript
    );

    private readonly IReadOnlySet<IMethodSymbol> _members;
    private readonly IReadOnlySet<IMethodSymbol> _forwarding;
    private readonly IReadOnlySet<INamedTypeSymbol> _reifiedClasses;
    private readonly IAssemblySymbol? _currentAssembly;
    private readonly TargetLanguage _target;

    public SelfTypedMembers(
        IReadOnlySet<IMethodSymbol> members,
        IReadOnlySet<IMethodSymbol> forwarding,
        IReadOnlySet<INamedTypeSymbol> reifiedClasses,
        IAssemblySymbol? currentAssembly,
        TargetLanguage target
    )
    {
        _members = members;
        _forwarding = forwarding;
        _reifiedClasses = reifiedClasses;
        _currentAssembly = currentAssembly;
        _target = target;
    }

    /// <summary>Whether <paramref name="method"/> is a self-typed static of this compilation.</summary>
    public bool Contains(IMethodSymbol method) => _members.Contains(method.OriginalDefinition);

    /// <summary>
    /// Whether the self-typed <paramref name="method"/> calls another one through its
    /// receiving class, so that class's statics are needed too.
    /// </summary>
    public bool Forwards(IMethodSymbol method) => _forwarding.Contains(method.OriginalDefinition);

    /// <summary>
    /// The type a call to <paramref name="callee"/> binds to its class's self parameter,
    /// when the call binds it to a receiving class (FR-113 item 3): the callee is
    /// self-typed, or a referenced package's CRTP static whose self argument names a class
    /// the call can receive on. <c>null</c> otherwise: a referenced static, whose body is
    /// not visible, keeps the receiver as written where a self-typed one would report
    /// <c>MS0054</c> (FR-113 item 5).
    /// </summary>
    public ITypeSymbol? ArgumentOf(IMethodSymbol callee)
    {
        if (_currentAssembly is null)
            return null;

        if (Contains(callee))
            return CrtpSelfParameter.ArgumentOf(callee);

        return
            CrtpSelfParameter.IsReferencedCandidate(callee, _currentAssembly)
            && CrtpSelfParameter.ArgumentOf(callee) is INamedTypeSymbol argument
            && !CrtpSelfParameter.IsHiddenOn(argument, callee, _target)
            && !_reifiedClasses.Contains(argument.OriginalDefinition)
            ? argument
            : null;
    }

    /// <summary>
    /// Whether <paramref name="typeParameter"/> is the self parameter of the self-typed
    /// static <paramref name="enclosingMember"/> sits in (a lambda inside it included):
    /// it then stands for the receiving class.
    /// </summary>
    public bool IsReceiverOf(ITypeParameterSymbol typeParameter, ISymbol? enclosingMember) =>
        enclosingMember is not null
        && ReifiedSymbols.MemberOf(enclosingMember, throughLocalFunctions: false)
            is IMethodSymbol member
        && Contains(member)
        && SymbolEqualityComparer.Default.Equals(
            CrtpSelfParameter.Of(member.ContainingType),
            typeParameter.OriginalDefinition
        );
}
