namespace Metano.Compiler.IR;

/// <summary>
/// How a value of a type is recognized at runtime, as neutral facts about the
/// type (spec 011 FR-021, stage 011b): the single decision table behind every
/// type test a backend lowers (<c>is T</c>, <c>as T</c>, patterns, casts,
/// overload dispatch). The categories say what the type is, never how a
/// target renders it: whether a test becomes <c>instanceof</c>, <c>typeof</c>,
/// a validating helper, a guard call or a descriptor read is the target's
/// decision.
/// </summary>
public abstract record IrRuntimeTypeTest
{
    private IrRuntimeTypeTest() { }

    /// <summary>
    /// The value is an instance of the type's runtime class: classes, records,
    /// structs, exceptions, the temporal types, and the BCL types that lower
    /// to a runtime class (<c>Task</c>, the 2.x maps and sets).
    /// </summary>
    public sealed record NominalClass : IrRuntimeTypeTest;

    /// <summary>
    /// A transpiled interface, tested nominally: a class instance implements it
    /// when its class declares it (directly or through a base or a base
    /// interface).
    /// </summary>
    public sealed record NominalInterface : IrRuntimeTypeTest;

    /// <summary>A <c>[PlainObject]</c>: the type has no runtime class, only a shape.</summary>
    public sealed record ErasedShape : IrRuntimeTypeTest;

    /// <summary>
    /// The value lowers to a JS primitive with the runtime representation of
    /// <paramref name="PrimitiveKind"/>: primitives themselves, enums (an
    /// <see cref="IrPrimitive.Int32"/> or <see cref="IrPrimitive.String"/>
    /// value of a declared set) and <c>[InlineWrapper]</c> / branded wrappers
    /// (their underlying primitive). <paramref name="StaticPrimitive"/> is the
    /// primitive of the tested expression's static type when it is the tested
    /// one (an <c>int?</c> tested for <c>int</c>), so the test only has to tell
    /// the value from <c>null</c>; <c>null</c> when the static type cannot
    /// decide (<c>object</c>, an interface, a type parameter).
    /// </summary>
    public sealed record Primitive(IrPrimitive PrimitiveKind, IrPrimitive? StaticPrimitive)
        : IrRuntimeTypeTest
    {
        /// <summary>
        /// The C# numeric type whose range the value must be in when
        /// <see cref="PrimitiveKind"/> folds it (<c>uint</c>, <c>ushort</c>,
        /// <c>sbyte</c>), or the underlying type of a numeric enum; <c>null</c>
        /// otherwise.
        /// </summary>
        public IrNumericKind? ExactKind { get; init; }
    }

    /// <summary>
    /// The type lowers to a fixed-length JS array (a <c>[JsTuple]</c>), so the
    /// value is recognized by its array shape and <paramref name="Arity"/>.
    /// The test is structural: any array of that length matches, and element
    /// types are not checked.
    /// </summary>
    public sealed record FixedTuple(int Arity) : IrRuntimeTypeTest;

    /// <summary>A native array; the element type is erased.</summary>
    public sealed record Array : IrRuntimeTypeTest;

    /// <summary>An <c>IEnumerable&lt;T&gt;</c> or an enumerator.</summary>
    public sealed record Iterable : IrRuntimeTypeTest;

    /// <summary>The <c>null</c> type.</summary>
    public sealed record Null : IrRuntimeTypeTest;

    /// <summary><c>System.Object</c>: any value but <c>null</c>.</summary>
    public sealed record Object : IrRuntimeTypeTest;

    /// <summary>
    /// The static type of the tested expression decides the answer: a
    /// non-nullable value type tested for itself (<paramref name="Result"/>
    /// <c>true</c>) or for another value type (<c>false</c>). C# itself warns
    /// (CS0183 / CS0184).
    /// </summary>
    public sealed record Constant(bool Result) : IrRuntimeTypeTest;

    /// <summary>
    /// A type parameter tested at runtime via its reified descriptor (spec 011 FR-024).
    /// </summary>
    public sealed record Reified(IrTypeParameterRef TypeParameter) : IrRuntimeTypeTest;

    /// <summary>
    /// No runtime test exists: a delegate, <c>void</c>, a type parameter, a
    /// collection of the 2.x mapping, an interface or another named type with
    /// no runtime identity.
    /// </summary>
    public sealed record None : IrRuntimeTypeTest;

