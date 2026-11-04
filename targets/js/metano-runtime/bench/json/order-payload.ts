/**
 * Deterministic `Order[]` JSON payloads (the scratch harness generator of
 * #456): the same seed always yields the same text for a size, cached as a
 * file under the OS temp directory so every benchmark process loads it
 * identically.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

/** Payload sizes by label, in bytes. */
export const PAYLOAD_SIZES: Readonly<Record<string, number>> = {
  "1k": 1_000,
  "100k": 100_000,
  "1m": 1_000_000,
  "10m": 10_000_000,
  "100m": 100_000_000,
};

const FIRST_NAMES = ["Ana", "Bruno", "Carla", "Daniel", "Élodie", "Fábio", "Günter", "Hiro"];
const LAST_NAMES = ["Silva", "Souza", "Müller", "Tanaka", "O'Brien", "Smith", "Nguyễn", "Costa"];
const CITIES = ["São Paulo", "Lisboa", "Berlin", "東京", "New York", "Recife", "Zürich"];
const STREETS = ["Rua das Flores", "Main St", "Avenida Paulista", "Hauptstraße", "Baker Street"];
const TAGS = ["gift", "priority", "b2b", "retail", "promo", "backorder", "fragile"];

const NOTES = [
  null,
  null,
  "Leave at the door",
  'Customer said "ring twice"',
  "Line one\nLine two",
  "Tab\tseparated",
  "Back\\slash path C:\\tmp",
  "Entregar após às 18h — obrigado!",
];

/** The path of the payload file of `label`, generated on first use. */
export function payloadFileOf(label: string): string {
  const bytes = PAYLOAD_SIZES[label];

  if (bytes === undefined) {
    throw new Error(`Unknown payload size '${label}'. Known: ${Object.keys(PAYLOAD_SIZES)}.`);
  }

  const directory = join(tmpdir(), "metano-json-bench");
  const path = join(directory, `orders-${label}.json`);

  if (!existsSync(path)) {
    mkdirSync(directory, { recursive: true });
    writeFileSync(path, ordersPayloadOf(bytes));
  }

  return path;
}

/** The payload text of `label`. */
export function payloadTextOf(label: string): string {
  return readFileSync(payloadFileOf(label), "utf8");
}

function ordersPayloadOf(targetBytes: number): string {
  const random = seededRandom(12345);
  const orders: string[] = [];
  let bytes = 2;

  while (bytes < targetBytes) {
    const order = orderJsonOf(orders.length + 1, random);
    bytes += Buffer.byteLength(order) + 1;
    orders.push(order);
  }

  return `[${orders.join(",")}]`;
}

interface SeededRandom {
  readonly int: (low: number, high: number) => number;
  readonly pick: <T>(values: readonly T[]) => T;
  readonly chance: (probability: number) => boolean;
}

function seededRandom(seed: number): SeededRandom {
  let state = seed;

  const next = () => {
    state = (state * 1103515245 + 12345) & 0x7fffffff;
    return state / 0x7fffffff;
  };

  return {
    int: (low, high) => low + Math.floor(next() * (high - low + 1)),
    pick: (values) => values[Math.floor(next() * values.length)]!,
    chance: (probability) => next() < probability,
  };
}

function orderJsonOf(id: number, random: SeededRandom): string {
  const items = Array.from({ length: random.int(1, 5) }, () => orderItemOf(random));
  const total = items.reduce((sum, item) => sum + item.quantity * Number(item.unitPrice), 0);
  const name = `${random.pick(FIRST_NAMES)} ${random.pick(LAST_NAMES)}`;
  const note = random.pick(NOTES);
  const phone = random.chance(0.5)
    ? null
    : `+55 11 9${random.int(1000, 9999)}-${random.int(1000, 9999)}`;
  const tags = Array.from({ length: random.int(0, 3) }, () => random.pick(TAGS));
  const month = String(random.int(1, 12)).padStart(2, "0");
  const day = String(random.int(1, 28)).padStart(2, "0");
  const street = `${random.pick(STREETS)}, ${random.int(1, 9999)}`;

  return (
    `{"id":${id},"code":"ORD-${String(id).padStart(8, "0")}","active":${random.chance(0.8)},` +
    `"note":${JSON.stringify(note)},"placedOn":"20${random.int(10, 26)}-${month}-${day}",` +
    `"total":${total.toFixed(2)},"sequence":${random.int(1, 2 ** 40)},` +
    `"customer":{"name":${JSON.stringify(name)},"email":"user${id}@example.com",` +
    `"phone":${JSON.stringify(phone)},"address":{"street":${JSON.stringify(street)},` +
    `"city":${JSON.stringify(random.pick(CITIES))},` +
    `"zip":"${random.int(10000, 99999)}-${random.int(100, 999)}"}},` +
    `"items":[${items.map((item) => item.json).join(",")}],"tags":${JSON.stringify(tags)}}`
  );
}

interface OrderItemJson {
  readonly quantity: number;
  readonly unitPrice: string;
  readonly json: string;
}

/** Two decimal places, like a `decimal(18,2)` column: exact as a double and as a decimal. */
function orderItemOf(random: SeededRandom): OrderItemJson {
  const quantity = random.int(1, 10);
  const unitPrice = (random.int(100, 999999) / 100).toFixed(2);
  const json = `{"sku":"SKU-${random.int(10000, 99999)}","quantity":${quantity},"unitPrice":${unitPrice}}`;

  return { quantity, unitPrice, json };
}
