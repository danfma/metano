using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The equality members a record declares itself instead of letting C#
/// synthesize them: an <c>Equals(R? other)</c> or a <c>GetHashCode</c>
/// override replaces the synthesized one. Single source of truth for the
/// backends that synthesize the record helpers and for the runtime-requirement
/// scan that imports what those helpers use, so the two never drift (a
/// skipped <c>hashCode</c> must not leave its <c>HashCode</c> import behind).
/// </summary>
public static class IrRecordEqualityOverrides
{
    /// <summary>The record declares its own <c>Equals(R? other)</c>.</summary>
    public static bool DeclaresEquals(IrClassDeclaration record) =>
        DeclaredMethods(record)
            .Any(m =>
                m is { Name: nameof(object.Equals), IsStatic: false, Parameters.Count: 1 }
                && IsRecordTypeRef(m.Parameters[0].Type, record.Name)
            );

    /// <summary>The record overrides <c>GetHashCode</c>.</summary>
    public static bool DeclaresHashCode(IrClassDeclaration record) =>
        DeclaredMethods(record).Any(IsHashCodeOverride);

    /// <summary>
    /// An instance <c>GetHashCode()</c> marked <c>override</c>: with no
    /// parameters, the only method it can override is <c>object.GetHashCode</c>.
    /// </summary>
    public static bool IsHashCodeOverride(IrMethodDeclaration method) =>
        method
            is {
                Name: nameof(object.GetHashCode),
                IsStatic: false,
                Parameters.Count: 0,
                Semantics.IsOverride: true,
            };

    private static IEnumerable<IrMethodDeclaration> DeclaredMethods(IrClassDeclaration record) =>
        (record.Members ?? []).OfType<IrMethodDeclaration>().SelectMany(WithOverloads);

    private static IEnumerable<IrMethodDeclaration> WithOverloads(IrMethodDeclaration method) =>
        [method, .. method.Overloads ?? []];

    private static bool IsRecordTypeRef(IrTypeRef type, string recordName) =>
        type switch
        {
            IrNullableTypeRef nullable => IsRecordTypeRef(nullable.Inner, recordName),
            IrNamedTypeRef named => named.Name == recordName,
            _ => false,
        };
}
