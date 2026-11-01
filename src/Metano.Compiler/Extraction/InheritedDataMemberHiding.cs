using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports a field or property that hides an inherited instance field or
/// property of the same name (with <c>new</c>, or implicitly, as C# warns with
/// CS0108). C# keeps a separate storage for each, so a read through the base
/// type sees the base value. A TypeScript or Dart class has a single member
/// per name, so both declarations share one storage and that read sees the
/// hiding member's value instead. The output is still produced; MS0001 makes
/// the divergence visible. Known gaps (#343): members are matched by C# name,
/// and a private base member that shares the name is not reported although
/// a TypeScript <c>private</c> member still shares the storage.
/// </summary>
internal static class InheritedDataMemberHiding
{
    public static void ReportIfHiding(ISymbol member, TargetLanguage? target)
    {
        if (HiddenDataMemberOf(member, target) is not { } hidden)
            return;

        var hiddenOwner = hidden.ContainingType.Name;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{member.ContainingType.Name}.{member.Name}' hides the inherited "
                    + $"'{hiddenOwner}.{hidden.Name}'. The generated class has one member per "
                    + $"name, so both share one storage and a read through '{hiddenOwner}' sees "
                    + "the hiding member's value.",
                member.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// The inherited member <paramref name="member"/> hides, matched by C#
    /// name (a <c>[Name]</c> rename is not considered).
    /// </summary>
    private static ISymbol? HiddenDataMemberOf(ISymbol member, TargetLanguage? target)
    {
        if (!CanHide(member))
            return null;

        return BaseTypesOf(member.ContainingType)
            .SelectMany(type => type.GetMembers(member.Name))
            .FirstOrDefault(inherited => IsHideableBy(inherited, target));
    }

    private static bool CanHide(ISymbol member) =>
        IsInstanceDataMember(member) && member is not IPropertySymbol { IsOverride: true };

    private static bool IsHideableBy(ISymbol inherited, TargetLanguage? target) =>
        IsInstanceDataMember(inherited)
        && inherited.DeclaredAccessibility is not Accessibility.Private
        && !SymbolHelper.HasIgnore(inherited, target);

    private static bool IsInstanceDataMember(ISymbol member) =>
        member is (IFieldSymbol or IPropertySymbol { IsIndexer: false }) && !member.IsStatic;

    private static IEnumerable<INamedTypeSymbol> BaseTypesOf(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            yield return baseType;
    }
}
