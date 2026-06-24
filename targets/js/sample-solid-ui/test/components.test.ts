import { describe, expect, test } from "bun:test";

import { Counter } from "../src/sample-solid-ui/ui/counter.tsx";
import { CounterGroup } from "../src/sample-solid-ui/ui/counter-group.tsx";

// Smoke test: the JSX component records lower to SolidJS function components.
// The repo has no DOM test harness (no happy-dom/jsdom), so rendering the
// returned JSX would need a document; instead we assert the lowering produced
// callable function components with the expected `props` arity — enough to
// catch a regression where a component fails to emit or emits a non-function
// (e.g. a class wrapper, which the JSX lowering must never produce).
describe("sample-solid-ui components", () => {
  test("Counter is a function component taking props", () => {
    expect(typeof Counter).toBe("function");
    expect(Counter.length).toBe(1);
  });

  test("CounterGroup is a function component taking props", () => {
    expect(typeof CounterGroup).toBe("function");
    expect(CounterGroup.length).toBe(1);
  });
});
