/**
 * The cases of spec 011's collection benchmark (FR-039, plan.md § Benchmark plan).
 * Each case is a pair of kernels over the same data: the native baseline and the
 * runtime variant, each making one pass over `N` elements (`ops` operations), in the
 * hot-helper shape of research R17 (the kernel is called many times).
 */

/** A budget of NFR-001: normalized overhead per operation, in nanoseconds. */
export interface BenchCase {
  readonly name: string;
  /** NFR-001's row (one figure, or one per engine), or `undefined` for a reported-only case. */
  readonly budgetNs?: number | { readonly node: number; readonly bun: number };
  /** Operations per kernel call. */
  readonly ops: string;
  /** Module-level code shared by both kernels (data, collections). */
  readonly setup: string;
  readonly baseline: string;
  readonly variant: string;
  /** Run against both dists (checked, unchecked + define false). */
  readonly everyMode?: boolean;
  /** A row the stage accepts over budget, with the hand-in that owns it (W4-35). */
  readonly deferredTo?: string;
}

const NUMBERS = "const a = Array.from({ length: N }, (_, i) => i);";

const LOWERED_FOREACH = `
  let s = 0;
  {
    const $l = l;
    const $a = $l._items;
    const $n = $a.length;
    const $v = (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) ? $l._version : 0;
    for (let $i = 0; ; $i++) {
      if ((typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) && ($l._version !== $v || $a.length !== $n)) throwModified();
      if ($i === $n) break;
      const x = $a[$i];
      s += x;
    }
  }
  return s;`;

const HOISTED_FOREACH = `
  let s = 0;
  {
    const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
    const $l = l;
    const $a = $l._items;
    const $n = $a.length;
    const $v = $c ? $l._version : 0;
    for (let $i = 0; ; $i++) {
      if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
      if ($i === $n) break;
      const x = $a[$i];
      s += x;
    }
  }
  return s;`;

const FOR_OF_ARRAY = "let s = 0; for (const x of a) s += x; return s;";

