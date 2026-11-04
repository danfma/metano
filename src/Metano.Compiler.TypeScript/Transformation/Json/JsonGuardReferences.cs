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

    public TsExpression Build(ITypeSymbol type)
    {
        var named = (INamedTypeSymbol)type;
        var guardName = $"is{_context.ResolveTsName(named)}";
        var mapped = JsonTsTypes.MapContract(_context, named) as TsNamedType;
        var origin = mapped?.Origin;
        var qualifiers = (mapped?.Name ?? guardName).Split('.')[..^1];

        if (qualifiers.Length == 0)
        {
            return origin is null
                ? new TsModuleExportReference(guardName, named.Name)
                : new TsTypeReference(guardName, origin);
        }

        TsExpression container = origin is null
            ? new TsIdentifier(qualifiers[0])
            : new TsTypeReference(qualifiers[0], origin);

        foreach (var qualifier in qualifiers[1..])
            container = new TsPropertyAccess(container, qualifier);

        return new TsPropertyAccess(container, guardName);
    }
}
