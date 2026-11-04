/**
 * The benchmark schema: an `Order` with strings, numbers, a nullable,
 * `PlainDate`, `Decimal`, nested objects and arrays, written as the specs the
 * transpiler generates (the scratch harness of #456, ported unchanged).
 * `Decimal` is decimal.js, a devDependency, so the writes measure its real
 * `toFixed()` / `toNumber()` cost (#458).
 */
import { Temporal } from "@js-temporal/polyfill";
import { Decimal } from "decimal.js";
import type {
  PrimitiveDescriptor,
  TypeDescriptor,
  TypeSpec,
  ValueSpec,
} from "../../src/system/json/index.ts";

export class Address {
  constructor(
    readonly street: string,
    readonly city: string,
    readonly zip: string,
  ) {}
}

export class Customer {
  constructor(
    readonly name: string,
    readonly email: string,
    readonly phone: string | null,
    readonly address: Address,
  ) {}
}

export class OrderItem {
  constructor(
    readonly sku: string,
    readonly quantity: number,
    readonly unitPrice: Decimal,
  ) {}
}

export class Order {
  constructor(
    readonly id: number,
    readonly code: string,
    readonly active: boolean,
    readonly note: string | null,
    readonly placedOn: Temporal.PlainDate,
    readonly total: Decimal,
    readonly sequence: number,
    readonly customer: Customer,
    readonly items: readonly OrderItem[],
    readonly tags: readonly string[],
  ) {}
}

const primitive: PrimitiveDescriptor = { kind: "primitive" };

const decimal = { kind: "decimal", type: Decimal } as const;

const addressSpec: TypeSpec<Address> = {
  type: Address,
  factory: (p) => new Address(p.street as string, p.city as string, p.zip as string),
  properties: [
    { ts: "street", json: "street", type: primitive },
    { ts: "city", json: "city", type: primitive },
    { ts: "zip", json: "zip", type: primitive },
  ],
};

const customerSpec: TypeSpec<Customer> = {
  type: Customer,
  factory: (p) =>
    new Customer(
      p.name as string,
      p.email as string,
      p.phone as string | null,
      p.address as Address,
    ),
  properties: [
    { ts: "name", json: "name", type: primitive },
    { ts: "email", json: "email", type: primitive },
    {
      ts: "phone",
      json: "phone",
      type: { kind: "nullable", inner: primitive },
      optional: true,
    },
    { ts: "address", json: "address", type: { kind: "ref", spec: () => addressSpec } },
  ],
};

/**
 * The `Order` specs with `price` as the descriptor of `total` and
 * `unitPrice`: `decimal` as generated, or `primitive` for a spec whose
 * closure holds no decimal, which `deserializeText` reads with plain
 * `JSON.parse` (#457).
 */
function ordersSpecOf(price: TypeDescriptor): ValueSpec<Order[]> {
  const orderItemSpec: TypeSpec<OrderItem> = {
    type: OrderItem,
    factory: (p) => new OrderItem(p.sku as string, p.quantity as number, p.unitPrice as Decimal),
    properties: [
      { ts: "sku", json: "sku", type: primitive },
      { ts: "quantity", json: "quantity", type: primitive },
      { ts: "unitPrice", json: "unitPrice", type: price },
    ],
  };

  const orderSpec: TypeSpec<Order> = {
    type: Order,
    factory: (p) =>
      new Order(
        p.id as number,
        p.code as string,
        p.active as boolean,
        p.note as string | null,
        p.placedOn as Temporal.PlainDate,
        p.total as Decimal,
        p.sequence as number,
        p.customer as Customer,
        p.items as OrderItem[],
        p.tags as string[],
      ),
    properties: [
      { ts: "id", json: "id", type: primitive },
      { ts: "code", json: "code", type: primitive },
      { ts: "active", json: "active", type: primitive },
      { ts: "note", json: "note", type: { kind: "nullable", inner: primitive }, optional: true },
      {
        ts: "placedOn",
        json: "placedOn",
        type: { kind: "temporal", parse: (iso) => Temporal.PlainDate.from(iso) },
      },
      { ts: "total", json: "total", type: price },
      { ts: "sequence", json: "sequence", type: primitive },
      { ts: "customer", json: "customer", type: { kind: "ref", spec: () => customerSpec } },
      {
        ts: "items",
        json: "items",
        type: { kind: "array", element: { kind: "ref", spec: () => orderItemSpec } },
      },
      { ts: "tags", json: "tags", type: { kind: "array", element: primitive } },
    ],
  };

  return { descriptor: { kind: "array", element: { kind: "ref", spec: () => orderSpec } } };
}

/** The payload root: a JSON array of orders, a `List<Order>` root in C#. */
export const ordersSpec = ordersSpecOf(decimal);

/** The same payload with `total` and `unitPrice` read as plain numbers. */
export const ordersWithoutDecimalsSpec = ordersSpecOf(primitive);
