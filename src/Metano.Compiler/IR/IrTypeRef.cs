namespace Metano.Compiler.IR;

/// <summary>
/// A semantic type reference in the IR. Represents what a type *is* in the source language,
/// not how any target renders it. Each backend maps <see cref="IrTypeRef"/> to its own
/// type system (e.g., <see cref="IrPrimitiveTypeRef"/> with <see cref="IrPrimitive.Guid"/>
/// becomes <c>UUID</c> in TypeScript, <c>String</c> in Dart).
/// </summary>
public abstract record IrTypeRef
{
    /// <summary>
    /// Whether the type is <c>object</c>: the primitive, or the named
    /// <c>System.Object</c> a reference through the <c>object</c> keyword maps to.
    /// </summary>
    public bool IsObject =>
        this
            is IrPrimitiveTypeRef { Primitive: IrPrimitive.Object }
                or IrNamedTypeRef { Name: "Object", Namespace: "System" };

    /// <summary>
    /// Whether the type is <c>object</c>, <c>ValueType</c> or <c>Enum</c>: a C#
    /// boxing conversion of a value type produces a value of it (spec 011
    /// FR-050).
    /// </summary>
    public bool AcceptsBoxedValues =>
        IsObject || this is IrNamedTypeRef { Name: "ValueType" or "Enum", Namespace: "System" };
}

/// <summary>
/// A type a backend composes from other types for its own lowering (for
/// example the TypeScript result tuple of a method with <c>out</c>
/// parameters). Core analyses that walk type references descend into
/// <see cref="ComponentTypes"/> without knowing the concrete type.
/// </summary>
public interface IIrCompositeTypeRef
{
    /// <summary>The types the composite is built from.</summary>
    IEnumerable<IrTypeRef> ComponentTypes { get; }
}

/// <summary>
/// A well-known primitive type. See <see cref="IrPrimitive"/> for the semantic catalog.
/// </summary>
public sealed record IrPrimitiveTypeRef(IrPrimitive Primitive) : IrTypeRef
{
    /// <summary>
    /// The C# numeric type when <see cref="Primitive"/> folds it into a signed
    /// counterpart (<c>sbyte</c> → <see cref="IrPrimitive.Byte"/>,
    /// <c>ushort</c> → <see cref="IrPrimitive.Int16"/>, <c>uint</c> →
    /// <see cref="IrPrimitive.Int32"/>), so a runtime type test can check the
    /// value's real range; <c>null</c> for every other primitive. It is a fact
    /// of the source type only and takes no part in equality: two references
    /// to the folded primitive stay the same type everywhere else. Read it only
    /// at the runtime test site, from the reference extraction gave that site,
    /// never from a deduplicated or cached reference (which may be either
    /// spelling). Rebuilding a reference with <c>new IrPrimitiveTypeRef(p)</c>
    /// drops it: derive a copy with <c>with</c>.
    /// </summary>
    public IrNumericKind? ExactKind { get; init; }

    /// <summary>Equality by <see cref="Primitive"/> alone.</summary>
    public bool Equals(IrPrimitiveTypeRef? other) =>
        other is not null && other.Primitive == Primitive;

    public override int GetHashCode() => Primitive.GetHashCode();
}

