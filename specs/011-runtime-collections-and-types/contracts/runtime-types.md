# Contract: `metano-runtime/system/reflection` and `metano-runtime/system/exceptions/*`

**Normative for**: FR-013–FR-029, FR-035, FR-036, NFR-003, NFR-006–NFR-009. Every emitted descriptor is a **typed data literal** (inside a lazy getter for classes, an exported const for everything else) that imports nothing from this module; no `new Function`, no `eval`, no module-scope side effect, no computed-symbol class key, no factory call at module scope. Thunks (`() => …`) break cycles and cross-module references. The memoized factories below (`nullableType`, `specialize`, `withImplementers`, the collection factories) are called only inside functions and getters. The shapes below are the ones research R13 measured as dropped by bun, esbuild, Rollup and webpack when unused, and as pulling no runtime code when used.

## `RuntimeType`

```ts
export interface RuntimeTypeBase<T> {
  readonly kind: RuntimeTypeKind;
  readonly name: string;        // C# simple name; a specialization renders "List<String>"
  readonly fullName: string;    // C# full name with namespace; type arguments by fullName
  readonly namespace: string;
  test(value: unknown): value is T;          // self-contained, duck-typed; isType delegates to it
  default?(): T;                             // primitives: 0, false, "", 0n; struct classes: new T(); absent → null
  // assignability is NOT a method: isAssignableFrom(target, source) in this module reads base / baseInterfaces / interfaces / typeArguments
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
export type ArrayType = RuntimeTypeBase<unknown[]> & { readonly kind: "array"; };              // erased element
export type CollectionType<T> = RuntimeTypeBase<T> & {
  readonly kind: "collection";
  readonly collection: "List" | "Dictionary" | "HashSet" | "Queue" | "Stack" | "ImmutableList" | "ReadOnlyList"
    | "IList" | "ICollection" | "IReadOnlyList" | "IReadOnlyCollection" | "IDictionary" | "IReadOnlyDictionary" | "ISet" | "IReadOnlySet";
  readonly ctor?: abstract new (...args: any[]) => T;                   // classes only
  readonly typeArguments: ReadonlyArray<RuntimeType>;                   // UnknownType = wildcard
  readonly variance: ReadonlyArray<"invariant" | "covariant">;          // identity vs assignability per argument (FR-008)
};
export type NullableType<T> = RuntimeTypeBase<T | null> & { readonly kind: "nullable"; readonly inner: () => RuntimeType<T>; };
export type UnionType<T> = RuntimeTypeBase<T> & { readonly kind: "union"; readonly cases: ReadonlyArray<() => RuntimeType>; }; // spec 010
export type DelegateType = RuntimeTypeBase<Function> & { readonly kind: "delegate"; };       // test: typeof function
export type UnknownType = RuntimeTypeBase<unknown> & { readonly kind: "unknown"; };            // wildcard; test always true
export type ObjectType = RuntimeTypeBase<object> & { readonly kind: "object"; };               // System.Object; test: v != null
export type RuntimeType<T = unknown> = ClassType<T> | InterfaceType<T> | PlainObjectType<T> | PrimitiveType<T> | EnumType<T>
  | ArrayType | CollectionType<T> | NullableType<any> | UnionType<T> | DelegateType | UnknownType | ObjectType;
```

`PrimitiveName` = `"Boolean" | "Byte" | "SByte" | "Int16" | "UInt16" | "Int32" | "UInt32" | "Int64" | "UInt64" | "Single" | "Double" | "Decimal" | "BigInteger" | "Char" | "String" | "Guid" | "DateTime" | "DateTimeOffset" | "DateOnly" | "TimeOnly" | "TimeSpan"`.

### Exported constants (plain literals, one per export, no factory call)

`BooleanType`, `ByteType`, …, `Int32Type`, `DoubleType`, `StringType`, `GuidType`, `DateTimeType`, …, `TimeSpanType`; `UnknownType`, `ObjectType`, `ArrayType`, `DelegateType`. The `Type` suffix keeps them from shadowing `String`, `Boolean`, `Symbol` and `Array`. Each is `export const Int32Type: PrimitiveType<number> = { kind: "primitive", name: "Int32", fullName: "System.Int32", namespace: "System", primitive: "Int32", test: isInt32, is: …, default: () => 0 };` (a literal referencing the `type-checking` helper; measured ≤ 0.2 KB gz with `isInt32`). Temporal constants test by tag: `test: (v) => Object.prototype.toString.call(v) === "[object Temporal.PlainDate]"` (the `Symbol.toStringTag` the polyfill and the native API expose); no `instanceof Temporal.*` anywhere in the runtime (the polyfill alone is 43–47 KB gz, research R13).

