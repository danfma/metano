using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// A name an interface the class declares requires of it (spec 013 FR-025,
/// FR-031), which no anchored overload of the class declares: a member
/// renamed by <c>[Name]</c> on a single interface member (#565), or the fast
/// path of a dispatched interface group implemented by a base method, named
/// from the base's own parameters (#595). The class declares it as a
/// forwarding member to the implementation, unless it or its base already
/// declares the name.
/// </summary>
/// <param name="Name">The name the interface declares.</param>
/// <param name="OwnImplementation">The implementing overload when the class
/// declares it; its emitted name is known once the claim pass names it.</param>
/// <param name="InheritedImplementation">The emitted name of an implementation
/// an emitted base declares.</param>
/// <param name="Signature">The implementation, whose parameters and return
/// type the forwarder declares.</param>
internal sealed record TsInterfaceRequirement(
    string Name,
    TsMethodOverloadKey? OwnImplementation,
    string? InheritedImplementation,
    IrMethodDeclaration Signature
);

/// <summary>
/// A forwarding member a class declares for an interface's name:
/// <c>name(…) { return this.target(…); }</c>.
/// </summary>
internal sealed record TsInterfaceForwarder(
    string Name,
    string Target,
    IrMethodDeclaration Signature
);

/// <summary>
/// Finds the <see cref="TsInterfaceRequirement"/>s of a class: one per method
/// of each emitted interface the class implements beyond the ones its emitted
/// base already implements. An interface the class implements more than once
/// keeps its runtime root dispatchers (DECIDED-30), an explicit or default
/// implementation is no forwarding target, and neither is an implementation
/// declared by a generic base, whose signature would name the base's type
/// parameters.
/// </summary>
internal sealed class TsInterfaceRequirementFinder(
    Func<INamedTypeSymbol, IrInterfaceDeclaration> interfaceShapeOf,
    Func<INamedTypeSymbol, TsInterfaceFastPaths> interfaceFastPathsOf,
    Func<INamedTypeSymbol, IrClassDeclaration> classShapeOf,
    Func<INamedTypeSymbol, TsOverloadClaims> claimsOf
)
{
    public IReadOnlyList<TsInterfaceRequirement> Of(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        INamedTypeSymbol? emittedBase
    )
    {
        var inherited = new HashSet<INamedTypeSymbol>(
            emittedBase?.AllInterfaces ?? [],
            SymbolEqualityComparer.Default
        );

        return type
            .AllInterfaces.Where(contract =>
                !inherited.Contains(contract)
                && SymbolHelper.IsTranspiledByItsAssembly(contract.OriginalDefinition)
                && !IsMultiplyConstructed(type, contract)
            )
            .SelectMany(contract => RequirementsOf(type, ir, contract))
            .ToList();
    }

    private IEnumerable<TsInterfaceRequirement> RequirementsOf(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        INamedTypeSymbol contract
    )
    {
        var members = contract
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(member => member is { MethodKind: MethodKind.Ordinary, IsStatic: false });

        foreach (var member in members)
        {
            if (
                type.FindImplementationForInterfaceMember(member)
                is not IMethodSymbol implementation
            )
                continue;

            if (RequirementOf(type, ir, member, implementation) is { } requirement)
                yield return requirement;
        }
    }

    private TsInterfaceRequirement? RequirementOf(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        IMethodSymbol member,
        IMethodSymbol implementation
    )
    {
        if (implementation.MethodKind is not MethodKind.Ordinary)
            return null;

        if (InterfaceNameOf(member) is not { } name)
            return null;

        var implementer = implementation.ContainingType.OriginalDefinition;

        if (SymbolEqualityComparer.Default.Equals(implementer, type.OriginalDefinition))
        {
            return DeclarationOf(implementation, ir.Members) is { } own
                ? new TsInterfaceRequirement(name, KeyOf(own), InheritedImplementation: null, own)
                : null;
        }

        if (!IsForwardableBase(implementer))
            return null;

        if (DeclarationOf(implementation, classShapeOf(implementer).Members) is not { } inherited)
            return null;

        var target = claimsOf(implementer)
            .FastPathNameAt(inherited.Name, inherited.DeclarationOrdinal)
            ?? TsClassMemberTable.MethodNameOf(inherited);

        return new TsInterfaceRequirement(name, OwnImplementation: null, target, inherited);
    }

    /// <summary>
    /// The name the interface declares for <paramref name="member"/>: the fast
    /// path of a dispatched group's overload, else the member's own name.
    /// </summary>
    private string? InterfaceNameOf(IMethodSymbol member)
    {
        var contract = member.ContainingType.OriginalDefinition;

        if (DeclarationOf(member, interfaceShapeOf(contract).Members) is not { } declaration)
            return null;

        return interfaceFastPathsOf(contract)
            .NameAt(declaration.Name, declaration.DeclarationOrdinal)
            ?? TsClassMemberTable.MethodNameOf(declaration);
    }

    /// <summary>
    /// A base class whose implementation a forwarder can call by a name its
    /// own claims give: an emitted, non-generic class (a default interface
    /// implementation is no class member).
    /// </summary>
    private static bool IsForwardableBase(INamedTypeSymbol implementer) =>
        implementer is { TypeKind: TypeKind.Class, IsGenericType: false }
        && SymbolHelper.IsTranspiledByItsAssembly(implementer);

    private static TsMethodOverloadKey KeyOf(IrMethodDeclaration declaration) =>
        new(declaration.Name, declaration.DeclarationOrdinal);

    /// <summary>
    /// The declaration among <paramref name="members"/>' method groups that
    /// stands for <paramref name="method"/>: of its name, with its parameters.
    /// </summary>
    private static IrMethodDeclaration? DeclarationOf(
        IMethodSymbol method,
        IReadOnlyList<IrMemberDeclaration>? members
    )
    {
        var definition = method.OriginalDefinition;

        var symbols = definition
            .ContainingType.GetMembers(definition.Name)
            .OfType<IMethodSymbol>()
            .ToList();

        return (members ?? [])
            .OfType<IrMethodDeclaration>()
            .SelectMany(declaration => IrMethodGroup.Of(declaration).Members)
            .FirstOrDefault(declaration =>
                declaration.Name == definition.Name
                && SymbolEqualityComparer.Default.Equals(
                    OverloadSymbols.Of(symbols, declaration.Parameters),
                    definition
                )
            );
    }

    /// <summary>Whether the class implements more than one construction of the interface.</summary>
    private static bool IsMultiplyConstructed(INamedTypeSymbol type, INamedTypeSymbol contract) =>
        type.AllInterfaces.Count(candidate =>
            SymbolEqualityComparer.Default.Equals(
                candidate.OriginalDefinition,
                contract.OriginalDefinition
            )
        ) >= 2;
}