/// <summary>
/// A named type reference — classes, interfaces, records, structs, or any user-defined type.
/// The <see cref="Name"/> and <see cref="Namespace"/> stay in their original C# casing.
/// </summary>
/// <param name="Name">The type name in original C# casing (e.g., <c>TodoItem</c>).</param>
/// <param name="Namespace">The C# namespace, if known.</param>
/// <param name="TypeArguments">Generic type arguments (empty for non-generic types).</param>
/// <param name="Origin">Cross-assembly origin, if this type comes from another package.</param>
/// <param name="PrivateNestedSegments">How many leading segments of a dotted
/// <paramref name="Name"/> lead to a <c>private</c> nested declaration a target may keep
/// out of its container's exported surface (<c>0</c> when none is private).</param>
public sealed record IrNamedTypeRef(
    string Name,
    string? Namespace = null,
    IReadOnlyList<IrTypeRef>? TypeArguments = null,
    IrTypeOrigin? Origin = null,
    IrNamedTypeSemantics? Semantics = null,
    int PrivateNestedSegments = 0
) : IrTypeRef
{
    /// <summary>
    /// The identity <see cref="IrNamedTypeSemantics.Supertypes"/> lists a type
    /// under: its namespace and name, without type arguments.
    /// </summary>
    public SupertypeKey SupertypeKey => SupertypeKey.From(Namespace, Name);

    /// <summary>
    /// For a class, struct or interface nested in generic types, the type
    /// arguments of those containers, outermost first (<c>Gen&lt;int&gt;.Sub</c>
    /// → <c>[int]</c>), ahead of its own <see cref="TypeArguments"/>; <c>null</c>
    /// when no container is generic (<see cref="IrTypeDeclaration.ContainerTypeParameters"/>).
    /// </summary>
    public IReadOnlyList<IrTypeRef>? ContainerTypeArguments { get; init; }
}

