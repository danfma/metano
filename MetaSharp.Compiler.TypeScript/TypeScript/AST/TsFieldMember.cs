namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A TypeScript class field: name: Type = initializer;
/// </summary>
public sealed record TsFieldMember(
    string Name,
    TsType Type,
    TsExpression? Initializer = null,
    bool Readonly = false,
    TsAccessibility Accessibility = TsAccessibility.Public
) : TsClassMember;
