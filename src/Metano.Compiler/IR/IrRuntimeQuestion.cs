namespace Metano.Compiler.IR;

/// <summary>What a runtime question asks (spec 011 FR-027).</summary>
public enum IrRuntimeQuestionKind
{
    /// <summary><c>x is T</c>, a type pattern, a <c>switch</c> arm or case label.</summary>
    Test,

    /// <summary><c>x as T</c>.</summary>
    As,

    /// <summary>An explicit cast <c>(T)x</c> the source wrote.</summary>
    Cast,

    /// <summary><c>x.GetType()</c>; the questioned type is the receiver's static type.</summary>
    GetType,

    /// <summary><c>typeof(T)</c>.</summary>
    TypeOf,

    /// <summary>A parameter type of an overload group that a target may dispatch at runtime.</summary>
    OverloadParameter,

    /// <summary>
    /// A boxing conversion of an enum or <c>[InlineWrapper]</c> value (spec 011
    /// FR-050); the questioned type is the boxed type, the input type the
    /// conversion's target (<c>object</c>, <c>ValueType</c>, <c>Enum</c> or an
    /// interface). Conversions into an interop-bound slot are not recorded.
    /// </summary>
    Box,

    /// <summary>
    /// A construction of a collection type (an object creation, a collection
    /// expression, a <c>ToList()</c> / <c>ToDictionary()</c> / <c>ToHashSet()</c>
    /// call, an array converted to a collection interface) or a class deriving
    /// from one; the type is the constructed type. It asks nothing at runtime: a
    /// target that carries type arguments reaches the argument types from it
    /// (spec 011 FR-008).
    /// </summary>
    CollectionConstruction,
}

/// <summary>The shape of a questioned type, as far as a runtime question cares.</summary>
public enum IrQuestionedTypeShape
{
    /// <summary>A class, record, struct, interface or enum (<see cref="IrQuestionedType.Key"/>).</summary>
    Named,

    /// <summary>A C# primitive, <c>string</c>, <c>decimal</c> or a temporal type.</summary>
    Primitive,

    /// <summary><c>System.Object</c>.</summary>
    Object,

    /// <summary><c>dynamic</c>.</summary>
    Dynamic,

    /// <summary>An array (<see cref="IrQuestionedType.ElementIsObject"/>).</summary>
    Array,

    /// <summary>A type parameter (<see cref="IrQuestionedType.Constraints"/>).</summary>
    TypeParameter,

    /// <summary>A delegate type.</summary>
    Delegate,

    /// <summary>The <c>null</c> literal (a <c>GetType()</c> receiver only).</summary>
    NullLiteral,

    /// <summary>Any other type: a pointer, a tuple, an anonymous type, an error type.</summary>
    Other,
}

/// <summary>
/// A type a runtime question is about, as neutral facts: its shape, and the
/// definition key of a named type or of a type parameter's constraints, which a
/// target resolves against the compilation it holds.
/// </summary>
public sealed record IrQuestionedType
{
    /// <summary>The shape of the type.</summary>
    public required IrQuestionedTypeShape Shape { get; init; }

    /// <summary>The definition of a <see cref="IrQuestionedTypeShape.Named"/> type.</summary>
    public IrTypeKey? Key { get; init; }

    /// <summary>
    /// The class and interface constraints of a
    /// <see cref="IrQuestionedTypeShape.TypeParameter"/>, in declaration order;
    /// empty for an unconstrained one.
    /// </summary>
    public IReadOnlyList<IrTypeKey> Constraints { get; init; } = [];

    /// <summary>Whether an <see cref="IrQuestionedTypeShape.Array"/>'s element type is <c>object</c>.</summary>
    public bool ElementIsObject { get; init; }

    /// <summary>
    /// The CLR definition name of a collection type (<c>System.Collections.Generic.List`1</c>),
    /// <c>null</c> for any other type.
    /// </summary>
    public string? CollectionDefinitionName { get; init; }

    /// <summary>
    /// The type arguments of a constructed <see cref="IrQuestionedTypeShape.Named"/>
    /// type, in order, so a target that specializes a generic descriptor
    /// (spec 011 FR-023) reaches the arguments' descriptors too; empty otherwise.
    /// </summary>
    public IReadOnlyList<IrQuestionedType> TypeArguments { get; init; } = [];

    /// <summary>
    /// Whether the type is <c>object</c>, <c>ValueType</c>, <c>Enum</c> or
    /// <c>dynamic</c>: a C# boxing conversion of any value type (or enum) can
    /// produce a value of it.
    /// </summary>
    public bool AcceptsBoxedValues { get; init; }
}

/// <summary>
/// One runtime question a transpilable type's code asks (spec 011 FR-027,
/// D29), recorded by the frontend so a target decides which descriptors its
/// output needs before it lowers any body.
/// </summary>
public sealed record IrRuntimeQuestion
{
    /// <summary>What the question asks.</summary>
    public required IrRuntimeQuestionKind Kind { get; init; }

    /// <summary>
    /// The tested, cast-to or <c>typeof</c> type; for
    /// <see cref="IrRuntimeQuestionKind.GetType"/>, the receiver's static type.
    /// </summary>
    public required IrQuestionedType Type { get; init; }

    /// <summary>Where the question is asked.</summary>
    public IrSourceSpan? Span { get; init; }

    /// <summary>
    /// The static type of the tested value of a test, <c>as</c> or cast;
    /// <c>null</c> for the other kinds or when unknown.
    /// </summary>
    public IrQuestionedType? InputType { get; init; }

    /// <summary>The source text of a test, <c>as</c> or cast, for a target's message.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// For a <see cref="IrRuntimeQuestionKind.Box"/>, whether a type question reads
    /// the converted value locally: the conversion is the direct operand of a test,
    /// <c>as</c>, cast, pattern, <c>switch</c> or <c>GetType()</c>, or initializes a
    /// local whose only uses are such operands in the same body.
    /// </summary>
    public bool FeedsTypeQuestion { get; init; }
}
