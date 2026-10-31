namespace Metano.Compiler.IR;

/// <summary>
/// How a target that erases static types (TypeScript) recognizes a value of a
/// type at runtime. The single decision behind every type test a backend lowers
/// (<c>is T</c>, <c>as T</c>): extraction gates on it, so a type with
/// <see cref="None"/> reports MS0001 instead of reaching a backend, and the
/// backend renders the remaining cases without deciding again.
/// </summary>
public abstract record IrRuntimeTypeTest
{
    private IrRuntimeTypeTest() { }

    /// <summary>The value is an instance of the type's runtime class.</summary>
    public sealed record Instanceof : IrRuntimeTypeTest;

    /// <summary>
    /// The value lowers to a JS primitive with the runtime representation of
    /// <paramref name="Primitive"/>: primitives themselves, enums (number or
    /// string) and branded wrappers (their underlying primitive).
    /// </summary>
    public sealed record Typeof(IrPrimitive Primitive) : IrRuntimeTypeTest;

    /// <summary>
    /// The type is erased (an interface or a <c>[PlainObject]</c>), and its
    /// <c>[GenerateGuard]</c> guard tests the value's shape.
    /// </summary>
    public sealed record Guard : IrRuntimeTypeTest;

    /// <summary>
    /// The type lowers to a fixed-length JS array (a <c>[JsTuple]</c>), so the
    /// value is recognized by its array shape and <paramref name="Arity"/>.
    /// The test is structural: any array of that length matches, and element
    /// types are not checked.
    /// </summary>
    public sealed record ArrayTuple(int Arity) : IrRuntimeTypeTest;

    /// <summary>
    /// No runtime test exists: an erased type without a guard, a type
    /// parameter, a collection or any other structural shape.
    /// </summary>
    public sealed record None : IrRuntimeTypeTest;

    public static IrRuntimeTypeTest Of(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef { Primitive: var primitive } => OfPrimitive(primitive),
            IrNamedTypeRef { Semantics: var semantics } => OfNamed(semantics),
            _ => new None(),
        };

    /// <summary>
    /// Temporal values are class instances; <c>object</c> and <c>void</c>
    /// have no runtime representation to test.
    /// </summary>
    private static IrRuntimeTypeTest OfPrimitive(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Object or IrPrimitive.Void => new None(),
            IrPrimitive.DateTime
            or IrPrimitive.DateTimeOffset
            or IrPrimitive.DateOnly
            or IrPrimitive.TimeOnly
            or IrPrimitive.TimeSpan => new Instanceof(),
            _ => new Typeof(primitive),
        };

    /// <summary>
    /// A named type without semantics keeps the <c>instanceof</c> test it
    /// always had.
    /// </summary>
    private static IrRuntimeTypeTest OfNamed(IrNamedTypeSemantics? semantics) =>
        semantics switch
        {
            null => new Instanceof(),
            { Kind: IrNamedTypeKind.NumericEnum } => new Typeof(IrPrimitive.Int32),
            { Kind: IrNamedTypeKind.StringEnum } => new Typeof(IrPrimitive.String),
            { Kind: IrNamedTypeKind.Branded } => OfBranded(semantics.BrandedUnderlyingPrimitive),
            { IsJsTuple: true } => OfJsTuple(semantics.JsTupleArity),
            { Kind: IrNamedTypeKind.Interface } or { IsPlainObject: true } =>
                semantics.HasGeneratedGuard ? new Guard() : new None(),
            { Kind: IrNamedTypeKind.Delegate or IrNamedTypeKind.Unknown } => new None(),
            _ => new Instanceof(),
        };

    /// <summary>
    /// A <c>[JsTuple]</c> has no class at runtime, so only its array shape is
    /// testable — and only when its arity is known.
    /// </summary>
    private static IrRuntimeTypeTest OfJsTuple(int? arity) =>
        arity is { } length ? new ArrayTuple(length) : new None();

    /// <summary>
    /// A branded wrapper has no class at runtime, so only a wrapper over a
    /// <c>typeof</c>-testable primitive has a test.
    /// </summary>
    private static IrRuntimeTypeTest OfBranded(IrPrimitive? underlying) =>
        underlying is { } primitive && OfPrimitive(primitive) is Typeof primitiveTest
            ? primitiveTest
            : new None();
}
