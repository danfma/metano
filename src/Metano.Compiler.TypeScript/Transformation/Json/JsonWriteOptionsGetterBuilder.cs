using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Builds the context's <c>writeOptions</c> getter override (spec 009 FR-050): a
/// <c>JsonWriteOptions</c> literal for an indented context, with the indent
/// members that differ from System.Text.Json's defaults. A context that does not
/// write indented text gets no getter, so its output stays byte-identical.
/// </summary>
internal static class JsonWriteOptionsGetterBuilder
{
    /// <summary>
    /// <c>protected override get writeOptions(): JsonWriteOptions { return {...}; }</c>,
    /// or <c>null</c> when the context writes compact text.
    /// </summary>
    public static TsGetterMember? Build(JsonContextWriteOptions options)
    {
        if (!options.WriteIndented)
            return null;

        return new TsGetterMember(
            "writeOptions",
            new TsNamedType("JsonWriteOptions", Origin: JsonRuntimeModule.Origin),
            [new TsReturnStatement(new TsObjectLiteral(MembersOf(options).ToList()))],
            Override: true,
            Accessibility: TsAccessibility.Protected
        );
    }

    private static IEnumerable<TsObjectProperty> MembersOf(JsonContextWriteOptions options)
    {
        yield return new TsObjectProperty("writeIndented", new TsLiteral("true"));

        if (options.IndentCharacter != JsonContextWriteOptions.DefaultIndentCharacter)
            yield return new TsObjectProperty(
                "indentCharacter",
                new TsStringLiteral(options.IndentCharacter.ToString())
            );

        if (options.IndentSize != JsonContextWriteOptions.DefaultIndentSize)
            yield return new TsObjectProperty(
                "indentSize",
                new TsLiteral(options.IndentSize.ToString())
            );

        if (options.NewLine != JsonContextWriteOptions.DefaultNewLine)
            yield return new TsObjectProperty("newLine", new TsStringLiteral(options.NewLine));
    }
}
