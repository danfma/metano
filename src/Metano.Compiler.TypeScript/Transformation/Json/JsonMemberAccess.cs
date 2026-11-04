using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// System.Text.Json's source-generation rules for which members a contract
/// writes and which it assigns on read (FR-046), measured on .NET
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
    /// <summary>
    /// Whether System.Text.Json serializes <paramref name="member"/>: it is
    /// included (<see cref="IsIncluded"/>) and no <c>[JsonIgnore]</c> hides it
    /// both ways. The collector further leaves out indexers, constants and
    /// the <c>[JsonExtensionData]</c> overflow.
    /// </summary>
    public static bool IsSerialized(ISymbol member) =>
        !member.IsStatic
        && !member.IsImplicitlyDeclared
        && IsIncluded(member)
        && !JsonSerializationAttributes.IsAlwaysIgnored(member);

    /// <summary>
    /// Whether the contract includes <paramref name="member"/>, before
    /// <c>[JsonIgnore]</c> and <c>[JsonExtensionData]</c> are applied.
    /// </summary>
    private static bool IsIncluded(ISymbol member) =>
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
            IPropertySymbol { SetMethod: { } setter } property => IsSetterAssignable(
                property,
                setter
            ),
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

    /// <summary>
    /// A public setter, or one generated code reaches under <c>[JsonInclude]</c>.
    /// </summary>
    private static bool IsSetterAssignable(IPropertySymbol property, IMethodSymbol setter) =>
        setter.DeclaredAccessibility == Accessibility.Public
        || (HasInclude(property) && IsVisibleToGeneratedCode(setter.DeclaredAccessibility));

    private static bool HasInclude(ISymbol member) =>
        JsonSerializationAttributes.Has(member, JsonSerializationAttributes.Include);

    /// <summary>The member and each of its accessors are reachable.</summary>
    private static bool IsReachable(ISymbol member) =>
        IsVisibleToGeneratedCode(member.DeclaredAccessibility)
        && AccessorsOf(member)
            .All(accessor => IsVisibleToGeneratedCode(accessor.DeclaredAccessibility));

    private static IEnumerable<IMethodSymbol> AccessorsOf(ISymbol member) =>
        member is IPropertySymbol property
            ? new[] { property.GetMethod, property.SetMethod }.OfType<IMethodSymbol>()
            : [];

    /// <summary>
    /// The accessibility levels generated code of the same assembly can
    /// reach outside the type.
    /// </summary>
    private static bool IsVisibleToGeneratedCode(Accessibility accessibility) =>
        accessibility
            is Accessibility.Public
                or Accessibility.Internal
                or Accessibility.ProtectedOrInternal;
}
