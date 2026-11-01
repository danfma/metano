using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Classifies a property's <c>init</c> accessor that has a body
/// (<c>init =&gt; _x = value;</c> / <c>init { … }</c>). Such an accessor is
/// transpiled like a custom <c>set</c> body — as the target's setter — when the
/// property has no compiler-generated backing field: its getter and accessor bodies
/// read and write declared state only. A property backed by a synthesized field (an
/// auto <c>get;</c>, or a C# 14 <c>field</c> keyword in either accessor) has no
/// declared storage to lower the accessor onto, so its body is not transpiled and
/// writing it is reported as MS0001.
/// </summary>
internal static class CustomInitAccessor
{
    /// <summary>The property's <c>init</c> accessor has a body.</summary>
    public static bool HasBody(IPropertySymbol property) =>
        property.SetMethod is { IsInitOnly: true } init && HasAccessorBody(init);

    /// <summary>
    /// The property's <c>init</c> accessor has a body that is transpiled as a setter.
    /// </summary>
    public static bool IsTranspiled(IPropertySymbol property) =>
        HasBody(property) && !HasSynthesizedBackingField(property);

    /// <summary>
    /// The property's <c>init</c> accessor has a body that is not transpiled: writing
    /// the property through an object initializer or <c>with</c> is unsupported.
    /// </summary>
    public static bool IsUntranspiled(IPropertySymbol property) =>
        HasBody(property) && HasSynthesizedBackingField(property);

    /// <summary>
    /// A transpiled <c>init</c> accessor of <paramref name="field"/>'s type assigns
    /// it. C# lets an <c>init</c> accessor write a <c>readonly</c> field, but the
    /// target setter it lowers to cannot, so such a field is not declared readonly.
    /// Symbol-based: only assignments that bind to <paramref name="field"/> count.
    /// </summary>
    public static bool WritesField(IFieldSymbol field, Compilation compilation) =>
        field
            .ContainingType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(IsTranspiled)
            .SelectMany(property => property.SetMethod!.DeclaringSyntaxReferences)
            .Select(reference => reference.GetSyntax())
            .Any(accessor => AssignsField(accessor, field, compilation));

    private static bool AssignsField(
        SyntaxNode accessor,
        IFieldSymbol field,
        Compilation compilation
    )
    {
        var model = compilation.GetSemanticModel(accessor.SyntaxTree);

        return accessor
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(assignment.Left).Symbol,
                    field
                )
            );
    }

    private static bool HasAccessorBody(IMethodSymbol accessor) =>
        accessor.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax()
                is AccessorDeclarationSyntax { Body: not null }
                    or AccessorDeclarationSyntax { ExpressionBody: not null }
        );

    private static bool HasSynthesizedBackingField(IPropertySymbol property) =>
        property
            .ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
}
