using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Analysis;

/// <summary>A runtime question over a type parameter (<c>is T</c>, <c>new T()</c>, …).</summary>
internal sealed record TriggerSite(
    ISymbol? Owner,
    ITypeParameterSymbol TypeParameter,
    ISymbol EnclosingMember,
    Location Location,
    bool IsNewConstraint
);

/// <summary>A call, or a method-group conversion, from <see cref="Caller"/>.</summary>
internal sealed record CallEdge(
    ISymbol Caller,
    IMethodSymbol Callee,
    Location Location,
    bool IsMethodGroup = false
);

/// <summary>A method and the base or interface member it overrides or implements.</summary>
internal sealed record GroupEdge(IMethodSymbol Member, IMethodSymbol BaseOrIface);

/// <summary>A construction of a generic type from <see cref="Caller"/>.</summary>
internal sealed record NewEdge(ISymbol Caller, INamedTypeSymbol TargetType, Location Location);
