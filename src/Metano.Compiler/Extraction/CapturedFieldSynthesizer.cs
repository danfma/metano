using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Synthesizes the backing fields for the primary-constructor parameters the
/// capture walk collected: one private field per parameter, named after the
/// code base's convention (<c>view</c> → <c>_view</c>) and moved past any
/// colliding member name. A parameter something in the type writes gets a
/// mutable field; a read-only one gets <c>readonly</c>.
/// </summary>
internal static class CapturedFieldSynthesizer
{
    /// <param name="CapturedParams">The captured parameters, keyed to their synthesized field's name — the map the identifier rewrite and the constructor annotation consume.</param>
    /// <param name="Fields">The synthesized fields, in capture order, to append to the class's members.</param>
    public static (
        IReadOnlyDictionary<ISymbol, string> CapturedParams,
        IReadOnlyList<IrFieldDeclaration> Fields
    ) Synthesize(
        IEnumerable<IParameterSymbol> capturedParams,
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver,
        PrimaryConstructorPromotion promotion
    )
    {
        // TypeScript shares one namespace across fields, properties and
        // methods, so the collision check has to consider every member's
        // name — not just fields — or the synthesizer would happily emit a
        // private field that shadows a public method.
        var takenNames = type.GetMembers().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var capturedParamsByField = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var fields = new List<IrFieldDeclaration>();

        foreach (var param in capturedParams)
        {
            // A member of the same name (a hand-written `_a`) moves the
            // synthesized field to the next free name (`_a2`, `_a3`, …).
            var fieldName = FreeFieldName(param.Name, takenNames);

            takenNames.Add(fieldName);
            capturedParamsByField[param] = fieldName;
            fields.Add(
                new IrFieldDeclaration(
                    Name: fieldName,
                    Visibility: IrVisibility.Private,
                    IsStatic: false,
                    Type: IrTypeRefMapper.Map(param.Type, originResolver),
                    IsReadonly: !promotion.IsWritten(param),
                    Initializer: null,
                    IsCapturedByCtor: true
                )
            );
        }

        return (capturedParamsByField, fields);
    }

    /// <summary>
    /// <see cref="SynthesizeFieldName"/>'s name for <paramref name="paramName"/>,
    /// or, when a member already has it, the first free one with a numeric
    /// suffix, so the synthesized field never shadows a member.
    /// </summary>
    private static string FreeFieldName(string paramName, IReadOnlySet<string> takenNames)
    {
        var baseName = SynthesizeFieldName(paramName);

        return Enumerable
            .Range(2, int.MaxValue - 2)
            .Select(suffix => baseName + suffix)
            .Prepend(baseName)
            .First(name => !takenNames.Contains(name));
    }

    /// <summary>
    /// Picks the synthesized field name from a primary-ctor param's source
    /// name. The convention matches the rest of the code base: camelCase
    /// parameter prefixed with an underscore (<c>view</c> → <c>_view</c>,
    /// <c>onClick</c> → <c>_onClick</c>). Reserved JS keywords already get
    /// their reserved-word treatment downstream when the backend renders the
    /// field, so the leading underscore is enough here.
    /// </summary>
    private static string SynthesizeFieldName(string paramName)
    {
        if (string.IsNullOrEmpty(paramName))
            return "_value";
        var first = char.ToLowerInvariant(paramName[0]);
        return paramName.Length == 1 ? "_" + first : "_" + first + paramName[1..];
    }
}
