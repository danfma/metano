using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Resolves the members a C# record's synthesized value equality compares at
/// its own level: every instance field the record declares — an
/// auto-property's backing field stands for the property. Like the
/// compiler-generated <c>Equals</c>, which calls <c>base.Equals</c> before
/// comparing its own fields, a backend compares the members of a record base
/// it emits (a transpilable base the class extends) by chaining to that
/// base's equality, so inherited members — private ones included — are never
/// read from the derived record. A base the backend does not emit has no
/// members on the emitted object, so its members are not compared.
/// </summary>
public static class IrRecordEqualityMemberExtractor
{
    /// <summary>
    /// The equality members of <paramref name="type"/>, or <c>null</c> when
    /// it is not a record.
    /// </summary>
    public static IReadOnlyList<IrRecordEqualityMember>? Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver,
        TargetLanguage? target
    )
    {
        if (!type.IsRecord)
            return null;

        return DeclaredMembers(type, target)
            .Select(member => ToEqualityMember(member, originResolver, target))
            .ToList();
    }

    /// <summary>
    /// The members whose values the record's own fields hold, in declaration
    /// order. A source record exposes its backing fields; a record loaded
    /// from metadata does not import private fields, so its settable
    /// instance properties stand in (get-only auto-properties of a
    /// referenced record are not seen).
    /// </summary>
    private static IEnumerable<ISymbol> DeclaredMembers(
        INamedTypeSymbol record,
        TargetLanguage? target
    )
    {
        var members = IsFromSource(record)
            ? FieldBackedMembers(record)
            : SettableProperties(record);

        return members.Where(member => !SymbolHelper.HasIgnore(member, target));
    }

    private static bool IsFromSource(INamedTypeSymbol record) =>
        record.Locations.Any(location => location.IsInSource);

    private static IEnumerable<ISymbol> FieldBackedMembers(INamedTypeSymbol record) =>
        record
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic && !field.IsConst)
            .Select(field => (ISymbol?)field.AssociatedSymbol ?? field);

    private static IEnumerable<ISymbol> SettableProperties(INamedTypeSymbol record) =>
        record
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                !property.IsStatic
                && !property.IsIndexer
                && !property.IsImplicitlyDeclared
                && property.SetMethod is not null
            );

    private static IrRecordEqualityMember ToEqualityMember(
        ISymbol member,
        IrTypeOriginResolver? originResolver,
        TargetLanguage? target
    ) =>
        new(
            member.Name,
            IrTypeRefMapper.Map(MemberType(member), originResolver, target),
            SymbolHelper.GetNameOverride(member, target)
        );

    private static ITypeSymbol MemberType(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => throw new ArgumentOutOfRangeException(
                nameof(member),
                member,
                "Not a data member."
            ),
        };
}
