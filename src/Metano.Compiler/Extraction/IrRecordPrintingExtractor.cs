using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads the facts behind a record's .NET text (baseline FR-093) from Roslyn:
/// who supplies its <c>ToString</c> and <c>PrintMembers</c> (Roslyn declares the
/// synthesized ones implicitly; a referenced assembly marks them
/// <c>[CompilerGenerated]</c>), and the members C#'s <c>PrintMembers</c> prints at
/// the record's own level, each with the default text the interpolation hole
/// <c>$"{this.Member}"</c> extracts to. The printable-member list follows C#, not
/// value equality: public instance fields and readable properties, no indexer
/// and no <c>override</c> property (the base prints it through virtual dispatch).
/// </summary>
internal static class IrRecordPrintingExtractor
{
    private const string PrintMembersName = "PrintMembers";

    /// <summary>
    /// The printing facts of <paramref name="type"/>, or <c>null</c> when it has no
    /// record value equality or the extraction runs without a compilation (shape-only
    /// mode, which extracts no expression).
    /// </summary>
    public static IrRecordPrinting? Extract(
        INamedTypeSymbol type,
        IrTypeSemantics semantics,
        IrExtractionContext context
    )
    {
        if (
            semantics.ValueEquality.Kind is not IrValueEqualityKind.Record
            || context.Compilation is not { } compilation
            || type.DeclaringSyntaxReferences.FirstOrDefault() is not { } declaration
        )
            return null;

        var extractor = new IrExpressionExtractor(
            compilation.GetSemanticModel(declaration.SyntaxTree),
            context
        );

        return new IrRecordPrinting(
            type.Name,
            ToStringFate(type),
            PrintMembersFate(type),
            PrintableMembers(type, context.Target)
                .Select(member => new IrRecordPrintMember(
                    member.Name,
                    extractor.ExtractMemberDefaultText(member, MemberType(member))
                ))
                .ToList()
        );
    }

    private static IrRecordPrintFate ToStringFate(INamedTypeSymbol type) =>
        type.GetMembers(nameof(ToString))
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method is { IsStatic: false, Parameters.Length: 0 }) switch
        {
            null => IrRecordPrintFate.None,
            { IsImplicitlyDeclared: true } => IrRecordPrintFate.Synthesized,
            _ => IrRecordPrintFate.UserDeclared,
        };

    /// <summary>
    /// <see cref="IrRecordPrintFate.UserDeclared"/> when the record or a record it
    /// derives from declares <c>PrintMembers</c>: a target that chains through its
    /// own synthesized member cannot reach that text.
    /// </summary>
    private static IrRecordPrintFate PrintMembersFate(INamedTypeSymbol type)
    {
        for (var current = type; current is { IsRecord: true }; current = current.BaseType)
        {
            if (DeclaresPrintMembers(current))
                return IrRecordPrintFate.UserDeclared;
        }

        return IrRecordPrintFate.Synthesized;
    }

    private static bool DeclaresPrintMembers(INamedTypeSymbol record) =>
        record
            .GetMembers(PrintMembersName)
            .OfType<IMethodSymbol>()
            .Any(method =>
                method is { IsStatic: false, Parameters.Length: 1 }
                && !method.IsImplicitlyDeclared
                && !method.IsCompilerGenerated()
            );

    /// <summary>
    /// The members C#'s <c>PrintMembers</c> prints at <paramref name="record"/>'s own
    /// level, in declaration order, without those <c>[Ignore]</c> removes from the
    /// emitted object.
    /// </summary>
    internal static IEnumerable<ISymbol> PrintableMembers(
        INamedTypeSymbol record,
        Metano.Annotations.TargetLanguage? target
    ) =>
        record
            .GetMembers()
            .Where(member =>
                member is { DeclaredAccessibility: Accessibility.Public, IsStatic: false }
                && IsPrintable(member)
                && !member.HasIgnore(target)
            );

    private static bool IsPrintable(ISymbol member) =>
        member switch
        {
            IFieldSymbol field => !field.IsConst,
            IPropertySymbol property => property
                is { IsIndexer: false, IsOverride: false, GetMethod: not null },
            _ => false,
        };

    /// <summary>The type of a printable field or property.</summary>
    internal static ITypeSymbol MemberType(ISymbol member) =>
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
}
