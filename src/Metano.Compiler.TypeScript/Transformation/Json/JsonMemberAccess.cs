using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>How a read sets a serialized member that no constructor parameter binds.</summary>
internal enum JsonReadAssignment
{
    /// <summary>Never: a get-only property or a read-only field (written only).</summary>
    None,

    /// <summary>
    /// Always, through the object initializer: an <c>init</c> accessor or a
    /// <c>required</c> member; a missing key sets the default.
    /// </summary>
    Always,

    /// <summary>
    /// Only when the payload carries the key: a <c>set</c> accessor or a
    /// writable field; a missing key keeps the constructed value.
    /// </summary>
    IfPresent,
}

/// <summary>Why the source generator cannot use a <c>[JsonInclude]</c> member.</summary>
internal enum JsonIncludeProblem
{
    None,

    /// <summary>
    /// A <c>private</c> / <c>protected</c> declaration or accessor, a
    /// read-only field, or a <c>protected internal</c> member of an assembly
    /// that does not grant access: the contract throws
    /// <c>InvalidOperationException</c>.
    /// </summary>
    Throws,

    /// <summary>
    /// An <c>internal</c> member of another assembly that does not grant
    /// <c>InternalsVisibleTo</c>: the source generator skips it.
    /// </summary>
    Skipped,
}

/// <summary>
/// System.Text.Json's source-generation rules for which members a contract
/// writes and how it assigns them on read (FR-046), measured on .NET 10 (SDK
/// 10.0.401) and judged from the context's assembly:
/// <list type="bullet">
///   <item>a public instance property with a public getter is serialized
///     (<c>{ private get; set; }</c>, <c>{ protected get; set; }</c> and
///     <c>{ internal get; set; }</c> are neither written nor read); a field or
///     a non-public property only with <c>[JsonInclude]</c>, and only when the
///     generated code can reach the member and its accessors: <c>internal</c>
///     and <c>protected internal</c> ones of the context's assembly, or of an
///     assembly granting it <c>InternalsVisibleTo</c>;</item>
///   <item>an unreachable <c>[JsonInclude]</c> member makes the contract throw
///     <c>InvalidOperationException</c> (SYSLIB1038), except an
///     <c>internal</c> one of another assembly, which is silently skipped; a
///     <c>[JsonInclude]</c> read-only field is written, and a payload that
///     carries it throws;</item>
///   <item>a read sets an unbound member through its object initializer when
///     it is <c>init</c> or <c>required</c> (a missing key sets the default),
///     and otherwise through a public setter, a reachable one under
///     <c>[JsonInclude]</c>, or as a writable field, only when the key is
///     present.</item>
/// </list>
/// </summary>
internal sealed class JsonMemberAccess(IAssemblySymbol? contextAssembly)
{
    private readonly IAssemblySymbol? _contextAssembly = contextAssembly;

    /// <summary>
    /// Whether System.Text.Json serializes <paramref name="member"/>: it is
    /// included and no <c>[JsonIgnore]</c> hides it both ways. The collector
    /// further leaves out indexers, constants and the
    /// <c>[JsonExtensionData]</c> overflow.
    /// </summary>
    public bool IsSerialized(ISymbol member) =>
        !member.IsStatic
        && !member.IsImplicitlyDeclared
        && IsIncluded(member)
        && !JsonSerializationAttributes.IsAlwaysIgnored(member);

    /// <summary>What prevents the source generator from using a <c>[JsonInclude]</c> member.</summary>
    public JsonIncludeProblem IncludeProblemOf(ISymbol member)
    {
        if (!HasInclude(member))
            return JsonIncludeProblem.None;

        if (member is IFieldSymbol { IsReadOnly: true })
            return JsonIncludeProblem.Throws;

        if (IsReachable(member))
            return JsonIncludeProblem.None;

        return AccessibilitiesOf(member).All(IsInternalOrPublic)
            ? JsonIncludeProblem.Skipped
            : JsonIncludeProblem.Throws;
    }

