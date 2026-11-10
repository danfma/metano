// Compile-time checks of the null-root signatures (FR-034, FR-037), run by
// `test/typecheck-fixtures.test.ts` through `tsgo -p tsconfig.typecheck.json`.
// Each `@ts-expect-error` must flag an error, or tsgo fails on it as unused.
import { JsonSerializer, SerializerContext, type TypeSpec, type ValueSpec } from "#/system/json";

import { type Equal, expectType } from "../../../typecheck/type-assertions.ts";

class Order {
  constructor(readonly id: string) {}
}

class SPoint {
  constructor(readonly x: number) {}
}

declare const orderSpec: TypeSpec<Order>;
declare const pointSpec: TypeSpec<SPoint>;
declare const int32Spec: ValueSpec<number>;
declare const orderListSpec: ValueSpec<Order[] | null>;
declare const nullableInt32Spec: ValueSpec<number | null>;
declare const context: SerializerContext;
declare const data: unknown;

const bound = JsonSerializer.withContext(context);

// ─── TypeSpec reads return T | null ───────────────────────────────────────────

const order = JsonSerializer.deserialize(data, orderSpec);
expectType<Equal<typeof order, Order | null>>();

const orderFromText = JsonSerializer.deserializeText("null", orderSpec);
expectType<Equal<typeof orderFromText, Order | null>>();

const boundOrder = bound.deserialize(data, orderSpec);
expectType<Equal<typeof boundOrder, Order | null>>();

const boundOrderFromText = bound.deserializeText("null", orderSpec);
expectType<Equal<typeof boundOrderFromText, Order | null>>();

// A struct contract shares the overload; its runtime check throws for null.
const point = JsonSerializer.deserialize(data, pointSpec);
expectType<Equal<typeof point, SPoint | null>>();

// @ts-expect-error a TypeSpec read may be null under strict
const strictOrder: Order = JsonSerializer.deserialize(data, orderSpec);

// @ts-expect-error a TypeSpec text read may be null under strict
const strictTextOrder: Order = JsonSerializer.deserializeText("{}", orderSpec);

// @ts-expect-error a bound TypeSpec read may be null under strict
const strictBoundOrder: Order = bound.deserialize(data, orderSpec);

// @ts-expect-error a bound TypeSpec text read may be null under strict
const strictBoundTextOrder: Order = bound.deserializeText("{}", orderSpec);

// The migration: handle the null or assert it.
const assertedOrder: Order = JsonSerializer.deserialize(data, orderSpec)!;

// ─── ValueSpec reads return T, which holds | null for a reference type ───────

const int32 = JsonSerializer.deserialize(data, int32Spec);
expectType<Equal<typeof int32, number>>();

const int32FromText = bound.deserializeText("1", int32Spec);
expectType<Equal<typeof int32FromText, number>>();

const orders = JsonSerializer.deserializeText("[]", orderListSpec);
expectType<Equal<typeof orders, Order[] | null>>();

const nullableInt32 = bound.deserialize(data, nullableInt32Spec);
expectType<Equal<typeof nullableInt32, number | null>>();

// @ts-expect-error a reference-type value root may be null under strict
const strictOrders: Order[] = JsonSerializer.deserialize(data, orderListSpec);

// ─── TypeSpec writes: non-null in, record out; null in, null out ──────────────

class Derived extends Order {
  constructor(
    id: string,
    readonly extra: number,
  ) {
    super(id);
  }
}

interface Shape {
  readonly kind: string;
}

declare const maybeOrder: Order | null;
declare const optionalOrder: Order | undefined;
declare const anyOrder: any;
declare const shape: Shape;
declare const shapeSpec: TypeSpec<Shape>;

const written = JsonSerializer.serialize(new Order("a"), orderSpec);
expectType<Equal<typeof written, Record<string, unknown>>>();

const writtenDerived = JsonSerializer.serialize(new Derived("a", 1), orderSpec);
expectType<Equal<typeof writtenDerived, Record<string, unknown>>>();

const writtenShape = bound.serialize(shape, shapeSpec);
expectType<Equal<typeof writtenShape, Record<string, unknown>>>();

const writtenAny = JsonSerializer.serialize(anyOrder, orderSpec);
// `any` may be `null`, so it takes the nullable overload.
expectType<Equal<typeof writtenAny, Record<string, unknown> | null>>();

const writtenNull = JsonSerializer.serialize(null, orderSpec);
expectType<Equal<typeof writtenNull, Record<string, unknown> | null>>();

const writtenMaybe = bound.serialize(maybeOrder, orderSpec);
expectType<Equal<typeof writtenMaybe, Record<string, unknown> | null>>();

const writtenOptional = JsonSerializer.serialize(optionalOrder, orderSpec);
expectType<Equal<typeof writtenOptional, Record<string, unknown> | null>>();

function writeGenericOrder<U extends Order>(value: U, spec: TypeSpec<U>): Record<string, unknown> {
  return JsonSerializer.serialize(value, spec);
}

// @ts-expect-error a value of another type is rejected
JsonSerializer.serialize("order", orderSpec);

// @ts-expect-error a write that may be null cannot be read without a check
const strictWritten: Record<string, unknown> = JsonSerializer.serialize(maybeOrder, orderSpec);

// ─── valueType is part of both spec shapes ───────────────────────────────────

const structSpec: TypeSpec<SPoint> = {
  type: SPoint,
  factory: (p) => new SPoint(p.x as number),
  properties: [{ ts: "x", json: "X", type: { kind: "primitive" } }],
  valueType: true,
};

const valueRootSpec: ValueSpec<number> = { descriptor: { kind: "primitive" }, valueType: true };

// Exported so `noUnusedLocals`-style checks never drop the declarations
// above: each one exists only for its inferred type or its expected error.
export {
  assertedOrder,
  strictBoundOrder,
  strictBoundTextOrder,
  strictOrder,
  strictOrders,
  strictTextOrder,
  strictWritten,
  structSpec,
  valueRootSpec,
  writeGenericOrder,
};
