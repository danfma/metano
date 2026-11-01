# Contract: `metano-runtime/system/reflection` and `metano-runtime/system/exceptions`

**Normative for**: FR-013–FR-028, FR-040, FR-041, NFR-003. All descriptors are typed object literals built by factories; no `new Function`, no `eval`. Thunks (`() => …`) break cycles and cross-module references. Factories that take descriptors are memoized on argument identity.

## Symbols

```ts
export const runtimeType: unique symbol = Symbol.for("metano-runtime/runtimeType");   // static on classes (OD-5)
export const typeArguments: unique symbol = Symbol.for("metano-runtime/typeArguments"); // hidden field on collections and reified classes
```

## `RuntimeType`

```ts
export interface RuntimeTypeBase<T> {
  readonly kind: RuntimeTypeKind;
  readonly name: string;        // C# simple name, e.g. "Circle", "List`1" rendered as "List<String>" for specializations
  readonly fullName: string;    // C# full name with namespace; type arguments rendered with their fullName
  readonly namespace: string;
  test(value: unknown): value is T;          // the runtime test of FR-021
  is(other: RuntimeType): boolean;           // assignability (Type.IsAssignableFrom)
  readonly defaultValue?: T;                 // primitives: 0, false, "", 0n; others undefined → null
}
export type ClassType<T> = RuntimeTypeBase<T> & {
  readonly kind: "class";
  readonly ctor: abstract new (...args: any[]) => T;
  readonly base?: () => ClassType<unknown>;
  readonly interfaces: ReadonlyArray<() => InterfaceType<unknown>>;   // direct + inherited, deduped, declaration order
  readonly typeParameters: ReadonlyArray<string>;                       // open definition only
  readonly typeArguments?: ReadonlyArray<RuntimeType>;                  // specialization only
  readonly isAbstract: boolean;
  readonly isValueType: boolean;                                        // C# struct lowered to a class
  readonly create?: () => T;                                            // public parameterless ctor (new T())
};
export type InterfaceType<T> = RuntimeTypeBase<T> & {
  readonly kind: "interface";
  readonly baseInterfaces: ReadonlyArray<() => InterfaceType<unknown>>;
  readonly plainImplementers: ReadonlyArray<() => PlainObjectType<unknown>>; // OD-8, deterministic order
};
export type PlainObjectType<T> = RuntimeTypeBase<T> & { readonly kind: "plainObject"; readonly guard: (v: unknown) => v is T; };
export type PrimitiveType<T> = RuntimeTypeBase<T> & { readonly kind: "primitive"; readonly primitive: PrimitiveName; };
export type EnumType<T> = RuntimeTypeBase<T> & { readonly kind: "enum" | "stringEnum"; readonly values: ReadonlyArray<T>; };
export type ArrayType<T> = RuntimeTypeBase<T[]> & { readonly kind: "array"; };                 // erased element
export type CollectionType<T> = RuntimeTypeBase<T> & {
  readonly kind: "collection";
  readonly collection: "List" | "Dictionary" | "HashSet" | "Queue" | "Stack" | "ImmutableList" | "ReadOnlyList";
  readonly ctor: abstract new (...args: any[]) => T;
  readonly typeArguments: ReadonlyArray<RuntimeType>;                   // Unknown = wildcard
};
export type NullableType<T> = RuntimeTypeBase<T | null> & { readonly kind: "nullable"; readonly inner: () => RuntimeType<T>; };
export type UnionType<T> = RuntimeTypeBase<T> & { readonly kind: "union"; readonly cases: ReadonlyArray<() => RuntimeType>; }; // spec 010
export type DelegateType = RuntimeTypeBase<Function> & { readonly kind: "delegate"; };       // test: typeof function
export type UnknownType = RuntimeTypeBase<unknown> & { readonly kind: "unknown"; };            // wildcard; test always true
export type ObjectType = RuntimeTypeBase<object> & { readonly kind: "object"; };               // System.Object; test: v != null
export type RuntimeType<T = unknown> = ClassType<T> | InterfaceType<T> | PlainObjectType<T> | PrimitiveType<T> | EnumType<T>
  | ArrayType<any> | CollectionType<T> | NullableType<any> | UnionType<T> | DelegateType | UnknownType | ObjectType;
