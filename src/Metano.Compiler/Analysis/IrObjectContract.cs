using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Facts about <c>System.Object</c>'s equality contract (<c>Equals(object)</c>
/// and <c>GetHashCode()</c>) in IR, for any class, record or not. Each backend
/// maps the contract onto its own (TypeScript's runtime <c>equals</c> /
/// <c>hashCode</c> methods, Dart's <c>operator ==</c> / <c>hashCode</c>), so the
/// recognition lives here once instead of in every backend.
/// </summary>
public static class IrObjectContract
{
    /// <summary>
    /// <c>System.Object</c>'s stable full name as member origins carry it
    /// (<see cref="SymbolHelper.GetStableFullName"/> spells it as the keyword).
    /// </summary>
    private const string ObjectStableFullName = "object";

    /// <summary>
    /// <c>object.GetHashCode</c> itself. The extraction gives every
    /// <c>GetHashCode()</c> call this origin, whichever override it binds to,
    /// except a <c>base.GetHashCode()</c> whose base declares the override.
    /// </summary>
    public static bool IsObjectGetHashCode(IrMemberOrigin origin) =>
        origin
            is {
                DeclaringTypeFullName: ObjectStableFullName,
                MemberName: nameof(object.GetHashCode)
            };

    /// <summary>
    /// A <c>GetHashCode()</c> call bound to an emitted override rather than to
    /// <c>object.GetHashCode</c> (a <c>base.GetHashCode()</c> whose base
    /// overrides it): it reads the member the override is declared as.
    /// </summary>
    public static bool IsHashCodeOverrideAccess(IrMemberOrigin origin) =>
        origin
            is { MemberName: nameof(object.GetHashCode), IsStatic: false, ParameterTypes.Count: 0 }
        && !IsObjectGetHashCode(origin);

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

    /// <summary>
    /// An instance <c>Equals(object)</c> marked <c>override</c>: the override of
    /// <c>object.Equals</c>, whose parameter is <c>object</c> (nullable or not).
    /// </summary>
    public static bool IsEqualsOverride(IrMethodDeclaration method) =>
        method is { Name: nameof(object.Equals), IsStatic: false, Parameters.Count: 1 }
        && method.Semantics.IsOverride
        && IsObjectTypeRef(method.Parameters[0].Type);

    private static bool IsObjectTypeRef(IrTypeRef type) =>
        type switch
        {
            IrNullableTypeRef nullable => IsObjectTypeRef(nullable.Inner),
            IrPrimitiveTypeRef { Primitive: IrPrimitive.Object } => true,
            // The type mapper leaves `object` a named reference to System.Object.
            IrNamedTypeRef { Name: nameof(Object), Namespace: nameof(System) } => true,
            _ => false,
        };
}
