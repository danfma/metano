namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript class field: name: Type = initializer;
/// </summary>
public sealed record TsFieldMember(
    string Name,
    TsType Type,
    TsExpression? Initializer = null,
    bool Readonly = false,
    bool Static = false,
    bool Optional = false,
    TsAccessibility Accessibility = TsAccessibility.Public
) : TsClassMember;
