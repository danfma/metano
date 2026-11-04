using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Emits the context's <c>readOptions</c> getter override (spec 009 FR-002,
/// FR-048 item 2): a <c>JsonReadOptions</c> literal with the members that
/// differ from System.Text.Json's defaults, in the contract's order. A context
/// with default options emits nothing, so its output stays byte-identical.
/// </summary>
internal static class JsonReadOptionsEmitter
{
    private static readonly TsTypeOrigin RuntimeJsonOrigin = new("metano-runtime", "system/json");

    /// <summary>
    /// <c>protected override get readOptions(): JsonReadOptions { return {...}; }</c>,
    /// or <c>null</c> when every option is the default.
    /// </summary>
    public static TsGetterMember? Build(JsonContextReadOptions options)
    {
        var members = new[]
        {
            options.PropertyNameCaseInsensitive
                ? new TsObjectProperty("propertyNameCaseInsensitive", new TsLiteral("true"))
                : null,
            options.ReadsNumbersFromStrings
                ? new TsObjectProperty(
                    "numberHandling",
                    new TsStringLiteral("allowReadingFromString")
                )
                : null,
            options.AllowOutOfOrderMetadataProperties
                ? new TsObjectProperty("allowOutOfOrderMetadataProperties", new TsLiteral("true"))
                : null,
        }
            .OfType<TsObjectProperty>()
            .ToList();

        if (members.Count == 0)
            return null;

        return new TsGetterMember(
            "readOptions",
            new TsNamedType("JsonReadOptions", Origin: RuntimeJsonOrigin),
            [new TsReturnStatement(new TsObjectLiteral(members))],
            Override: true,
            Accessibility: TsAccessibility.Protected
        );
    }
}