/// <summary>
/// Semantic metadata about a named type reference that backends use to
/// decide how to render runtime tests, conversions, and dispatches.
/// Attached to <see cref="IrNamedTypeRef"/> — kept optional so existing
/// call sites that don't need the extra info aren't forced to supply it.
/// </summary>
/// <param name="Kind">The broad category of the type. Backends branch on
/// this when an <c>instanceof</c> / <c>typeof</c> / string-value test
/// differs by kind (e.g., interfaces have no class object to test against
/// at runtime, so the TS backend falls back to structural checks).</param>
/// <param name="StringEnumValues">When <see cref="Kind"/> is
/// <see cref="IrNamedTypeKind.StringEnum"/>, carries the set of string
/// values the enum can take so the TS backend can emit an exhaustive
/// equality check instead of an impossible-to-evaluate <c>instanceof</c>.</param>
/// <param name="BrandedUnderlyingPrimitive">When the type is a branded
/// wrapper, the underlying primitive the runtime value is actually
/// typed as.</param>
/// <param name="IsTranspilable">Whether the type is emitted to the
/// target (as opposed to coming from the BCL or a foreign package).
/// Governs whether a runtime class/object is guaranteed to exist.</param>
/// <param name="JsxNativeElementTag">The tag from <c>[JsxNativeElement("tag")]</c>
/// on the referenced type — a <c>new T { … }</c> of such a type lowers to an
/// intrinsic <c>&lt;tag&gt;</c> element. <c>null</c> when absent. Mirrors the
/// declaration-side <see cref="IrTypeSemantics.JsxNativeElementTag"/> so a
/// backend can decide JSX lowering off the constructed value's type ref
/// without re-resolving the Roslyn symbol.</param>
/// <param name="IsJsxComponent">The referenced type derives from a
/// <c>[JsxComponentBuilder]</c> base — a <c>new T { … }</c> in a renderable
/// position lowers to a component usage <c>&lt;T … /&gt;</c>.</param>
/// <param name="RendersAsJsxElement">The referenced type is JSX-renderable in a
/// value position (component, native element, or imported renderable typed as
/// the marked element). Drives the expression bridge's decision to route a
/// <c>new</c> through the JSX bridge.</param>
/// <param name="HasGeneratedGuard">The referenced type carries
/// <c>[GenerateGuard]</c>, so a backend that erases it at runtime (an
/// interface or a <c>[PlainObject]</c> in TypeScript) can test it through the
/// generated guard. See <see cref="IrRuntimeTypeTest"/>.</param>
/// <param name="IsPlainObject">The referenced type carries
/// <c>[PlainObject]</c>: it lowers to a structural shape with no runtime
/// class, like an interface.</param>
/// <param name="IsJsTuple">The referenced type carries <c>[JsTuple]</c>: it
/// lowers to a fixed-length array with no runtime class, so a runtime test can
/// only check the array shape and its length.</param>
/// <param name="JsTupleArity">The element count of a <c>[JsTuple]</c> type's
/// positional shape; <c>null</c> when the type is not a <c>[JsTuple]</c> or
/// its shape cannot be read.</param>
/// <param name="IsDefaultConstructible">The referenced type is a transpiled struct
/// that declares no constructor, so a backend can build the zero value C# gives an
/// unassigned field of the type with a parameterless construction of its emitted
/// class. <c>false</c> for a struct that declares any constructor (a parameterized
/// or primary one is what the emitted class takes; an explicit parameterless one
/// would run where C#'s zero value skips it), a <c>[PlainObject]</c>,
/// <c>[JsTuple]</c>, <c>[External]</c> or <c>[Import]</c> struct, a BCL struct, and
/// every non-struct type.</param>
/// <param name="IsValueType">The referenced type is a value type: a struct, a
/// record struct, an enum, a branded struct. Its <c>default</c> is a zeroed
/// value, never <c>null</c>.</param>
/// <param name="HasImplicitParameterlessOverload">The referenced type is a
/// transpiled struct with a runtime class (the exclusions of
/// <paramref name="IsDefaultConstructible"/>) whose declared constructors form
/// a dispatched set (two or more declared constructors, spec 013 D1) with no
/// parameterless one, so C#'s implicit parameterless constructor — the
/// zero value — joins the set as one more overload (spec 013 FR-027).</param>
/// <param name="ClrTypeName">The C# display name of the type's original
/// definition (<c>System.ArgumentException</c>,
/// <see cref="Metano.Compiler.BclExportReader.ExportKey"/>), the key its
/// <c>[ExportFromBcl]</c> entry is read under. Set only for
/// <see cref="IrNamedTypeKind.Exception"/> types, so a backend resolves an
/// exception's runtime class by the same key as the extraction and
/// <see cref="IrExceptionConstruction.ExceptionTypeName"/>; <c>null</c> for
/// every other kind.</param>
/// <param name="Supertypes">The keys (<see cref="IrNamedTypeRef.SupertypeKey"/>) of every base
/// class below <c>System.Object</c> and every interface the type implements, so a backend can
/// tell which of two types is the more derived (an overload dispatcher tries
/// <c>F(ArgumentException)</c> before <c>F(Exception)</c>); <c>null</c> when the type has none
/// or is not a class, record, struct, interface or exception.</param>
/// <param name="StructuralEqualityLeaves">Set for a transpiled struct, record
/// struct or sealed record whose equality is the compiler-generated, field-wise
/// one all the way down: every instance field, recursively through such types,
/// tuples and nullables, ends in one of these primitives (a branded value counts
/// as its underlying primitive; an enum records <see cref="IrPrimitive.Int32"/>,
/// or <see cref="IrPrimitive.String"/> for a <c>[StringEnum]</c>, whatever its
/// underlying type). <c>null</c> when a field is a class, an array, a
/// collection, <c>object</c> or an open type parameter (a type argument
/// of a constructed generic type is read as the type it names), which C#
/// compares by reference or by an equality the definition does not fix; when the type or a base declares its own
/// <c>Equals</c>, which replaces the field-wise one; for an unsealed record,
/// whose derived types can add state; for a type read from a metadata
/// reference, which hides its private backing fields; and for every other
/// kind. A backend admits the type to a value comparison only if it compares
/// each leaf as .NET does.</param>
public sealed record IrNamedTypeSemantics(
    IrNamedTypeKind Kind,
    IReadOnlyList<string>? StringEnumValues = null,
    IrPrimitive? BrandedUnderlyingPrimitive = null,
    bool IsTranspilable = false,
    bool IsIgnored = false,
    IrEnumMemberInfo? EnumZeroMember = null,
    bool IsExternal = false,
    string? JsxNativeElementTag = null,
    bool IsJsxComponent = false,
    bool RendersAsJsxElement = false,
    bool HasGeneratedGuard = false,
    bool IsPlainObject = false,
    bool IsJsTuple = false,
    int? JsTupleArity = null,
    bool IsDefaultConstructible = false,
    bool IsValueType = false,
    bool HasImplicitParameterlessOverload = false,
    string? ClrTypeName = null,
    IReadOnlySet<IrPrimitive>? StructuralEqualityLeaves = null,
    IReadOnlyList<SupertypeKey>? Supertypes = null
)
{
    /// <summary>
    /// Whether a numeric enum carries <c>[System.Flags]</c>, so its values are
    /// bit combinations of the members rather than one of them.
    /// </summary>
    public bool IsFlagsEnum { get; init; }

    /// <summary>
    /// The underlying numeric type of a numeric enum (<c>enum E : byte</c>), the
    /// range a runtime test of the enum checks; <c>null</c> for every other kind.
    /// </summary>
    public IrNumericKind? EnumUnderlyingKind { get; init; }

    /// <summary>
    /// Whether a transpiled struct with a runtime class declares an explicit
    /// parameterless constructor (rather than only the C# implicit one).
    /// </summary>
    public bool HasExplicitParameterlessCtor { get; init; }

    /// <summary>
    /// Whether the referenced type is a transpiled struct with a runtime class
    /// that declares a constructor (the complement of
    /// <see cref="IsDefaultConstructible"/> among such structs): its zero value
    /// cannot be built by a parameterless construction, so a backend reads it
    /// from the struct's own zero value (#802).
    /// </summary>
    public bool HasZeroValueFactory { get; init; }

    /// <summary>
    /// The referenced type is a C# 15 <c>closed</c> class (spec 010 FR-C1).
    /// </summary>
    public bool IsClosed { get; init; }

    /// <summary>
    /// The referenced interface is not transpiled but the target runtime declares it: an
    /// <c>[ExportFromBcl]</c> binds it to a package export (<c>System.IDisposable</c> →
    /// <c>metano-runtime</c>'s <c>IDisposable</c>), which carries its runtime descriptor
    /// beside it (FR-111). A backend treats it like an interface of a referenced library.
    /// </summary>
    public bool IsRuntimeDeclared { get; init; }
}

