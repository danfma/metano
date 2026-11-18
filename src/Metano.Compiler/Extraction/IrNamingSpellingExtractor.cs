using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads a parameter's <see cref="IrNamingSpelling"/> from its Roslyn symbol
/// (spec 013 FR-002, contracts/naming.md § Spellings): exact CLR names for the
/// primitives (<c>uint</c> → <c>UInt32</c>), <c>&lt;Element&gt;Array</c>,
/// <c>&lt;Inner&gt;OrNull</c>, <c>&lt;C&gt;Of&lt;A&gt;&lt;B&gt;</c>,
/// <c>TupleOf&lt;A&gt;&lt;B&gt;</c>, <c>RefOf</c> / <c>OutOf</c> /
/// <c>InOf</c>, the type parameter's name and <c>Value</c> for an error type.
/// Each named type carries its own qualifier chain for the climb (FR-003).
/// </summary>
internal static class IrNamingSpellingExtractor
{
    private const string GlobalQualifier = "Global";

    public static IrNamingSpelling Extract(IParameterSymbol parameter)
    {
        var components = new List<IrNamingComponent>();

        if (RefKindPrefix(parameter.RefKind) is { } prefix)
            components.Add(Unqualified(prefix));

        AddType(parameter.Type, components);

        return new IrNamingSpelling(components);
    }

    /// <summary>The spelling of a type outside a parameter (a conversion's target type).</summary>
    public static IrNamingSpelling Extract(ITypeSymbol type)
    {
        var components = new List<IrNamingComponent>();

        AddType(type, components);

        return new IrNamingSpelling(components);
    }

    private static void AddType(ITypeSymbol type, List<IrNamingComponent> components)
    {
        if (IsAnnotatedReference(type))
        {
            AddType(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), components);
            components.Add(Unqualified("OrNull"));

            return;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                AddType(array.ElementType, components);
                components.Add(Unqualified("Array"));
                break;

            case INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable:
                AddType(nullable.TypeArguments[0], components);
                components.Add(Unqualified("OrNull"));
                break;

            case INamedTypeSymbol { IsTupleType: true } tuple:
                components.Add(Unqualified("TupleOf"));
                AddTypes(tuple.TupleElements.Select(element => element.Type), components);
                break;

            case ITypeParameterSymbol typeParameter:
                components.Add(Unqualified(NamingText.Capitalize(typeParameter.Name)));
                break;

            case IDynamicTypeSymbol:
                components.Add(Unqualified("Object"));
                break;

            case INamedTypeSymbol named when PrimitiveSpelling(named.SpecialType) is { } primitive:
                components.Add(Unqualified(primitive));
                break;

            case INamedTypeSymbol { TypeKind: not TypeKind.Error } named:
                AddNamedType(named, components);
                break;

            default:
                components.Add(Unqualified("Value"));
                break;
        }
    }

    private static void AddTypes(IEnumerable<ITypeSymbol> types, List<IrNamingComponent> components)
    {
        foreach (var type in types)
            AddType(type, components);
    }

    private static void AddNamedType(INamedTypeSymbol named, List<IrNamingComponent> components)
    {
        components.Add(
            new IrNamingComponent(
                NamingText.Capitalize(named.Name),
                QualifierChain(named),
                TypeIdentity(named)
            )
        );

        if (named.TypeArguments.Length == 0)
            return;

        components.Add(Unqualified("Of"));
        AddTypes(named.TypeArguments, components);
    }

    /// <summary>
    /// Containing types nearest first, then namespace segments nearest first,
    /// then <c>Global</c>. A generic containing type carries its type arguments
    /// (<c>Outer&lt;int&gt;.Inner</c> climbs to <c>OuterOfInt32Inner</c>), so two
    /// constructions of it stay apart.
    /// </summary>
    private static List<string> QualifierChain(INamedTypeSymbol named)
    {
        var chain = new List<string>();

        for (
            var container = named.ContainingType;
            container is not null;
            container = container.ContainingType
        )
            chain.Add(ContainerQualifier(container));

        for (
            var ns = named.ContainingNamespace;
            ns is { IsGlobalNamespace: false };
            ns = ns.ContainingNamespace
        )
            chain.Add(NamingText.Capitalize(ns.Name));

        chain.Add(GlobalQualifier);

        return chain;
    }

    private static string ContainerQualifier(INamedTypeSymbol container)
    {
        var name = NamingText.Capitalize(container.Name);

        if (container.TypeArguments.Length == 0)
            return name;

        return name
            + "Of"
            + string.Concat(container.TypeArguments.Select(argument => Extract(argument).Leaf));
    }

    private static string TypeIdentity(INamedTypeSymbol named) =>
        $"{named.ContainingAssembly?.Identity.Name}::"
        + named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool IsAnnotatedReference(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated
        && (type.IsReferenceType || type is ITypeParameterSymbol { IsValueType: false });

    private static string? RefKindPrefix(RefKind refKind) =>
        refKind switch
        {
            RefKind.Ref or RefKind.RefReadOnlyParameter => "RefOf",
            RefKind.Out => "OutOf",
            RefKind.In => "InOf",
            _ => null,
        };

    private static string? PrimitiveSpelling(SpecialType specialType) =>
        specialType switch
        {
            SpecialType.System_SByte => "SByte",
            SpecialType.System_Byte => "Byte",
            SpecialType.System_Int16 => "Int16",
            SpecialType.System_UInt16 => "UInt16",
            SpecialType.System_Int32 => "Int32",
            SpecialType.System_UInt32 => "UInt32",
            SpecialType.System_Int64 => "Int64",
            SpecialType.System_UInt64 => "UInt64",
            SpecialType.System_IntPtr => "IntPtr",
            SpecialType.System_UIntPtr => "UIntPtr",
            SpecialType.System_Single => "Single",
            SpecialType.System_Double => "Double",
            SpecialType.System_Decimal => "Decimal",
            SpecialType.System_Boolean => "Boolean",
            SpecialType.System_Char => "Char",
            SpecialType.System_String => "String",
            SpecialType.System_Object => "Object",
            _ => null,
        };

    private static IrNamingComponent Unqualified(string text) => new(text, []);
}