    /// <summary>
    /// Whether extraction lets a test against <paramref name="type"/> reach a
    /// target that erases static types: a type with a runtime test, a
    /// <c>[PlainObject]</c> (its shape) and a delegate, whose tests the target
    /// recognizes or rejects with its own diagnostic.
    /// A C# array tests as an array, its element erased (MS0046); a collection
    /// always has a test (the target's descriptor, spec 011 D31). Iterables,
    /// <c>object</c> and the remaining <see cref="None"/> types keep the
    /// extraction's MS0001.
    /// </summary>
    public static bool ExistsFor(IrTypeRef type) =>
        type is IrCollectionTypeRef
        || Of(type) switch
        {
            NominalClass or NominalInterface or Primitive or FixedTuple => true,
            Reified { TypeParameter.Scope: not IrTypeParameterScope.None } => true,
            Array => type is IrArrayTypeRef { IsNative: true },
            // A target recognizes a plain object by a guard it synthesizes, or
            // reports that it cannot (spec 011 FR-020); a generic one has no
            // test yet.
            ErasedShape => !IsGeneric(type),
            _ => IsDelegate(type),
        };

    /// <summary>
    /// The category of a test against <paramref name="type"/> of a value whose
    /// static type is <paramref name="staticType"/> (<c>null</c> when unknown,
    /// as for an overload dispatcher's argument).
    /// </summary>
    public static IrRuntimeTypeTest Of(IrTypeRef type, IrTypeRef? staticType = null) =>
        ConstantOf(type, staticType) ?? OfType(type, StaticPrimitiveOf(staticType));

    private static bool IsGeneric(IrTypeRef type) =>
        type is IrNamedTypeRef { TypeArguments.Count: > 0 };

    /// <summary>Whether <paramref name="type"/> is a delegate type.</summary>
    public static bool IsDelegate(IrTypeRef type) =>
        type is IrFunctionTypeRef or IrNamedTypeRef { Semantics.Kind: IrNamedTypeKind.Delegate };

    private static IrRuntimeTypeTest OfType(IrTypeRef type, IrPrimitive? staticPrimitive) =>
        type switch
        {
            IrPrimitiveTypeRef { Primitive: var primitive } primitiveType => WithExactKind(
                OfPrimitive(primitive, staticPrimitive),
                primitiveType.ExactKind
            ),
            IrNamedTypeRef { Semantics: var semantics } => OfNamed(semantics, staticPrimitive),
            IrCollectionTypeRef collection => OfType(collection.LegacyForm, staticPrimitive),
            IrArrayTypeRef => new Array(),
            IrIterableTypeRef or IrIteratorTypeRef => new Iterable(),
            IrMapTypeRef or IrSetTypeRef or IrPromiseTypeRef => new NominalClass(),
            IrTypeParameterRef typeParam => new Reified(typeParam),
            _ => new None(),
        };

    /// <summary>
    /// Temporal values are class instances; <c>object</c> accepts any value but
    /// <c>null</c>, and <c>void</c> has no value to test.
    /// </summary>
    private static IrRuntimeTypeTest OfPrimitive(
        IrPrimitive primitive,
        IrPrimitive? staticPrimitive
    ) =>
        primitive switch
        {
            IrPrimitive.Object => new Object(),
            IrPrimitive.Void => new None(),
            IrPrimitive.DateTime
            or IrPrimitive.DateTimeOffset
            or IrPrimitive.DateOnly
            or IrPrimitive.TimeOnly
            or IrPrimitive.TimeSpan => new NominalClass(),
            _ => new Primitive(primitive, staticPrimitive == primitive ? primitive : null),
        };