export const CASES: readonly BenchCase[] = [
  {
    name: "List.get (sequential)",
    budgetNs: 1.0,
    ops: "N",
    everyMode: true,
    setup: `${NUMBERS} const l = List.of(a.slice());`,
    baseline: "let s = 0; const n = a.length; for (let i = 0; i < n; i++) s += a[i]; return s;",
    variant: "let s = 0; const n = l.count; for (let i = 0; i < n; i++) s += l.get(i); return s;",
  },
  {
    name: "List.get (data-driven index)",
    budgetNs: { node: 1.75, bun: 1.0 },
    ops: "N",
    everyMode: true,
    setup: `${NUMBERS} const l = List.of(a.slice());
      const idx = a.map((_, i) => (i * 7919) % N);`,
    baseline: "let s = 0; for (let i = 0; i < N; i++) s += a[idx[i]]; return s;",
    variant: "let s = 0; for (let i = 0; i < N; i++) s += l.get(idx[i]); return s;",
  },
  {
    name: "List.set",
    budgetNs: { node: 1.75, bun: 1.0 },
    ops: "N",
    everyMode: true,
    setup: `${NUMBERS} const l = List.of(a.slice());`,
    baseline: "for (let i = 0; i < N; i++) a[i] = i; return a[1];",
    variant: "for (let i = 0; i < N; i++) l.set(i, i); return l.get(1);",
  },
  {
    name: "list[i] += 1 (direct form)",
    budgetNs: 1.5,
    ops: "N",
    everyMode: true,
    setup: `${NUMBERS} const l = List.of(a.slice());`,
    baseline: "for (let i = 0; i < N; i++) a[i] = a[i] + 1; return a[1];",
    variant: "for (let i = 0; i < N; i++) l.set(i, l.get(i) + 1); return l.get(1);",
  },
  {
    name: "list[i] += 1 (closure fallback, update)",
    budgetNs: 3.0,
    ops: "N",
    setup: `${NUMBERS} const l = List.of(a.slice());`,
    baseline: "for (let i = 0; i < N; i++) a[i] = a[i] + 1; return a[1];",
    variant: "for (let i = 0; i < N; i++) l.update(i, (v) => v + 1); return l.get(1);",
  },
  {
    name: "get at a polymorphic site (4 element types)",
    budgetNs: 2.0,
    ops: "N",
    setup: `const arrays = [
        Array.from({ length: N / 4 }, (_, i) => i),
        Array.from({ length: N / 4 }, (_, i) => i + 0.5),
        Array.from({ length: N / 4 }, (_, i) => ({ v: i })),
        Array.from({ length: N / 4 }, (_, i) => String(i)),
      ];
      const lists = arrays.map((xs) => List.of(xs.slice()));
      function touch(x) { return typeof x === "number" ? x : 1; }`,
    baseline:
      "let s = 0; for (let k = 0; k < 4; k++) { const xs = arrays[k]; for (let i = 0; i < xs.length; i++) s += touch(xs[i]); } return s;",
    variant:
      "let s = 0; for (let k = 0; k < 4; k++) { const xs = lists[k]; const n = xs.count; for (let i = 0; i < n; i++) s += touch(xs.get(i)); } return s;",
  },
  {
    name: "get at a site seeing List and a fixed-size adopted list",
    budgetNs: 2.0,
    ops: "N",
    everyMode: true,
    setup: `const halves = [Array.from({ length: N / 2 }, (_, i) => i), Array.from({ length: N / 2 }, (_, i) => i)];
      const lists = [List.of(halves[0].slice()), List.adopt(halves[1].slice(), { fixedSize: true })];`,
    baseline:
      "let s = 0; for (let k = 0; k < 2; k++) { const xs = halves[k]; for (let i = 0; i < xs.length; i++) s += xs[i]; } return s;",
    variant:
      "let s = 0; for (let k = 0; k < 2; k++) { const xs = lists[k]; const n = xs.count; for (let i = 0; i < n; i++) s += xs.get(i); } return s;",
  },
  {
    name: "List.add",
    budgetNs: 0.5,
    ops: "N",
    setup: "",
    baseline: "const xs = []; for (let i = 0; i < N; i++) xs.push(i); return xs.length;",
    variant: "const xs = new List(); for (let i = 0; i < N; i++) xs.add(i); return xs.count;",
  },
  {
    name: "lowered foreach over List (per element; final $c form)",
    budgetNs: { node: 1.25, bun: 0.5 },
    ops: "N",
    everyMode: true,
    setup: `${NUMBERS} const l = List.of(a.slice());`,
    baseline: FOR_OF_ARRAY,
    variant: HOISTED_FOREACH,
  },
  {
    name: "lowered foreach, guard read on every step (superseded R0 form)",
    ops: "N",
    everyMode: true,
    setup: `${NUMBERS} const l = List.of(a.slice());`,
    baseline: FOR_OF_ARRAY,
    variant: LOWERED_FOREACH,
  },
  {
    name: "List [Symbol.iterator] (for..of, interop)",
    ops: "N",
    setup: `${NUMBERS} const l = List.of(a.slice());`,
    baseline: FOR_OF_ARRAY,
    variant: "let s = 0; for (const x of l) s += x; return s;",
  },
  {
    name: "Dictionary.get (number keys)",
    budgetNs: 2.0,
    deferredTo: "R2",
    ops: "N",
    setup: `const m = new Map(); const d = new Dictionary();
      for (let i = 0; i < N; i++) { m.set(i, i); d.set(i, i); }`,
    baseline: "let s = 0; for (let i = 0; i < N; i++) s += m.get(i); return s;",
    variant: "let s = 0; for (let i = 0; i < N; i++) s += d.get(i); return s;",
  },
  {
    name: "Dictionary.set (number keys, overwrite)",
    budgetNs: 2.0,
    deferredTo: "R2",
    ops: "N",
    setup: `const m = new Map(); const d = new Dictionary();
      for (let i = 0; i < N; i++) { m.set(i, i); d.set(i, i); }`,
    baseline: "for (let i = 0; i < N; i++) m.set(i, i); return m.size;",
    variant: "for (let i = 0; i < N; i++) d.set(i, i); return d.count;",
  },
  {
    name: "Dictionary.containsKey (string keys)",
    budgetNs: 2.0,
    deferredTo: "R2",
    ops: "N",
    setup: `const keys = Array.from({ length: N }, (_, i) => "k" + i);
      const m = new Map(); const d = new Dictionary();
      for (const k of keys) { m.set(k, 1); d.set(k, 1); }`,
    baseline: "let s = 0; for (let i = 0; i < N; i++) if (m.has(keys[i])) s++; return s;",
    variant: "let s = 0; for (let i = 0; i < N; i++) if (d.containsKey(keys[i])) s++; return s;",
  },
  {
    name: "Dictionary.add (string keys, fresh dictionary)",
    budgetNs: 2.0,
    deferredTo: "R2",
    ops: "N",
    setup: `const keys = Array.from({ length: N }, (_, i) => "k" + i);`,
    baseline: "const m = new Map(); for (let i = 0; i < N; i++) m.set(keys[i], i); return m.size;",
    variant:
      "const d = new Dictionary(); for (let i = 0; i < N; i++) d.add(keys[i], i); return d.count;",
  },
  {
    name: "HashSet.add (numbers, fresh set)",
    budgetNs: 2.0,
    deferredTo: "R2",
    ops: "N",
    setup: "",
    baseline: "const s = new Set(); for (let i = 0; i < N; i++) s.add(i); return s.size;",
    variant: "const s = new HashSet(); for (let i = 0; i < N; i++) s.add(i); return s.count;",
  },
  {
    name: "HashSet.contains (numbers)",
    budgetNs: 2.0,
    deferredTo: "R2",
    ops: "N",
    setup: `const ns = new Set(); const hs = new HashSet();
      for (let i = 0; i < N; i++) { ns.add(i); hs.add(i); }`,
    baseline: "let s = 0; for (let i = 0; i < N; i++) if (ns.has(i)) s++; return s;",
    variant: "let s = 0; for (let i = 0; i < N; i++) if (hs.contains(i)) s++; return s;",
  },
  {
    name: "HashSet.remove + add (numbers)",
    budgetNs: 2.0,
    deferredTo: "R2",
    ops: "2 * N",
    setup: `const ns = new Set(); const hs = new HashSet();
      for (let i = 0; i < N; i++) { ns.add(i); hs.add(i); }`,
    baseline: "for (let i = 0; i < N; i++) { ns.delete(i); ns.add(i); } return ns.size;",
    variant: "for (let i = 0; i < N; i++) { hs.remove(i); hs.add(i); } return hs.count;",
  },
  {
    name: "Queue.enqueue + dequeue",
    budgetNs: 1.5,
    ops: "2 * N",
    setup: "",
    baseline:
      "const q = []; let head = 0; for (let i = 0; i < N; i++) q.push(i); let s = 0; for (let i = 0; i < N; i++) s += q[head++]; return s;",
    variant:
      "const q = new Queue(); for (let i = 0; i < N; i++) q.enqueue(i); let s = 0; for (let i = 0; i < N; i++) s += q.dequeue(); return s;",
  },
  {
    name: "Stack.push + pop",
    budgetNs: 1.5,
    ops: "2 * N",
    setup: "",
    baseline:
      "const st = []; for (let i = 0; i < N; i++) st.push(i); let s = 0; for (let i = 0; i < N; i++) s += st.pop(); return s;",
    variant:
      "const st = new Stack(); for (let i = 0; i < N; i++) st.push(i); let s = 0; for (let i = 0; i < N; i++) s += st.pop(); return s;",
  },
  {
    name: "Dictionary.get (record keys, reported)",
    ops: "N",
    setup: `class P { constructor(x, y) { this.x = x; this.y = y; }
        equals(o) { return o instanceof P && o.x === this.x && o.y === this.y; }
        hashCode() { return (this.x * 31 + this.y) | 0; } }
      const keys = Array.from({ length: N }, (_, i) => new P(i, i + 1));
      const probes = keys.map((k) => new P(k.x, k.y));
      const m = new Map(keys.map((k, i) => [k, i])); const d = new Dictionary();
      keys.forEach((k, i) => d.set(k, i));`,
    baseline: "let s = 0; for (let i = 0; i < N; i++) s += m.get(keys[i]); return s;",
    variant: "let s = 0; for (let i = 0; i < N; i++) s += d.get(probes[i]); return s;",
  },
];