### Factories (memoized; never called at module scope)

There is **no** `classType` / `interfaceType` / `plainObjectType` / `enumType` / `unionType`: the compiler writes those descriptors out as literals (the `kind`, `name`, `fullName`, `namespace`, the thunks and a `test` closure), so a generated module that declares a descriptor imports nothing from `system/reflection`. Measured: one thrown exception costs 0.35 KB gz with a literal getter and 0.58 KB with a factory call in the getter (research R13); the ≤ 0.5 KB budget of NFR-008 holds only with the literal.

```ts
export function nullableType<T>(inner: RuntimeType<T>): NullableType<T>;         // memoized on inner
export function specialize<T>(open: ClassType<T>, args: ReadonlyArray<RuntimeType>): ClassType<T>; // memoized on (open, ...args)
export function withImplementers<T>(iface: InterfaceType<T>, implementers: ReadonlyArray<PlainObjectType<unknown>>): InterfaceType<T>; // memoized; FR-019 cross-package
```

The collection factories (`listType`, `dictionaryType`, `hashSetType`, `queueType`, `stackType`, `immutableListType`, `readOnlyListType`, and the interface ones `iListType`, `iReadOnlyListType`, …) live in **`system/collections/descriptors.ts`**, next to the classes they name, so `reflection` never imports `collections` and the dependency graph stays acyclic (data-model.md § 4). Memoization: a `Map` per factory keyed by the tuple of argument identities (nested `Map`s), so `listType(StringType) === listType(StringType)` and `Type ==` is `===`.

### Operations

```ts
export const isType = <T>(value: unknown, type: RuntimeType<T>): value is T => type.test(value);  // no dispatch on kind
export function isAssignableFrom(target: RuntimeType, source: RuntimeType): boolean;  // Type.IsAssignableFrom; reads the data
export function asType<T>(value: unknown, type: RuntimeType<T>): T | null;   // FR-026
export function castTo<T>(value: unknown, type: RuntimeType<T>): T;          // FR-026; InvalidCastException
export function getType(value: unknown): RuntimeType;                       // FR-018
export function tryFilter(filter: () => boolean): boolean;                  // FR-015: false when the filter throws
```

`getType`, in order: `null` / `undefined` → throws `NullReferenceException`; `typeof === "number"` → `isInt32(v) ? Int32Type : DoubleType`; `"string"` → `StringType`; `"boolean"` → `BooleanType`; `"bigint"` → `BigIntegerType`; `"function"` → `DelegateType`; `Array.isArray` → `ArrayType`; `Object.hasOwn(value.constructor, "$runtimeType")` → that getter's value (a runtime collection's or reified class's getter specializes from the **most-derived** `$typeArguments$<Class>` slot when present); prototype `Object.prototype` / `null` → `ObjectType`; any other constructor → a memoized anonymous `ClassType` named `constructor.name` (`WeakMap`).

Each descriptor's `test` is self-contained: `class` → `v instanceof ctor` (plus pairwise type-argument comparison for a specialization of a reified class); `interface` → walk `v.constructor` (own `$runtimeType` only) and its `base` thunks comparing `interfaces` entries by identity, then (plain object only) the `plainImplementers` guards; `plainObject` → `guard`; `primitive` → the validating helper; `collection` → `instanceof ctor` (or, for an interface kind, `instanceof` any implementing class) and per-argument identity / assignability by `variance` with an absent `$typeArguments$<Class>` or `UnknownType` as wildcard; `array` → `asArray(v) !== null` (`asArray` returns the backing array of a fixed-size adopted `List`, else the array itself, else `null`); `nullable` → `v === null || inner().test(v)`; `union` → OR of cases; `delegate` → `typeof === "function"`; `unknown` → `true`; `object` → `v != null`.

## Emission shapes