/// <summary>
/// Identifies the zero member of an enum referenced from another type. Used
/// by backends that need to render <c>default(MyEnum)</c> as a member access:
/// the runtime semantics of <c>default(E)</c> is the member with constant
/// value <c>0</c>, whatever the enum's kind (an enum that declares none has no
/// such member),
/// and resolving that requires looking inside the enum's members — which the
/// referencing type doesn't otherwise have access to.
/// </summary>
/// <param name="Name">The C# member name in its original casing
/// (e.g., <c>"Backlog"</c>) — used by <c>[StringEnum]</c> backends that key
/// the runtime object on the source name.</param>
/// <param name="EmittedName">The renamed form when the member carries a
/// target-scoped <c>[Name]</c> override; <c>null</c> when no override exists
/// or when the target prefers the original name.</param>
public sealed record IrEnumMemberInfo(string Name, string? EmittedName = null);

/// <summary>
/// Broad categorization of a named type — enough for backends to pick
/// the right runtime test / render policy without rediscovering it from
/// the Roslyn symbol.
/// </summary>
public enum IrNamedTypeKind
{
    Class,
    Record,
    Struct,
    Interface,
    NumericEnum,
    StringEnum,
    Delegate,
    Branded,
    Exception,
    Unknown,
}

/// <summary>
/// A reference to a type parameter (e.g., <c>T</c>, <c>TKey</c>).
/// </summary>
public sealed record IrTypeParameterRef(string Name) : IrTypeRef
{
    /// <summary>Where the type parameter's runtime descriptor is resolved (stage 011d).</summary>
    public IrTypeParameterScope Scope { get; init; } = IrTypeParameterScope.None;

    /// <summary>The 0-based index inside <c>$typeArgs</c> or the class slot when <see cref="Scope"/> is <see cref="IrTypeParameterScope.CtorParam"/> or <see cref="IrTypeParameterScope.ClassSlot"/>.</summary>
    public int SlotIndex { get; init; } = -1;

