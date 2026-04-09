namespace MetaSharp.TypeScript.AST;

/// <param name="Optional">When true the property is rendered with a <c>?</c> suffix
/// (<c>name?: Type</c>) — used for [PlainObject] interface fields whose source C#
/// constructor parameter has a default value.</param>
public sealed record TsProperty(
    string Name,
    TsType Type,
    bool Readonly = false,
    TsAccessibility Accessibility = TsAccessibility.Public,
    bool Optional = false);
