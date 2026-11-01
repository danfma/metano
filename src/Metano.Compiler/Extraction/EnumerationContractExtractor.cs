using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads the enumeration contracts (<c>IEnumerable</c>, <c>IEnumerator</c>) a class or
/// struct takes on itself, and the declared members that implement them. Members are
/// resolved through Roslyn's interface mapping
/// (<see cref="ITypeSymbol.FindImplementationForInterfaceMember"/>), so an explicit
/// implementation, a member inherited from a base class that does not take on the
/// contract, and a <c>[Name]</c> override all resolve to the member the type is
/// emitted with. A contract the base type already takes on is not reported: the
/// base carries it, and an overriding member is reached through it.
/// </summary>
internal static class EnumerationContractExtractor
{
    private const string GetEnumeratorName = "GetEnumerator";
    private const string CurrentName = "Current";
    private const string MoveNextName = "MoveNext";

    public static IrEnumerableContract? ExtractEnumerable(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver,
        TargetLanguage? target
    )
    {
        if (!TakesOnContract(type, SpecialType.System_Collections_IEnumerable))
            return null;

        var generic = GenericInterface(type, SpecialType.System_Collections_Generic_IEnumerable_T);
        var contract = generic ?? Interface(type, SpecialType.System_Collections_IEnumerable);

        if (Implementation(type, contract, GetEnumeratorName, target) is not { } getEnumerator)
            return null;

        return new IrEnumerableContract(ElementType(generic, originResolver), getEnumerator);
    }

    public static IrEnumeratorContract? ExtractEnumerator(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver,
        TargetLanguage? target
    )
    {
        if (!TakesOnContract(type, SpecialType.System_Collections_IEnumerator))
            return null;

        var nonGeneric = Interface(type, SpecialType.System_Collections_IEnumerator);
        var generic = GenericInterface(type, SpecialType.System_Collections_Generic_IEnumerator_T);
        var current = Implementation(type, generic ?? nonGeneric, CurrentName, target);
        var moveNext = Implementation(type, nonGeneric, MoveNextName, target);

        if (current is null || moveNext is null)
            return null;

        return new IrEnumeratorContract(ElementType(generic, originResolver), current, moveNext);
    }

    private static bool TakesOnContract(INamedTypeSymbol type, SpecialType contract)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return false;

        var inheritsContract = type.BaseType is { } baseType && Implements(baseType, contract);

        return Implements(type, contract) && !inheritsContract;
    }

    private static bool Implements(INamedTypeSymbol type, SpecialType contract) =>
        type.AllInterfaces.Any(i => i.SpecialType == contract);

    private static INamedTypeSymbol Interface(INamedTypeSymbol type, SpecialType contract) =>
        type.AllInterfaces.First(i => i.SpecialType == contract);

    private static INamedTypeSymbol? GenericInterface(
        INamedTypeSymbol type,
        SpecialType definition
    ) => type.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.SpecialType == definition);

    private static IrTypeRef ElementType(
        INamedTypeSymbol? genericContract,
        IrTypeOriginResolver? originResolver
    ) =>
        genericContract is { TypeArguments: [var element] }
            ? IrTypeRefMapper.Map(element, originResolver)
            : new IrPrimitiveTypeRef(IrPrimitive.Object);

    private static IrContractMember? Implementation(
        INamedTypeSymbol type,
        INamedTypeSymbol contract,
        string memberName,
        TargetLanguage? target
    )
    {
        var interfaceMember = contract.GetMembers(memberName).FirstOrDefault();

        if (interfaceMember is null)
            return null;

        return type.FindImplementationForInterfaceMember(interfaceMember) is { } implementation
            ? DeclaredMember(implementation, target)
            : null;
    }

    /// <summary>
    /// The name and attributes <paramref name="member"/> is declared with, or
    /// <c>null</c> when the target does not declare it (<c>[Ignore]</c>, or an explicit
    /// implementation the lowering drops). A superseded explicit implementation (the
    /// <c>IEnumerator IEnumerable.GetEnumerator()</c> bridge) resolves to the member
    /// that claims its name.
    /// </summary>
    private static IrContractMember? DeclaredMember(ISymbol member, TargetLanguage? target)
    {
        if (SymbolHelper.HasIgnore(member, target))
            return null;

        if (ExplicitImplementationLowering.Resolve(member, target) is not { } explicitMember)
            return new IrContractMember(member.Name, IrAttributeExtractor.Extract(member));

        return explicitMember switch
        {
            { Fate: ExplicitImplementationFate.EmittedUnderInterfaceName } => ContractMember(
                explicitMember.InterfaceMember
            ),
            { Fate: ExplicitImplementationFate.Superseded, ClaimingMember: { } claiming } =>
                ContractMember(
                    ExplicitImplementationLowering.ImplementedInterfaceMember(claiming) ?? claiming
                ),
            _ => null,
        };
    }

    private static IrContractMember ContractMember(ISymbol member) =>
        new(member.Name, IrAttributeExtractor.Extract(member));
}