    /// <summary>The declaring class's emitted name when <see cref="Scope"/> is <see cref="IrTypeParameterScope.ClassSlot"/>.</summary>
    public string? DeclaringTypeName { get; init; }
}

/// <summary>
/// <c>T | null</c> — a nullable wrapper. Each backend decides how to represent nullability
/// (TypeScript: <c>T | null</c>, Kotlin: <c>T?</c>, Dart: <c>T?</c>).
/// </summary>
public sealed record IrNullableTypeRef(IrTypeRef Inner) : IrTypeRef;

/// <summary>
/// An ordered collection type (C# <c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, etc.).
/// Each backend decides the representation (TypeScript: <c>T[]</c>, Dart: <c>List&lt;T&gt;</c>).
/// </summary>
public sealed record IrArrayTypeRef(IrTypeRef ElementType) : IrTypeRef
{
    /// <summary>
    /// Whether the source type is a C# array (<c>T[]</c>), not a collection
    /// that lowers to one: a runtime test of an array checks the array, its
    /// element erased (spec 011 FR-021), while the collections' tests arrive
    /// with stage 011c. Read it only at the runtime test site: like
    /// <see cref="IrPrimitiveTypeRef.ExactKind"/> it is not part of equality,
    /// so a deduplicated ref may lose it, and rebuilding with
    /// <c>new IrArrayTypeRef(e)</c> drops it (derive a copy with <c>with</c>).
    /// </summary>
    public bool IsNative { get; init; }

    /// <summary>
    /// Whether the source type is a read-only native JS array view
    /// (<c>ReadOnlyJsArray&lt;T&gt;</c>): TypeScript emits <c>readonly T[]</c>
    /// instead of <c>T[]</c>. Unlike <see cref="IsNative"/>, this changes the
    /// emitted type, so it participates in equality.
    /// </summary>
    public bool IsReadOnly { get; init; }

    public bool Equals(IrArrayTypeRef? other) =>
        other is not null
        && IsReadOnly == other.IsReadOnly
        && ElementType.Equals(other.ElementType);

    public override int GetHashCode() =>
        HashCode.Combine(typeof(IrArrayTypeRef), ElementType, IsReadOnly);
}

/// <summary>
/// A key-value collection (C# <c>Dictionary&lt;K,V&gt;</c>, <c>IDictionary&lt;K,V&gt;</c>).
/// Each backend decides the representation (TypeScript: <c>Map&lt;K,V&gt;</c>,
/// Dart: <c>Map&lt;K,V&gt;</c>, Kotlin: <c>MutableMap&lt;K,V&gt;</c>).
/// </summary>
public sealed record IrMapTypeRef(IrTypeRef KeyType, IrTypeRef ValueType) : IrTypeRef;

/// <summary>
/// A set collection (C# <c>HashSet&lt;T&gt;</c>, <c>ISet&lt;T&gt;</c>).
/// </summary>
public sealed record IrSetTypeRef(IrTypeRef ElementType) : IrTypeRef;

/// <summary>
/// A tuple type (C# <c>ValueTuple</c> or <c>Tuple</c>).
/// </summary>
public sealed record IrTupleTypeRef(IReadOnlyList<IrTypeRef> Elements) : IrTypeRef;

/// <summary>
/// A function/delegate type (C# <c>Action&lt;T&gt;</c>, <c>Func&lt;T,R&gt;</c>,
/// or any custom delegate). <paramref name="ThisType"/> holds the
/// synthetic JavaScript <c>this</c> receiver when the delegate's
/// first parameter carried <c>[This]</c>; the parameter itself is
/// dropped from <paramref name="Parameters"/> so each backend emits
/// the shape it prefers (TypeScript inserts a <c>(this: T, …)</c>
/// annotation; Dart re-introduces the parameter positionally).
/// </summary>
public sealed record IrFunctionTypeRef(
    IReadOnlyList<IrParameter> Parameters,
    IrTypeRef ReturnType,
    IrTypeRef? ThisType = null
) : IrTypeRef;

