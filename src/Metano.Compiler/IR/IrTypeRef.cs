namespace Metano.Compiler.IR;

/// <summary>
/// A semantic type reference in the IR. Represents what a type *is* in the source language,
/// not how any target renders it. Each backend maps <see cref="IrTypeRef"/> to its own
/// type system (e.g., <see cref="IrPrimitiveTypeRef"/> with <see cref="IrPrimitive.Guid"/>
/// becomes <c>UUID</c> in TypeScript, <c>String</c> in Dart).
/// </summary>
public abstract record IrTypeRef;

/// <summary>
/// A well-known primitive type. See <see cref="IrPrimitive"/> for the semantic catalog.
/// </summary>
public sealed record IrPrimitiveTypeRef(IrPrimitive Primitive) : IrTypeRef;

/// <summary>
/// A named type reference — classes, interfaces, records, structs, or any user-defined type.
/// The <see cref="Name"/> and <see cref="Namespace"/> stay in their original C# casing.
/// </summary>
/// <param name="Name">The type name in original C# casing (e.g., <c>TodoItem</c>).</param>
/// <param name="Namespace">The C# namespace, if known.</param>
/// <param name="TypeArguments">Generic type arguments (empty for non-generic types).</param>
/// <param name="Origin">Cross-assembly origin, if this type comes from another package.</param>
public sealed record IrNamedTypeRef(
    string Name,
    string? Namespace = null,
    IReadOnlyList<IrTypeRef>? TypeArguments = null,
    IrTypeOrigin? Origin = null,
    IrNamedTypeSemantics? Semantics = null
) : IrTypeRef;

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
/// <param name="InlineWrappedPrimitive">When the type is an inline
/// wrapper, the underlying primitive the runtime value is actually
/// typed as.</param>
/// <param name="IsTranspilable">Whether the type is emitted to the
/// target (as opposed to coming from the BCL or a foreign package).
/// Governs whether a runtime class/object is guaranteed to exist.</param>
public sealed record IrNamedTypeSemantics(
    IrNamedTypeKind Kind,
    IReadOnlyList<string>? StringEnumValues = null,
    IrPrimitive? InlineWrappedPrimitive = null,
    bool IsTranspilable = false,
    bool IsNoEmit = false,
    IrEnumMemberInfo? EnumDefaultMember = null
);

/// <summary>
/// Identifies the default member of an enum referenced from another type. Used
/// by backends that need to render <c>default(MyEnum)</c> as a member access:
/// the runtime semantics of <c>default(E)</c> is the member with constant
/// value <c>0</c> (or the first declared member for <c>[StringEnum]</c>),
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
    InlineWrapper,
    Exception,
    Unknown,
}

/// <summary>
/// A reference to a type parameter (e.g., <c>T</c>, <c>TKey</c>).
/// </summary>
public sealed record IrTypeParameterRef(string Name) : IrTypeRef;

/// <summary>
/// <c>T | null</c> — a nullable wrapper. Each backend decides how to represent nullability
/// (TypeScript: <c>T | null</c>, Kotlin: <c>T?</c>, Dart: <c>T?</c>).
/// </summary>
public sealed record IrNullableTypeRef(IrTypeRef Inner) : IrTypeRef;

/// <summary>
/// An ordered collection type (C# <c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, etc.).
/// Each backend decides the representation (TypeScript: <c>T[]</c>, Dart: <c>List&lt;T&gt;</c>).
/// </summary>
public sealed record IrArrayTypeRef(IrTypeRef ElementType) : IrTypeRef;

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
/// or any custom delegate).
/// </summary>
public sealed record IrFunctionTypeRef(IReadOnlyList<IrParameter> Parameters, IrTypeRef ReturnType)
    : IrTypeRef;

/// <summary>
/// An asynchronous result (C# <c>Task&lt;T&gt;</c>, <c>ValueTask&lt;T&gt;</c>).
/// Each backend decides the representation (TypeScript: <c>Promise&lt;T&gt;</c>,
/// Dart: <c>Future&lt;T&gt;</c>, Kotlin: suspend function).
/// </summary>
public sealed record IrPromiseTypeRef(IrTypeRef ResultType) : IrTypeRef;

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
/// A key-value pair type (C# <c>KeyValuePair&lt;K,V&gt;</c>).
/// Each backend decides the representation (TypeScript: <c>[K, V]</c> tuple).
/// </summary>
public sealed record IrKeyValuePairTypeRef(IrTypeRef KeyType, IrTypeRef ValueType) : IrTypeRef;

/// <summary>
/// A grouping type (C# <c>IGrouping&lt;K,V&gt;</c> from LINQ).
/// </summary>
public sealed record IrGroupingTypeRef(IrTypeRef KeyType, IrTypeRef ElementType) : IrTypeRef;

/// <summary>
/// Fallback for types that cannot be mapped. Backends should render this as their
/// "any" or "dynamic" equivalent and optionally emit a diagnostic.
/// </summary>
public sealed record IrUnknownTypeRef : IrTypeRef;
