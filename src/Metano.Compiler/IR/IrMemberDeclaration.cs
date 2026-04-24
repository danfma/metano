namespace Metano.Compiler.IR;

/// <summary>
/// A member of a type declaration. Names stay in their original C# casing —
/// each target backend applies its own naming policy. Member-level attributes
/// (e.g., <c>[Name("x")]</c>, <c>[Ignore]</c>) are carried on <see cref="Attributes"/>.
/// </summary>
public abstract record IrMemberDeclaration(string Name, IrVisibility Visibility, bool IsStatic)
{
    public IReadOnlyList<IrAttribute>? Attributes { get; init; }
}

/// <summary>
/// A method declaration.
/// </summary>
public sealed record IrMethodDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IReadOnlyList<IrParameter> Parameters,
    IrTypeRef ReturnType,
    IReadOnlyList<IrStatement>? Body,
    IrMethodSemantics Semantics,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrMethodDeclaration>? Overloads = null
) : IrMemberDeclaration(Name, Visibility, IsStatic);

/// <summary>
/// A property declaration. Carries per-accessor visibility so shapes like
/// <c>public string Status { get; private set; }</c> are representable.
/// </summary>
public sealed record IrPropertyDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IrTypeRef Type,
    IrPropertyAccessors Accessors,
    IrVisibility? SetterVisibility = null,
    IrExpression? Initializer = null,
    IReadOnlyList<IrStatement>? GetterBody = null,
    IReadOnlyList<IrStatement>? SetterBody = null,
    IrPropertySemantics? Semantics = null,
    bool IsOptional = false
) : IrMemberDeclaration(Name, Visibility, IsStatic);

/// <summary>
/// A field declaration.
/// </summary>
/// <param name="IsCapturedByCtor">When <c>true</c>, this field's
/// <see cref="Initializer"/> is a bare identifier that names a constructor
/// parameter (DI-style: <c>private readonly IFoo _foo = foo;</c>). The
/// matching <see cref="IrConstructorParameter.CapturedFieldName"/> carries
/// the back-reference. Backends emit the <c>this._foo = foo</c> assignment
/// in the constructor body and skip the field's initializer at the same
/// time so the value is assigned exactly once.</param>
public sealed record IrFieldDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IrTypeRef Type,
    bool IsReadonly,
    IrExpression? Initializer = null,
    bool IsCapturedByCtor = false,
    bool IsConstant = false
) : IrMemberDeclaration(Name, Visibility, IsStatic);

/// <summary>
/// An event declaration.
/// </summary>
public sealed record IrEventDeclaration(
    string Name,
    IrVisibility Visibility,
    bool IsStatic,
    IrTypeRef HandlerType
) : IrMemberDeclaration(Name, Visibility, IsStatic);