    /// <summary>How a read assigns <paramref name="member"/> when no constructor parameter binds it.</summary>
    public JsonReadAssignment ReadAssignmentOf(ISymbol member) =>
        member switch
        {
            IPropertySymbol { SetMethod: { } setter } property
                when IsSetterAssignable(property, setter) => setter.IsInitOnly
            || IsRequired(property)
                ? JsonReadAssignment.Always
                : JsonReadAssignment.IfPresent,
            IFieldSymbol { IsReadOnly: false } field => IsRequired(field)
                ? JsonReadAssignment.Always
                : JsonReadAssignment.IfPresent,
            _ => JsonReadAssignment.None,
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
    /// The C# <c>required</c> modifier or <c>[JsonRequired]</c>: System.Text.Json
    /// rejects a payload without the member.
    /// </summary>
    public static bool IsRequired(ISymbol member) =>
        member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }
        || JsonSerializationAttributes.Has(member, JsonSerializationAttributes.Required);

    /// <summary>
    /// Whether the contract includes <paramref name="member"/>, before
    /// <c>[JsonIgnore]</c> and <c>[JsonExtensionData]</c> are applied. A
    /// <c>[JsonInclude]</c> read-only field is written, so it is included
    /// (and reported).
    /// </summary>
    private bool IsIncluded(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => HasPublicGetter(property)
                || (HasInclude(property) && IsReachable(property)),
            IFieldSymbol field => HasInclude(field) && IsReachable(field),
            _ => false,
        };

    /// <summary>A public property whose getter, when it has one, is public too.</summary>
    private static bool HasPublicGetter(IPropertySymbol property) =>
        property.DeclaredAccessibility == Accessibility.Public
        && property.GetMethod?.DeclaredAccessibility is null or Accessibility.Public;

    /// <summary>
    /// A public setter, or one generated code reaches under <c>[JsonInclude]</c>.
    /// </summary>
    private bool IsSetterAssignable(IPropertySymbol property, IMethodSymbol setter) =>
        setter.DeclaredAccessibility == Accessibility.Public
        || (
            HasInclude(property) && IsVisibleToGeneratedCode(property, setter.DeclaredAccessibility)
        );

    private static bool HasInclude(ISymbol member) =>
        JsonSerializationAttributes.Has(member, JsonSerializationAttributes.Include);

    /// <summary>The member and each of its accessors are reachable.</summary>
    private bool IsReachable(ISymbol member) =>
        AccessibilitiesOf(member)
            .All(accessibility => IsVisibleToGeneratedCode(member, accessibility));

    /// <summary>The declared accessibility of the member and of each accessor.</summary>
    private static IEnumerable<Accessibility> AccessibilitiesOf(ISymbol member)
    {
        IEnumerable<IMethodSymbol> accessors = member is IPropertySymbol property
            ? new[] { property.GetMethod, property.SetMethod }.OfType<IMethodSymbol>()
            : [];

        return accessors
            .Select(accessor => accessor.DeclaredAccessibility)
            .Prepend(member.DeclaredAccessibility);
    }

    private static bool IsInternalOrPublic(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Internal;

    /// <summary>
    /// Whether code the source generator emits in the context's assembly,
    /// outside the type, reaches <paramref name="accessibility"/>: public
    /// always, <c>internal</c> and <c>protected internal</c> only within the
    /// member's assembly or one granting <c>InternalsVisibleTo</c> to the
    /// context's.
    /// </summary>
    private bool IsVisibleToGeneratedCode(ISymbol member, Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal or Accessibility.ProtectedOrInternal => GrantsInternalAccess(
                member.ContainingAssembly
            ),
            _ => false,
        };

    /// <summary>
    /// Whether the member's assembly lets the context's see its internals; an
    /// unknown assembly on either side counts as the same one.
    /// </summary>
    private bool GrantsInternalAccess(IAssemblySymbol? memberAssembly) =>
        memberAssembly is null
        || _contextAssembly is null
        || SymbolEqualityComparer.Default.Equals(memberAssembly, _contextAssembly)
        || memberAssembly.GivesAccessTo(_contextAssembly);
}
