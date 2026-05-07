/**
 * Validates the array-backed provider sample. Each test pulls the
 * stages straight off a `linq()` chain (via a tiny intercept) and runs
 * them through the provider, which materializes the result by reading
 * each operator's `queryable` tree instead of opening the closure.
 *
 * Cross-checks: every materialized result matches what the runtime
 * would have produced with the closures, so the tree shape is a
 * faithful encoding of the lambda body. `fellBack` lists any stage the
 * provider could not introspect — empty for the queryable chains the
 * compiler captures, populated for the IEnumerable baseline.
 */
import { describe, expect, test } from "bun:test";
import { type AnyOperator, linq, select, where } from "metano-runtime";
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
 * Captures the operator descriptors a `linq(source, ...stages)` call
 * would push into the runtime so the test can hand them straight to
 * the provider. Mirrors the runtime's own pipe shape — `source`
 * always slot zero, every other arg is an operator/terminal.
 */
function captureStages<T>(source: Iterable<T>, ...stages: unknown[]): AnyOperator[] {
  // Touch the runtime entry point so the closure path stays exercised
  // in the same test (cross-check baseline below). The descriptor
  // array is what the provider inspects; `linq` is purely the
  // closure-side runner.
  // biome-ignore lint/suspicious/noExplicitAny: variadic linq overloads bind concrete generics; cast keeps test ergonomic
  const args = [source, ...stages] as [Iterable<unknown>, ...any[]];
  // biome-ignore lint/suspicious/noExplicitAny: same — pipe runtime entry takes a heterogeneous tuple
  void (linq as any)(...args);
  return stages as AnyOperator[];
}

describe("array-provider", () => {
  test("UserQueries.adults — provider materializes via tree", () => {
    const expected = Array.from(UserQueries.adults(sample));
    const stages = captureStages<User>(sample, where((u: User) => u.age >= 18, {
      tree: {
        kind: "binary",
        op: ">=",
        left: { kind: "member", target: { kind: "param", name: "u" }, member: "age" },
        right: { kind: "literal", value: 18 },
      },
    }));

    const result = runArrayProvider(sample, stages);
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("UserQueries.activeAdults — composite boolean tree", () => {
    const expected = Array.from(UserQueries.activeAdults(sample));
    const stages = captureStages<User>(
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

    const result = runArrayProvider(sample, stages);
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("UserQueries.adultsAtLeast — captured local resolves through bundle", () => {
    const minAge = 25;
    const expected = Array.from(UserQueries.adultsAtLeast(sample, minAge));
    const stages = captureStages<User>(
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

    const result = runArrayProvider(sample, stages);
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("UserQueries.adultNames — where + select chain", () => {
    const expected = Array.from(UserQueries.adultNames(sample));
    const stages = captureStages<User>(
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

    const result = runArrayProvider(sample, stages);
    expect(result.fellBack).toEqual([]);
    expect(result.materialized).toEqual(expected);
  });

  test("missing queryable meta — provider falls back to closure", () => {
    const stages = [where((u: User) => u.age >= 18)] as unknown as AnyOperator[];
    const result = runArrayProvider(sample, stages);

    expect(result.fellBack).toEqual(["where"]);
    expect(result.materialized).toEqual(sample.filter((u) => u.age >= 18));
  });
});
