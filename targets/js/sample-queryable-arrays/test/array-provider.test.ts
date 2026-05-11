/**
 * Validates the array-backed provider sample. Each test builds a
 * `linq()` chain, reads the descriptor list off the result via the
 * runtime's symbol-keyed `getStages` slot, and hands them to the
 * provider — which materializes the result by reading each operator's
 * `queryable` tree instead of opening the closure.
 *
 * Cross-checks: every materialized result matches what the runtime
 * would have produced with the closures, so the tree shape is a
 * faithful encoding of the lambda body. `fellBack` lists any stage the
 * provider could not introspect — empty for the queryable chains the
 * compiler captures, populated for the IEnumerable baseline.
 */
import { describe, expect, test } from "bun:test";
import { type AnyOperator, getStages, linq, select, where } from "metano-runtime";
import { runArrayProvider } from "#/provider/array-provider";
import { User } from "#/user";
import { UserQueries } from "#/user-queries";

const sample: User[] = [
  new User("Alice", 30, true),
  new User("Bob", 17, true),
  new User("Carol", 42, false),
  new User("Dan", 22, true),
];

/**
 * Reads the stage list off a `linq()` result. Throws when the symbol
 * slot is missing so a regression in the runtime fails the test
 * loudly instead of silently driving the provider with an empty
 * chain.
 */
function stagesFor(query: unknown): AnyOperator[] {
  const stages = getStages(query);
  if (stages === undefined) {
    throw new Error(
      "linq() result is missing the stages slot — runtime contract regression",
    );
  }
  return stages as AnyOperator[];
}

describe("array-provider", () => {
  test("UserQueries.adults — provider materializes via tree", () => {
    const expected = Array.from(UserQueries.adults(sample));
    const query = linq(
      sample,
      where((u: User) => u.age >= 18, {
        tree: {
          kind: "binary",
          op: ">=",
          left: { kind: "member", target: { kind: "param", name: "u" }, member: "age" },
          right: { kind: "literal", value: 18 },
        },
      }),
    );

    const result = runArrayProvider(sample, stagesFor(query));
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("UserQueries.activeAdults — composite boolean tree", () => {
    const expected = Array.from(UserQueries.activeAdults(sample));
    const query = linq(
      sample,
      where((u: User) => u.age >= 18 && u.active, {
        tree: {
          kind: "binary",
          op: "&&",
          left: {
            kind: "binary",
            op: ">=",
            left: { kind: "member", target: { kind: "param", name: "u" }, member: "age" },
            right: { kind: "literal", value: 18 },
          },
          right: { kind: "member", target: { kind: "param", name: "u" }, member: "active" },
        },
      }),
    );

    const result = runArrayProvider(sample, stagesFor(query));
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("UserQueries.adultsAtLeast — captured local resolves through bundle", () => {
    const minAge = 25;
    const expected = Array.from(UserQueries.adultsAtLeast(sample, minAge));
    const query = linq(
      sample,
      where((u: User) => u.age >= minAge, {
        tree: {
          kind: "binary",
          op: ">=",
          left: { kind: "member", target: { kind: "param", name: "u" }, member: "age" },
          right: { kind: "capture", name: "minAge" },
        },
        captures: { minAge },
      }),
    );

    const result = runArrayProvider(sample, stagesFor(query));
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("UserQueries.adultNames — where + select chain", () => {
    const expected = Array.from(UserQueries.adultNames(sample));
    const query = linq(
      sample,
      where((u: User) => u.age >= 18, {
        tree: {
          kind: "binary",
          op: ">=",
          left: { kind: "member", target: { kind: "param", name: "u" }, member: "age" },
          right: { kind: "literal", value: 18 },
        },
      }),
      select((u: User) => u.name, {
        tree: { kind: "member", target: { kind: "param", name: "u" }, member: "name" },
      }),
    );

    const result = runArrayProvider(sample, stagesFor(query));
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("missing queryable meta — provider falls back to closure", () => {
    const query = linq(sample, where((u: User) => u.age >= 18));
    const result = runArrayProvider(sample, stagesFor(query));

    expect(result.fellBack).toEqual(["where"]);
    expect(result.materialized).toEqual(sample.filter((u) => u.age >= 18));
  });
});
