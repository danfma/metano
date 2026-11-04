using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Builds the context's <c>readOptions</c> getter override (spec 009 FR-002,
/// FR-048 item 2): a <c>JsonReadOptions</c> literal with the members that
/// differ from System.Text.Json's defaults, in the contract's order. A context
/// with default options gets no getter, so its output stays byte-identical.
/// </summary>
internal static class JsonReadOptionsGetterBuilder
{
    /// <summary>
    /// <c>protected override get readOptions(): JsonReadOptions { return {...}; }</c>,
    /// or <c>null</c> when every option is the default.
    /// </summary>
    public static TsGetterMember? Build(JsonContextReadOptions options)
    {
        var members = NonDefaultMembersOf(options).ToList();

        if (members.Count == 0)
            return null;

        return new TsGetterMember(
            "readOptions",
            new TsNamedType("JsonReadOptions", Origin: JsonRuntimeModule.Origin),
            [new TsReturnStatement(new TsObjectLiteral(members))],
            Override: true,
            Accessibility: TsAccessibility.Protected
        );
    }

    private static IEnumerable<TsObjectProperty> NonDefaultMembersOf(JsonContextReadOptions options)
    {
        if (options.PropertyNameCaseInsensitive)
            yield return new TsObjectProperty("propertyNameCaseInsensitive", new TsLiteral("true"));

        if (options.ReadsNumbersFromStrings)
            yield return new TsObjectProperty(
                "numberHandling",
                new TsStringLiteral("allowReadingFromString")
            );

        if (options.AllowOutOfOrderMetadataProperties)
            yield return new TsObjectProperty(
                "allowOutOfOrderMetadataProperties",
                new TsLiteral("true")
            );
    }
}
