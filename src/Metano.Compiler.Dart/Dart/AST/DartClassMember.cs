namespace Metano.Dart.AST;

/// <summary>Base for class members (fields, methods, getters, setters).</summary>
public abstract record DartClassMember;

/// <summary>An instance or static field. When <see cref="Initializer"/> is supplied
/// the printer emits <c>= expr</c> after the declaration, which removes the need
/// for <c>late</c> on non-nullable fields.</summary>
public sealed record DartField(
    string Name,
    DartType Type,
    bool IsFinal = false,
    bool IsStatic = false,
    bool IsLate = false,
    Metano.Compiler.IR.IrExpression? Initializer = null
) : DartClassMember;

/// <summary>A method declaration. When <see cref="Body"/> is non-null the printer
/// emits the IR statements as the method body; when <see cref="IsAbstract"/> is
/// true the printer emits a signature-only declaration; otherwise it falls back to
/// a <c>throw UnimplementedError()</c> stub.
/// <see cref="OperatorSymbol"/> switches the printer to Dart operator syntax
/// (<c>operator +</c>, <c>operator ==</c>, …) and ignores <see cref="Name"/>.</summary>
public sealed record DartMethodSignature(
    string Name,
    IReadOnlyList<DartParameter> Parameters,
    DartType ReturnType,
    IReadOnlyList<DartTypeParameter>? TypeParameters = null,
    bool IsStatic = false,
    bool IsAbstract = false,
    bool IsAsync = false,
    IReadOnlyList<Metano.Compiler.IR.IrStatement>? Body = null,
    string? OperatorSymbol = null,
    bool IsOverride = false
) : DartClassMember;

/// <summary>A <c>get</c> accessor. Same body/abstract/stub rules as
/// <see cref="DartMethodSignature"/>.</summary>
public sealed record DartGetter(
    string Name,
    DartType ReturnType,
    bool IsStatic = false,
    bool IsAbstract = false,
    IReadOnlyList<Metano.Compiler.IR.IrStatement>? Body = null,
    bool IsOverride = false
) : DartClassMember;
