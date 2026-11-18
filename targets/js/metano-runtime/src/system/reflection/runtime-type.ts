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
export type ClassType<T> = RuntimeTypeBase<T> & {
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
};

export type InterfaceType<T> = RuntimeTypeBase<T> & {
  readonly kind: "interface";
  readonly baseInterfaces: ReadonlyArray<() => InterfaceType<unknown>>;
  /** The `[PlainObject]` implementers, in a deterministic order. */
  readonly plainImplementers: ReadonlyArray<() => PlainObjectType<unknown>>;
};

export type PlainObjectType<T> = RuntimeTypeBase<T> & {
  readonly kind: "plainObject";
  readonly guard: (value: unknown) => value is T;
};

export type PrimitiveType<T> = RuntimeTypeBase<T> & {
  readonly kind: "primitive";
  readonly primitive: PrimitiveName;
};

export type EnumType<T> = RuntimeTypeBase<T> & {
  readonly kind: "enum" | "stringEnum";
  readonly values: ReadonlyArray<T>;
  /** Parallel to {@link values}. */
  readonly names: ReadonlyArray<string>;
  readonly isFlags: boolean;
};

/** A native array; the element type is erased. */
export type ArrayType = RuntimeTypeBase<unknown[]> & { readonly kind: "array" };

export type CollectionType<T> = RuntimeTypeBase<T> & {
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
};

export type NullableType<T> = RuntimeTypeBase<T | null> & {
  readonly kind: "nullable";
  readonly inner: () => RuntimeType<T>;
};

export type UnionType<T> = RuntimeTypeBase<T> & {
  readonly kind: "union";
  readonly cases: ReadonlyArray<() => RuntimeType>;
};

export type DelegateType = RuntimeTypeBase<Function> & { readonly kind: "delegate" };

/** The wildcard: its test always passes. */
export type UnknownType = RuntimeTypeBase<unknown> & { readonly kind: "unknown" };

/** `System.Object`: its test is `value != null`. */
export type ObjectType = RuntimeTypeBase<object> & { readonly kind: "object" };

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
