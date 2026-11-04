using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// System.Text.Json's source-generation rules for which members a contract
/// writes and which it assigns on read (FR-046, slice S6b), measured on .NET
/// 10 (SDK 10.0.401):
/// <list type="bullet">
///   <item>a public instance property is serialized; a field or a non-public
///     property only with <c>[JsonInclude]</c>, and only when the generated
///     code can reach it: <c>internal</c> and <c>protected internal</c>
///     members are written and read, while a <c>private</c> or
///     <c>protected</c> one (or a public property with such an accessor)
///     makes the contract throw <c>InvalidOperationException</c> (SYSLIB1038)
///     and is reported instead;</item>
///   <item>on read, a member not bound to a constructor parameter is assigned
///     through a public setter or <c>init</c> accessor, through an
///     <c>internal</c> one only with <c>[JsonInclude]</c>, or as a writable
///     field; a get-only property is written and never read.</item>
/// </list>
/// </summary>
internal static class JsonMemberAccess
{
    private const string IncludeAttribute = "JsonIncludeAttribute";

    /// <summary>
    /// Whether the contract serializes <paramref name="member"/>, before
    /// <c>[JsonIgnore]</c> and <c>[JsonExtensionData]</c> are applied.
    /// </summary>
    public static bool IsIncluded(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => property.DeclaredAccessibility == Accessibility.Public
                || (HasInclude(property) && IsReachable(property)),
            IFieldSymbol field => HasInclude(field) && IsReachable(field),
            _ => false,
        };

    /// <summary>
    /// Whether <paramref name="member"/> has <c>[JsonInclude]</c> but a
    /// declaration or accessor the source generator cannot reach.
    /// </summary>
    public static bool HasUnreachableInclude(ISymbol member) =>
        HasInclude(member) && !IsReachable(member);

    /// <summary>
    /// Whether a read assigns <paramref name="member"/> after construction:
    /// a writable field, or a property whose <c>set</c> / <c>init</c>
    /// accessor is public, or reachable under <c>[JsonInclude]</c>.
    /// </summary>
    public static bool IsAssignedOnRead(ISymbol member) =>
        member switch
        {
            IPropertySymbol { SetMethod: { } setter } property => setter.DeclaredAccessibility
                == Accessibility.Public
                || (HasInclude(property) && IsReachable(setter.DeclaredAccessibility)),
            IFieldSymbol field => !field.IsReadOnly,
            _ => false,
        };

    /// <summary>The type of a data member (a property or a field).</summary>
    public static ITypeSymbol TypeOf(ISymbol member) =>
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

    private static bool HasInclude(ISymbol member) =>
        JsonSerializationAttributes.Has(member, IncludeAttribute);

    /// <summary>The member and each of its accessors are reachable.</summary>
    private static bool IsReachable(ISymbol member) =>
        IsReachable(member.DeclaredAccessibility)
        && AccessorsOf(member).All(accessor => IsReachable(accessor.DeclaredAccessibility));

    private static IEnumerable<IMethodSymbol> AccessorsOf(ISymbol member) =>
        member is IPropertySymbol property
            ? new[] { property.GetMethod, property.SetMethod }.OfType<IMethodSymbol>()
            : [];

    /// <summary>
    /// The accessibility levels generated code of the same assembly can
    /// reach outside the type.
    /// </summary>
    private static bool IsReachable(Accessibility accessibility) =>
        accessibility
            is Accessibility.Public
                or Accessibility.Internal
                or Accessibility.ProtectedOrInternal;
}