```ts
// class (reached): string-keyed lazy getter returning a DATA LITERAL, memoized in a module-level let; no reflection import
import type { ClassType } from "metano-runtime/system/reflection";
let Circle$rt: ClassType<Circle> | undefined;
export class Circle extends Shape implements IHasArea {
  static get $runtimeType(): ClassType<Circle> {
    return (Circle$rt ??= {
      kind: "class", name: "Circle", fullName: "Geometry.Circle", namespace: "Geometry", ctor: Circle,
      base: () => Shape.$runtimeType, interfaces: [() => IHasArea$runtimeType], typeParameters: [], isAbstract: false, isValueType: false,
      create: () => new Circle(),           // only with a public parameterless ctor
      test: (v: unknown): v is Circle => v instanceof Circle,
    });
  }
  …
}
// interface (reached): a separate exported literal in the interface's file; no annotation needed; the ONE descriptor that imports a reflection function
export interface IHasArea { readonly area: number; }
export const IHasArea$runtimeType: InterfaceType<IHasArea> = {
  kind: "interface", name: "IHasArea", fullName: "Geometry.IHasArea", namespace: "Geometry", baseInterfaces: [], plainImplementers: [() => AreaDto$runtimeType],
  test: (v: unknown): v is IHasArea => implementsInterface(v, IHasArea$runtimeType),   // the one reflection import an interface descriptor needs
};
// plain object (reached)
export interface AreaDto { area: number; }
export function isAreaDto(value: unknown): value is AreaDto { … }       // synthesized guard (TypeGuardBuilder)
export const AreaDto$runtimeType: PlainObjectType<AreaDto> = { kind: "plainObject", name: "AreaDto", fullName: "Geometry.AreaDto", namespace: "Geometry", guard: isAreaDto, test: isAreaDto };
// numeric enum (FR-044) + descriptor when reached
export const Color = { Red: 0, Green: 1 } as const;
export type Color = (typeof Color)[keyof typeof Color];
export const Color$runtimeType: EnumType<Color> = { kind: "enum", name: "Color", fullName: "Geometry.Color", namespace: "Geometry", values: [0, 1], test: (v): v is Color => v === 0 || v === 1 };
// [Flags] numeric enum (FR-044)
export const Perms = { Read: 1, Write: 2, Execute: 4 } as const;
export type Perms = number;
// [StrictUnionGuard] base (FR-029): the strict guard lives in ITS OWN module, shape.guard.ts; shape.ts never imports a variant
// shape.guard.ts
import { Shape } from "./shape";
import { isCircleDto } from "./circle-dto";   // variant modules run `class CircleDto extends Shape` at load
import { isSquareDto } from "./square-dto";
const variants = [() => isCircleDto, () => isSquareDto];
export function isShape(value: unknown): value is Shape { … /* instanceof fast path, discriminator match, then variants[i]()(value) */ }
// shape.ts: the base, its descriptor getter (no variant data) — importing it first, unbundled, must not throw
// reified method
export function isA<T>(o: unknown, T$type: RuntimeType<T>): boolean { return isType(o, T$type); }
isA(x, Circle.$runtimeType); isA(5, Int32Type); isA(xs, listType(StringType));
// reified class: one non-enumerable string-keyed slot PER DECLARING CLASS, each defined exactly once
export class Box<T> {
  declare readonly $typeArguments$Box: readonly [RuntimeType<T>];
  constructor(value: T, $typeArgs: readonly [RuntimeType<T>]) {
    Object.defineProperty(this, "$typeArguments$Box", { value: $typeArgs, enumerable: false });
    this.value = value;
  }
  holds(o: unknown): boolean { return isType(o, this.$typeArguments$Box[0]); }
  static accepts<T>(o: unknown, T$type: RuntimeType<T>): boolean { return isType(o, T$type); }   // statics get their own parameter
}
new Box(1, [Int32Type]);
export class IntBox extends Box<number> { constructor(v: number) { super(v, [Int32Type]); } }            // closes T: no slot of its own
export class Pair<U> extends Box<List<U>> {
  declare readonly $typeArguments$Pair: readonly [RuntimeType<U>];
  constructor(v: List<U>, $typeArgs: readonly [RuntimeType<U>]) {
    super(v, [listType($typeArgs[0])]);                                                    // Box's slot, defined by Box's ctor
    Object.defineProperty(this, "$typeArguments$Pair", { value: $typeArgs, enumerable: false }); // Pair's slot, defined once here
  }
}
```

