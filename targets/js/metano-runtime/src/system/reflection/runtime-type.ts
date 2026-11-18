/**
 * The shapes of the runtime type descriptors (spec 011, `contracts/runtime-types.md`).
 * Types only: a descriptor is a data literal the compiler (or a runtime class) writes
 * out, so declaring one imports no code from this module.
 */

/** The .NET primitive a {@link PrimitiveType} describes. */
export type PrimitiveName =
  | "Boolean"
  | "Byte"
  | "SByte"
  | "Int16"
  | "UInt16"
  | "Int32"
  | "UInt32"
  | "Int64"
  | "UInt64"
  | "Single"
  | "Double"
  | "Decimal"
  | "BigInteger"
  | "Char"
  | "String"
  | "Guid"
  | "DateTime"
  | "DateTimeOffset"
  | "DateOnly"
  | "TimeOnly"
  | "TimeSpan";

/** The discriminant of every {@link RuntimeType}. */
export type RuntimeTypeKind = RuntimeType["kind"];

/** The members every descriptor has. Assignability is `isAssignableFrom`, not a method. */
export interface RuntimeTypeBase<T> {
  readonly kind: string;
  /** The C# simple name; a specialization renders `List<String>`. */
  readonly name: string;
  /** The C# full name with its namespace. */
  readonly fullName: string;
  readonly namespace: string;
  /** A self-contained test of a value against the type. */
  test(value: unknown): value is T;
  /** The type's default value; absent means `null`. */
  default?(): T;
}

/** A class, record or struct lowered to a class, or a runtime exception class. */
export interface ClassType<T> extends RuntimeTypeBase<T> {
  readonly kind: "class";
  readonly ctor: abstract new (...args: any[]) => T;
  readonly base?: () => ClassType<unknown>;
  /** Direct and inherited interfaces, deduplicated, in declaration order. */
  readonly interfaces: ReadonlyArray<() => InterfaceType<unknown>>;
  /** The open definition's type parameters. */
  readonly typeParameters: ReadonlyArray<string>;
  /** The type arguments of a specialization. */
  readonly typeArguments?: ReadonlyArray<RuntimeType>;
  readonly isAbstract: boolean;
  /** A C# struct lowered to a class. */
  readonly isValueType: boolean;
  /** The public parameterless constructor (`new T()`). */
  readonly create?: () => T;
}

export interface InterfaceType<T> extends RuntimeTypeBase<T> {
  readonly kind: "interface";
  readonly baseInterfaces: ReadonlyArray<() => InterfaceType<unknown>>;
  /** The `[PlainObject]` implementers, in a deterministic order. */
  readonly plainImplementers: ReadonlyArray<() => PlainObjectType<unknown>>;
}

export interface PlainObjectType<T> extends RuntimeTypeBase<T> {
  readonly kind: "plainObject";
  readonly guard: (value: unknown) => value is T;
}

export interface PrimitiveType<T> extends RuntimeTypeBase<T> {
  readonly kind: "primitive";
  readonly primitive: PrimitiveName;
}

export interface EnumType<T> extends RuntimeTypeBase<T> {
  readonly kind: "enum" | "stringEnum";
  readonly values: ReadonlyArray<T>;
  /** Parallel to {@link values}. */
  readonly names: ReadonlyArray<string>;
  readonly isFlags: boolean;
}

/** A native array; the element type is erased. */
export interface ArrayType extends RuntimeTypeBase<unknown[]> {
  readonly kind: "array";
}

export interface CollectionType<T> extends RuntimeTypeBase<T> {
  readonly kind: "collection";
  readonly collection:
    | "List"
    | "Dictionary"
    | "HashSet"
    | "Queue"
    | "Stack"
    | "ImmutableList"
    | "ReadOnlyList"
    | "ImmutableHashSet"
    | "ImmutableDictionary"
    | "SortedSet"
    | "SortedDictionary"
    | "SortedList"
    | "ConcurrentDictionary"
    | "IImmutableList"
    | "IImmutableSet"
    | "IImmutableDictionary"
    | "IList"
    | "ICollection"
    | "IReadOnlyList"
    | "IReadOnlyCollection"
    | "IDictionary"
    | "IReadOnlyDictionary"
    | "ISet"
    | "IReadOnlySet";
  /** Classes only. */
  readonly ctor?: abstract new (
    ...args: any[]
  ) => T;
  /** `UnknownType` is the wildcard. */
  readonly typeArguments: ReadonlyArray<RuntimeType>;
  readonly variance: ReadonlyArray<"invariant" | "covariant">;
}

export interface NullableType<T> extends RuntimeTypeBase<T | null> {
  readonly kind: "nullable";
  readonly inner: () => RuntimeType<T>;
}

export interface UnionType<T> extends RuntimeTypeBase<T> {
  readonly kind: "union";
  readonly cases: ReadonlyArray<() => RuntimeType>;
}

export interface DelegateType extends RuntimeTypeBase<Function> {
  readonly kind: "delegate";
}

/** The wildcard: its test always passes. */
export interface UnknownType extends RuntimeTypeBase<unknown> {
  readonly kind: "unknown";
}

/** `System.Object`: its test is `value != null`. */
export interface ObjectType extends RuntimeTypeBase<object> {
  readonly kind: "object";
}

export type RuntimeType<T = unknown> =
  | ClassType<T>
  | InterfaceType<T>
  | PlainObjectType<T>
  | PrimitiveType<T>
  | EnumType<T>
  | ArrayType
  | CollectionType<T>
  | NullableType<any>
  | UnionType<T>
  | DelegateType
  | UnknownType
  | ObjectType;