/// <summary>
/// A callable value with one or more call signatures — the lowered form of a
/// <c>[JsCallable]</c> interface used in type position. A single-signature
/// callable is represented directly as <see cref="IrFunctionTypeRef"/>; this
/// node carries the <em>overloaded</em> case (multiple <c>Invoke</c> overloads),
/// which a single function type cannot express. Backends render it as the
/// intersection of its signatures (TypeScript: <c>((v: T) =&gt; void) &amp;
/// ((u: (c: T) =&gt; T) =&gt; void)</c>). The interface name is erased — no
/// declaration or import is emitted for it.
/// </summary>
public sealed record IrCallableTypeRef(IReadOnlyList<IrFunctionTypeRef> Signatures) : IrTypeRef;

/// <summary>
/// An asynchronous result (C# <c>Task&lt;T&gt;</c>, <c>ValueTask&lt;T&gt;</c>).
/// Each backend decides the representation (TypeScript: <c>Promise&lt;T&gt;</c>,
/// Dart: <c>Future&lt;T&gt;</c>, Kotlin: suspend function).
/// </summary>
/// <param name="IsValueType">Whether the C# type is a value type
/// (<c>ValueTask</c>, <c>ValueTask&lt;T&gt;</c>), whose <c>default</c> is a
/// completed task rather than <c>null</c>.</param>
public sealed record IrPromiseTypeRef(IrTypeRef ResultType, bool IsValueType = false) : IrTypeRef;

/// <summary>
/// A generator/iterator type (C# <c>IEnumerable&lt;T&gt;</c> from <c>yield</c> methods).
/// </summary>
public sealed record IrGeneratorTypeRef(IrTypeRef YieldType) : IrTypeRef;

/// <summary>
/// A read-only collection (C# <c>IReadOnlyCollection&lt;T&gt;</c>).
/// Semantically an iterable — each backend decides representation
/// (TypeScript: <c>Iterable&lt;T&gt;</c>).
/// </summary>
public sealed record IrIterableTypeRef(IrTypeRef ElementType) : IrTypeRef;

/// <summary>
/// A one-shot cursor over a sequence (C# <c>IEnumerator&lt;T&gt;</c>, what
/// <c>GetEnumerator()</c> returns). Each backend decides representation
/// (TypeScript: <c>Iterator&lt;T&gt;</c>, Dart: <c>Iterator&lt;T&gt;</c>). An
/// iterator method (<c>yield</c>) declared to return it maps to
/// <see cref="IrGeneratorTypeRef"/> instead.
/// </summary>
public sealed record IrIteratorTypeRef(IrTypeRef ElementType) : IrTypeRef;

/// <summary>
/// A key-value pair type (C# <c>KeyValuePair&lt;K,V&gt;</c>).
/// Each backend decides the representation (TypeScript: <c>[K, V]</c> tuple).
/// </summary>
public sealed record IrKeyValuePairTypeRef(IrTypeRef KeyType, IrTypeRef ValueType) : IrTypeRef;

/// <summary>
/// A grouping type (C# <c>IGrouping&lt;K,V&gt;</c> from LINQ).
/// </summary>
public sealed record IrGroupingTypeRef(IrTypeRef KeyType, IrTypeRef ElementType) : IrTypeRef;

/// <summary>
/// The C# <c>dynamic</c> type (spec 011 FR-030): a value with no static type
/// guarantee, whose member accesses, invocations and indexers bind at run
/// time. Targets render it as their own dynamic type (TS <c>any</c>, Dart
/// <c>dynamic</c>).
/// </summary>
public sealed record IrDynamicTypeRef : IrTypeRef;

/// <summary>
/// Fallback for types that cannot be mapped. Backends should render this as their
/// "any" or "dynamic" equivalent and optionally emit a diagnostic.
/// </summary>
public sealed record IrUnknownTypeRef : IrTypeRef;