A base ctor and a derived ctor never define the same slot, so no `TypeError` from a second `defineProperty`, and `Box<T>`'s and `Pair<U>`'s arguments coexist. `getType` specializes from the most-derived slot (`$typeArguments$Pair` for a `Pair`, `$typeArguments$Box` for an `IntBox`, which inherits Box's slot through its `super` call). `copyRecordWith` (`record-copy.ts`) copies every own property named `$typeArguments$*` explicitly after `Object.assign` (which copies enumerable own properties only), so `with` keeps them.

## Exceptions (`metano-runtime/system/exceptions/*`)

One module per class; `index.ts` re-exports them; `to-exception.ts` holds `toException`. Each class has the lazy `$runtimeType` getter of FR-018 returning a **literal** (never an eager static, never a factory call: an exception module imports nothing from `system/reflection` beyond types), with `base` thunks forming the hierarchy so `isAssignableFrom` works through `base` while `catch` / `is` use `instanceof` and never touch the descriptor.

```ts
export class Exception extends Error {
  constructor(message?: string | null, innerException?: Error | null);
  readonly innerException: Exception | Error | null;   // also set as `cause`
  get stackTrace(): string | null;                      // `stack`
  readonly name: string;                                // class name, set by every subclass
  toString(): string;                                   // "<fullName>: <message>" (+ " ---> <inner>" as .NET)
  static get $runtimeType(): ClassType<Exception>;
}
export class SystemException extends Exception {}
export class InvalidOperationException extends SystemException {}
export class ArgumentException extends SystemException { constructor(message?, paramName?, innerException?); readonly paramName: string | null; }
export class ArgumentNullException extends ArgumentException { constructor(paramName?, message?); }
export class ArgumentOutOfRangeException extends ArgumentException { constructor(paramName?, actualValue?, message?); readonly actualValue: unknown; }
export class KeyNotFoundException extends SystemException {}
export class IndexOutOfRangeException extends SystemException {}
export class FormatException extends SystemException {}
export class ArithmeticException extends SystemException {}
export class OverflowException extends ArithmeticException {}
export class DivideByZeroException extends ArithmeticException {}
export class NotSupportedException extends SystemException {}   // spec 009 moves here
export class NotImplementedException extends SystemException {}
export class InvalidCastException extends SystemException {}
export class NullReferenceException extends SystemException {}
export class MissingMethodException extends SystemException {}
export class ObjectDisposedException extends InvalidOperationException {}
export class OperationCanceledException extends SystemException {}
export class TaskCanceledException extends OperationCanceledException {}
export class JsonException extends Exception {}                 // spec 009 moves here (System.Text.Json.JsonException : Exception)
export function toException(value: unknown): Exception;         // FR-015 catch-all normalisation (DOMException "AbortError" → TaskCanceledException)
```

Default messages are those of `IrExceptionConstructionExtractor.DefaultMessages` (e.g. `Operation is not valid due to the current state of the object.`, `Value cannot be null.`, `Specified argument was out of the range of valid values.`); the `(Parameter 'x')` suffix is appended as .NET does. **Budget** (NFR-008): one class with its literal getter ≤ 0.5 KB gz (research R13 measured 0.35 KB for the literal shape on a probe; the real class is re-measured by FR-043); an unused class is 0 bytes.

## Typed catch (FR-015)

```ts
try { … }
catch (e$) {
  if (e$ instanceof ArgumentNullException) { const e = e$; … }
  else if (e$ instanceof ArgumentException && tryFilter(() => e$.message.length > 3)) { const e = e$; … }   // when filter
  else { const e = toException(e$); … }                                                                      // catch (Exception e) / catch
}
// catch (Exception e) when (f(e)) is an ordinary arm, not a catch-all:
catch (e$) {
  const e0 = toException(e$);
  if (tryFilter(() => isTransient(e0))) { const e = e0; … }
  else if (e$ instanceof FormatException) { … }
  else throw e$;
}
// throw; inside an arm → throw e$;
```

Dart renders the same chain with `is` tests inside one `catch (e$)` (FR-038).

## JSON link (FR-028)

```ts
interface TypeSpec<T> { …; readonly runtimeType?: () => RuntimeType<T>; }   // additive
type ListDescriptor = { kind: "list"; element: TypeDescriptor };            // + dictionary, queue, stack, immutableList, readOnlyList
type DictionaryDescriptor = { kind: "dictionary"; key: TypeDescriptor; value: TypeDescriptor }; // replaces "map"
```
