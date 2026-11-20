using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The Roslyn facts the runtime-question scan and the extraction of
/// <c>typeof</c> / <c>GetType()</c> share (spec 011 FR-025, FR-027), so both
/// answer the same way.
/// </summary>
internal static class RuntimeQuestionFacts
{
    /// <summary>
    /// The <c>System.Type</c> members a runtime type descriptor answers (spec
    /// 011 Edge Cases): its identity surface, with no member metadata.
    /// </summary>
    private static readonly IReadOnlySet<string> SupportedTypeMembers = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "Name",
        "FullName",
        "Namespace",
        "IsInterface",
        "IsClass",
        "IsInstanceOfType",
        "IsAssignableFrom",
        "Equals",
        "GetHashCode",
        "ToString",
        "op_Equality",
        "op_Inequality",
    };

    /// <summary>
    /// Whether no runtime type descriptor stands for <paramref name="type"/>
    /// in <c>typeof</c>: <c>void</c>, a pointer, a function pointer, an open
    /// generic definition (<c>typeof(List&lt;&gt;)</c>).
    /// </summary>
    public static bool IsUnsupportedTypeOf(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Void
        || type is IPointerTypeSymbol or IFunctionPointerTypeSymbol
        || type is INamedTypeSymbol { IsUnboundGenericType: true };

    /// <summary>Whether <paramref name="type"/> is, or is built from, a type parameter.</summary>
    public static bool MentionsTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => MentionsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(MentionsTypeParameter),
            _ => false,
        };

    /// <summary>
    /// Whether <paramref name="member"/> is a <c>System.Type</c> (or
    /// <c>MemberInfo</c>) member outside the descriptor surface.
    /// </summary>
    public static bool IsUnsupportedTypeMember(ISymbol member) =>
        IsReflectionType(member.ContainingType) && !SupportedTypeMembers.Contains(member.Name);

    /// <summary>
    /// Whether <paramref name="method"/> creates or finds a type by reflection:
    /// <c>Activator.CreateInstance</c>, <c>Type.GetType(string)</c>.
    /// </summary>
    public static bool IsReflectiveLookup(IMethodSymbol method) =>
        (method.ContainingType?.ToDisplayString(), method.Name) switch
        {
            ("System.Activator", "CreateInstance") => true,
            ("System.Type", "GetType") => method.IsStatic,
            _ => false,
        };

    private static bool IsReflectionType(INamedTypeSymbol? type) =>
        type?.ToDisplayString()
            is "System.Type"
                or "System.Reflection.MemberInfo"
                or "System.Reflection.TypeInfo";

    /// <summary>The neutral facts of <paramref name="type"/> as a questioned type.</summary>
    public static IrQuestionedType QuestionedTypeOf(ITypeSymbol? type) =>
        ShapeOf(type) with
        {
            AcceptsBoxedValues =
                type is IDynamicTypeSymbol
                || type?.SpecialType
                    is SpecialType.System_Object
                        or SpecialType.System_ValueType
                        or SpecialType.System_Enum,
        };

    private static IrQuestionedType ShapeOf(ITypeSymbol? type) =>
        type switch
        {
            null => Shaped(IrQuestionedTypeShape.Other),
            IDynamicTypeSymbol => Shaped(IrQuestionedTypeShape.Dynamic),
            { SpecialType: SpecialType.System_Object } => Shaped(IrQuestionedTypeShape.Object),
            ITypeParameterSymbol parameter => new IrQuestionedType
            {
                Shape = IrQuestionedTypeShape.TypeParameter,
                Constraints =
                [
                    .. parameter.ConstraintTypes.OfType<INamedTypeSymbol>().Select(IrTypeKeys.Of),
                ],
            },
            IArrayTypeSymbol array => new IrQuestionedType
            {
                Shape = IrQuestionedTypeShape.Array,
                ElementIsObject = array.ElementType.SpecialType == SpecialType.System_Object,
            },
            INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable => QuestionedTypeOf(nullable.TypeArguments[0]),
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => Shaped(
                IrQuestionedTypeShape.Delegate
            ),
            INamedTypeSymbol named when IsPrimitiveLike(named) => Shaped(
                IrQuestionedTypeShape.Primitive
            ),
            INamedTypeSymbol { IsTupleType: false, IsAnonymousType: false } named =>
                new IrQuestionedType
                {
                    Shape = IrQuestionedTypeShape.Named,
                    Key = IrTypeKeys.Of(named),
                    CollectionDefinitionName = CollectionDefinitionNameOf(named),
                    TypeArguments =
                    [
                        .. (
                            CollectionKindResolver.KindOf(named) is { } kind
                                ? CollectionKindResolver.TypeArgumentsOf(named, kind)
                                : named.TypeArguments
                        ).Select(QuestionedTypeOf),
                    ],
                },
            _ => Shaped(IrQuestionedTypeShape.Other),
        };

    private static IrQuestionedType Shaped(IrQuestionedTypeShape shape) => new() { Shape = shape };

    /// <summary>
    /// The types the targets lower to a primitive descriptor: the C# special
    /// types, <c>Guid</c>, <c>BigInteger</c> and the temporal types.
    /// </summary>
    private static bool IsPrimitiveLike(INamedTypeSymbol type) =>
        type.SpecialType is not SpecialType.None
        || type.ToDisplayString()
            is "System.Guid"
                or "System.Numerics.BigInteger"
                or "System.DateTimeOffset"
                or "System.DateOnly"
                or "System.TimeOnly"
                or "System.TimeSpan";

    private static string? CollectionDefinitionNameOf(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        var containing = definition.ContainingNamespace?.ToDisplayString() ?? "";

        return containing.StartsWith("System.Collections", StringComparison.Ordinal)
            ? IrTypeKeys.Of(definition).MetadataName
            : null;
    }
}
