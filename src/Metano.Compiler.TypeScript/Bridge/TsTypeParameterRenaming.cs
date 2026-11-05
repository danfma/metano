using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renames type parameter references inside a TypeScript type
/// (<c>Box&lt;T&gt;</c> → <c>Box&lt;U&gt;</c>), so a signature declared in a
/// base class's type parameters can be compared with one declared in a
/// derived class's (spec 013 FR-026). A type parameter is a
/// <see cref="TsNamedType"/> without type arguments.
/// </summary>
internal static class TsTypeParameterRenaming
{
    /// <param name="type">The type to rewrite.</param>
    /// <param name="renaming">Type parameter name → its new name.</param>
    public static TsType Rename(TsType type, IReadOnlyDictionary<string, string> renaming) =>
        type switch
        {
            TsNamedType { TypeArguments: null or { Count: 0 } } named
                when renaming.TryGetValue(named.Name, out var renamed) => named with
            {
                Name = renamed,
            },
            TsNamedType { TypeArguments: { } arguments } named => named with
            {
                TypeArguments = RenameAll(arguments, renaming),
            },
            TsArrayType array => array with { ElementType = Rename(array.ElementType, renaming) },
            TsPromiseType promise => promise with { Inner = Rename(promise.Inner, renaming) },
            TsUnionType union => union with { Types = RenameAll(union.Types, renaming) },
            TsIntersectionType intersection => intersection with
            {
                Types = RenameAll(intersection.Types, renaming),
            },
            TsTupleType tuple => tuple with { Elements = RenameAll(tuple.Elements, renaming) },
            TsFunctionType function => RenameFunction(function, renaming),
            _ => type,
        };

    private static TsFunctionType RenameFunction(
        TsFunctionType function,
        IReadOnlyDictionary<string, string> renaming
    ) =>
        function with
        {
            Parameters = function
                .Parameters.Select(parameter =>
                    parameter.Type is { } parameterType
                        ? parameter with
                        {
                            Type = Rename(parameterType, renaming),
                        }
                        : parameter
                )
                .ToList(),
            ReturnType = Rename(function.ReturnType, renaming),
            ThisType = function.ThisType is { } thisType ? Rename(thisType, renaming) : null,
        };

    private static List<TsType> RenameAll(
        IReadOnlyList<TsType> types,
        IReadOnlyDictionary<string, string> renaming
    ) => types.Select(type => Rename(type, renaming)).ToList();
}
