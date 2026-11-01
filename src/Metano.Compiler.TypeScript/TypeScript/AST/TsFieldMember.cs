namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript class field: name: Type = initializer;
/// </summary>
/// <param name="DefinitelyAssigned">When true the field carries the definite-assignment
/// assertion (<c>name!: Type;</c>), telling <c>strictPropertyInitialization</c> that code
/// outside the constructor assigns it. Only meaningful without an
/// <see cref="Initializer"/>.</param>
/// <param name="Abstract">An abstract property declaration (<c>abstract readonly name: Type;</c>):
/// it has no storage and no initializer, and a derived class implements it with a field,
/// a parameter property or an accessor.</param>
public sealed record TsFieldMember(
    string Name,
    TsType Type,
    TsExpression? Initializer = null,
    bool Readonly = false,
    bool Static = false,
    bool Optional = false,
    TsAccessibility Accessibility = TsAccessibility.Public,
    bool DefinitelyAssigned = false,
    bool Abstract = false
) : TsClassMember;
