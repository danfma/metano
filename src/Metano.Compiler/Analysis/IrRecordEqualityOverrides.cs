using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The equality members a record declares itself instead of letting C#
/// synthesize them: an <c>Equals(R? other)</c> or a <c>GetHashCode</c>
/// override replaces the synthesized one. Single source of truth for the
/// backends that synthesize the record helpers (the TypeScript record
/// synthesis and the Dart class bridge) and for the runtime-requirement scan
/// that imports what those helpers use, so the three never drift (a skipped
/// <c>hashCode</c> must not leave its <c>HashCode</c> import behind).
/// </summary>
public static class IrRecordEqualityOverrides
{
    /// <summary>
    /// The record declares its own <c>Equals(R? other)</c>. The declaration
    /// carries no namespace, so the parameter type is matched on the record's
    /// name and arity; an <c>Equals</c> overload over a same-named type of
    /// another namespace would also match.
    /// </summary>
    public static bool DeclaresEquals(IrClassDeclaration record) =>
        DeclaredMethods(record)
            .Any(m =>
                m is { Name: nameof(object.Equals), IsStatic: false, Parameters.Count: 1 }
                && IsRecordTypeRef(m.Parameters[0].Type, record)
            );

    /// <summary>The record overrides <c>GetHashCode</c>.</summary>
    public static bool DeclaresHashCode(IrClassDeclaration record) =>
        DeclaredMethods(record).Any(IrObjectContract.IsHashCodeOverride);

    private static IEnumerable<IrMethodDeclaration> DeclaredMethods(IrClassDeclaration record) =>
        (record.Members ?? []).OfType<IrMethodDeclaration>().SelectMany(WithOverloads);

    private static IEnumerable<IrMethodDeclaration> WithOverloads(IrMethodDeclaration method) =>
        [method, .. method.Overloads ?? []];

    private static bool IsRecordTypeRef(IrTypeRef type, IrClassDeclaration record) =>
        type switch
        {
            IrNullableTypeRef nullable => IsRecordTypeRef(nullable.Inner, record),
            IrNamedTypeRef named => named.Name == record.Name
                && (named.TypeArguments?.Count ?? 0) == (record.TypeParameters?.Count ?? 0),
            _ => false,
        };
}