    /// <summary>A named type without semantics keeps the class test it always had.</summary>
    private static IrRuntimeTypeTest OfNamed(
        IrNamedTypeSemantics? semantics,
        IrPrimitive? staticPrimitive
    ) =>
        semantics switch
        {
            null => new NominalClass(),
            { Kind: IrNamedTypeKind.NumericEnum } => new Primitive(IrPrimitive.Int32, null)
            {
                ExactKind = semantics.EnumUnderlyingKind,
            },
            { Kind: IrNamedTypeKind.StringEnum } => new Primitive(IrPrimitive.String, null),
            { Kind: IrNamedTypeKind.Branded } => OfBranded(
                semantics.BrandedUnderlyingPrimitive,
                staticPrimitive
            ),
            { IsJsTuple: true } => OfJsTuple(semantics.JsTupleArity),
            { IsPlainObject: true } => new ErasedShape(),
            { Kind: IrNamedTypeKind.Interface } => OfInterface(semantics),
            { Kind: IrNamedTypeKind.Delegate or IrNamedTypeKind.Unknown } => new None(),
            _ => new NominalClass(),
        };

    private static IrRuntimeTypeTest WithExactKind(IrRuntimeTypeTest test, IrNumericKind? kind) =>
        test is Primitive primitive && kind is not null
            ? primitive with
            {
                ExactKind = kind,
            }
            : test;

    /// <summary>
    /// An interface has a runtime identity only where something emits it: a
    /// transpiled interface that is neither external nor ignored, or one the
    /// target runtime declares (FR-111).
    /// </summary>
    private static IrRuntimeTypeTest OfInterface(IrNamedTypeSemantics semantics) =>
        semantics
            is { IsTranspilable: true, IsExternal: false, IsIgnored: false }
                or { IsRuntimeDeclared: true }
            ? new NominalInterface()
            : new None();

    /// <summary>
    /// A <c>[JsTuple]</c> has no class at runtime, so only its array shape is
    /// testable — and only when its arity is known.
    /// </summary>
    private static IrRuntimeTypeTest OfJsTuple(int? arity) =>
        arity is { } length ? new FixedTuple(length) : new None();

    /// <summary>
    /// A branded wrapper has no class at runtime: it is recognized as its
    /// underlying primitive, when that one is a JS primitive.
    /// </summary>
    private static IrRuntimeTypeTest OfBranded(
        IrPrimitive? underlying,
        IrPrimitive? staticPrimitive
    ) =>
        underlying is { } primitive
        && OfPrimitive(primitive, staticPrimitive) is Primitive primitiveTest
            ? primitiveTest
            : new None();

    /// <summary>
    /// The fold of a test whose static type decides it: a non-nullable value
    /// type tested for a value type. <c>null</c> when the test has to run.
    /// </summary>
    private static Constant? ConstantOf(IrTypeRef type, IrTypeRef? staticType)
    {
        if (ValueTypeIdentityOf(staticType) is not { } staticIdentity)
            return null;

        return ValueTypeIdentityOf(type) is { } testedIdentity
            ? new Constant(staticIdentity == testedIdentity)
            : null;
    }

    /// <summary>
    /// An identity for a non-nullable value type that is never anything else
    /// at runtime: a numeric, <c>bool</c>, <c>char</c> or <c>Guid</c>
    /// primitive, or a named value type (an enum, a struct, a branded
    /// wrapper). <c>null</c> for every other type, including <c>string</c> (a
    /// reference that may be <c>null</c>) and the temporal types.
    /// </summary>
    private static string? ValueTypeIdentityOf(IrTypeRef? type) =>
        type switch
        {
            IrPrimitiveTypeRef { Primitive: var primitive } when IsValuePrimitive(primitive) =>
                primitive.ToString(),
            IrNamedTypeRef { Semantics.IsValueType: true } named =>
                $"{named.Namespace}.{named.Name}",
            _ => null,
        };

    private static bool IsValuePrimitive(IrPrimitive primitive) =>
        primitive
            is not (
                IrPrimitive.String
                or IrPrimitive.Object
                or IrPrimitive.Void
                or IrPrimitive.DateTime
                or IrPrimitive.DateTimeOffset
                or IrPrimitive.DateOnly
                or IrPrimitive.TimeOnly
                or IrPrimitive.TimeSpan
            );

    /// <summary>
    /// The primitive the tested expression's static type is, through a
    /// nullable; <c>null</c> for any other static type.
    /// </summary>
    private static IrPrimitive? StaticPrimitiveOf(IrTypeRef? staticType) =>
        staticType switch
        {
            IrNullableTypeRef { Inner: var inner } => StaticPrimitiveOf(inner),
            IrPrimitiveTypeRef { Primitive: var primitive } => primitive,
            _ => null,
        };
}