```

`PrimitiveName` = `"Boolean" | "Byte" | "SByte" | "Int16" | "UInt16" | "Int32" | "UInt32" | "Int64" | "UInt64" | "Single" | "Double" | "Decimal" | "BigInteger" | "Char" | "String" | "Guid" | "DateTime" | "DateTimeOffset" | "DateOnly" | "TimeOnly" | "TimeSpan"`. Exported constants: `Boolean`, `Int32`, `Double`, `String`, … (one per name), `Unknown`, `Object`, `Array` (erased), `Delegate`.

### Factories (memoized where they take descriptors)

```ts
export function classType<T>(spec: ClassTypeSpec<T>): ClassType<T>;              // emitted once per class, as the static
export function interfaceType<T>(spec: InterfaceTypeSpec<T>): InterfaceType<T>;
export function plainObjectType<T>(spec: { name, fullName, namespace, guard }): PlainObjectType<T>;
export function enumType<T>(spec): EnumType<T>;
export function nullableType<T>(inner: RuntimeType<T>): NullableType<T>;         // memoized on inner
export function specialize<T>(open: ClassType<T>, args: ReadonlyArray<RuntimeType>): ClassType<T>; // memoized on (open, ...args)
export function listType<T>(elem: RuntimeType<T>): CollectionType<List<T>>;      // memoized; also dictionaryType, hashSetType, queueType, stackType, immutableListType, readOnlyListType
export function unionType<T>(spec): UnionType<T>;
```

Memoization: a `Map` per factory keyed by the tuple of argument identities (nested `Map`s), so `listType(String) === listType(String)` and `Type ==` is `===`.

### Operations

```ts
export function getType(value: unknown): RuntimeType;                       // FR-018 (constructor[runtimeType] → fallbacks)
export function isType<T>(value: unknown, type: RuntimeType<T>): value is T; // FR-021 generic dispatch (used for Reified / Interface / Collection)
export function asType<T>(value: unknown, type: RuntimeType<T>): T | null;   // FR-026
export function castTo<T>(value: unknown, type: RuntimeType<T>): T;          // FR-026; InvalidCastException
export function implementsInterface(value: unknown, iface: InterfaceType<unknown>): boolean; // nominal walk + plainImplementers
```

`getType` fallbacks, in order: `null` / `undefined` → throws `NullReferenceException` (as `GetType()` on `null` does in C#); `typeof === "number"` → `Double`; `"string"` → `String`; `"boolean"` → `Boolean`; `"bigint"` → `BigInteger`; `"function"` → `Delegate`; `Array.isArray` → `Array`; `value.constructor?.[runtimeType]` → that descriptor (collections specialize it from `value[typeArguments]`); prototype `Object.prototype` / `null` → `Object`; any other constructor → a memoized anonymous `ClassType` named `constructor.name` (`WeakMap`).

`isType` dispatch on `type.kind`: `class` → `instanceof type.ctor` (plus type-argument comparison for a specialization of a reified class); `interface` → `implementsInterface`; `plainObject` → `guard`; `primitive` → the validating helper (`isInt32`, …); `collection` → `instanceof ctor` and pairwise `typeArguments` identity with `Unknown` as wildcard; `array` → `Array.isArray`; `nullable` → `value === null || inner().test(value)`; `union` → OR of cases; `delegate` → `typeof === "function"`; `unknown` → `true`; `object` → `value != null`.

## Emission shapes

```ts
// class (reached)
import { runtimeType, classType, type ClassType } from "metano-runtime/system/reflection";
export class Circle extends Shape implements IHasArea {
  static readonly [runtimeType]: ClassType<Circle> = classType({
    name: "Circle", fullName: "Geometry.Circle", namespace: "Geometry", ctor: Circle,
    base: () => Shape[runtimeType], interfaces: [() => IHasArea.$type], isAbstract: false, isValueType: false,
    create: () => new Circle(),           // only with a public parameterless ctor
  });
  …
}
// interface (reached)
export interface IHasArea { readonly area: number; }
export namespace IHasArea {
  export const $type: InterfaceType<IHasArea> = interfaceType({ name: "IHasArea", fullName: "Geometry.IHasArea", namespace: "Geometry", baseInterfaces: [], plainImplementers: [() => AreaDto.$type] });
}
// plain object (reached)
export interface AreaDto { area: number; }
export function isAreaDto(value: unknown): value is AreaDto { … }       // synthesized guard (TypeGuardBuilder)
export namespace AreaDto { export const $type = plainObjectType<AreaDto>({ name: "AreaDto", fullName: "Geometry.AreaDto", namespace: "Geometry", guard: isAreaDto }); }
// reified method
export function isA<T>(o: unknown, T$type: RuntimeType<T>): boolean { return isType(o, T$type); }
isA(x, Circle[runtimeType]); isA(5, Int32); isA(xs, listType(String));
// reified class
export class Box<T> {
  readonly [typeArguments]: readonly [RuntimeType<T>];
  constructor(value: T, $typeArgs: readonly [RuntimeType<T>]) { this.value = value; this[typeArguments] = $typeArgs; }
  holds(o: unknown): boolean { return isType(o, this[typeArguments][0]); }
}
new Box(1, [Int32]);
```

The `[typeArguments]` field is defined non-enumerable (`Object.defineProperty` in the runtime's `defineTypeArguments` helper) so it never reaches `JSON.stringify` or structural `equals`.

## Exceptions (`metano-runtime/system/exceptions`)

```ts
export class Exception extends Error {
  constructor(message?: string | null, innerException?: Error | null);
  readonly innerException: Exception | Error | null;   // also set as `cause`
  get stackTrace(): string | null;                      // `stack`
  readonly name: string;                                // class name, set by every subclass
  toString(): string;                                   // "<fullName>: <message>" (+ " ---> <inner>" as .NET)
  static readonly [runtimeType]: ClassType<Exception>;
}
export class SystemException extends Exception {}
export class InvalidOperationException extends SystemException {}
export class ArgumentException extends SystemException { constructor(message?, paramName?, innerException?); readonly paramName: string | null; }
export class ArgumentNullException extends ArgumentException { constructor(paramName?, message?); }
export class ArgumentOutOfRangeException extends ArgumentException { constructor(paramName?, actualValue?, message?); readonly actualValue: unknown; }
export class KeyNotFoundException extends SystemException {}
export class IndexOutOfRangeException extends SystemException {}
export class FormatException extends SystemException {}
export class OverflowException extends SystemException {}        // ArithmeticException collapsed
export class DivideByZeroException extends SystemException {}
export class NotSupportedException extends SystemException {}   // spec 009 moves here
export class NotImplementedException extends SystemException {}
export class InvalidCastException extends SystemException {}
export class NullReferenceException extends SystemException {}
export class MissingMethodException extends SystemException {}
export class ObjectDisposedException extends InvalidOperationException {}
export class JsonException extends Exception {}                 // spec 009 moves here (System.Text.Json.JsonException : Exception)
export function toException(value: unknown): Exception;         // FR-015 catch-all normalisation
```

Default messages are those of `IrExceptionConstructionExtractor.DefaultMessages` (e.g. `Operation is not valid due to the current state of the object.`, `Value cannot be null.`, `Specified argument was out of the range of valid values.`); the `(Parameter 'x')` suffix is appended as .NET does. Each class is `reached` by definition (FR-013) and carries its descriptor, with `base` thunks forming the hierarchy so `is SystemException` works through `instanceof` and `IsAssignableFrom` through `base`.

## Typed catch (FR-015)

```ts
try { … }
catch (e$) {
  if (e$ instanceof ArgumentNullException) { const e = e$; … }
  else if (e$ instanceof ArgumentException && count > 3) { const e = e$; … }   // when filter
  else { const e = toException(e$); … }                                         // catch (Exception e) / catch
}
// without a catch-all:
catch (e$) { if (e$ instanceof FormatException) { … } else throw e$; }
// throw; inside an arm → throw e$;
```

## JSON link (FR-028)

```ts
interface TypeSpec<T> { …; readonly runtimeType?: () => RuntimeType<T>; }   // additive
type ListDescriptor = { kind: "list"; element: TypeDescriptor };            // + dictionary, queue, stack, immutableList, readOnlyList
type DictionaryDescriptor = { kind: "dictionary"; key: TypeDescriptor; value: TypeDescriptor }; // replaces "map"
```
