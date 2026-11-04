using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The expression a polymorphic descriptor uses to reference the synthesized
/// guard of a <c>[PlainObject]</c> type (FR-019 rule 1, FR-041): a value
/// import from the type's own module, resolved like the type itself.
/// <list type="bullet">
///   <item>a top-level type of this project → <c>isPoint</c>, imported from
///     the type's file (<see cref="TsModuleExportReference"/>);</item>
///   <item>a type of a referenced Metano package → <c>isPoint</c> with the
///     type's package origin (<see cref="TsTypeReference"/>);</item>
///   <item>a nested type → <c>Shape.isCircle</c>: its guard is declared in
///     the companion namespace, next to the type.</item>
/// </list>
/// The guard name is the one <see cref="TypeGuardBuilder"/> declares,
/// <c>is</c> + the type's TS name.
/// </summary>
internal sealed class JsonGuardReferences(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    public TsExpression GuardReferenceOf(INamedTypeSymbol type)
    {
        var guardName = $"is{_context.ResolveTsName(type)}";
        var mapped = (TsNamedType)JsonTsTypes.MapContract(_context, type);
        var qualifiers = mapped.Name.Split('.').SkipLast(1).ToList();

        if (qualifiers.Count == 0)
        {
            return mapped.Origin is null
                ? new TsModuleExportReference(guardName, type.Name)
                : new TsTypeReference(guardName, mapped.Origin);
        }

        TsExpression root = mapped.Origin is null
            ? new TsIdentifier(qualifiers[0])
            : new TsTypeReference(qualifiers[0], mapped.Origin);

        var container = qualifiers
            .Skip(1)
            .Aggregate(root, (outer, qualifier) => new TsPropertyAccess(outer, qualifier));

        return new TsPropertyAccess(container, guardName);
    }
}
