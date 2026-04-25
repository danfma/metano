# ADR-0016 — `[This]` attribute via `bindReceiver` runtime helper

**Status:** Accepted
**Date:** 2026-04-24

## Context

JS/TS APIs frequently rely on the function's `this` binding rather
than an explicit parameter. The canonical case is a DOM event
handler:

```ts
element.onclick = function (event) {
  this.innerHTML = "clicked";
};
```

TypeScript expresses the contract through a synthetic first
parameter on the function type:

```ts
type MouseEventListener = (this: Element, event: MouseEvent) => void;
```

C# has no native way to say "the first parameter IS the receiver."
Without an explicit attribute, our DOM bindings either drop the
receiver (losing the type information) or expose it as a regular
positional argument that emits incorrectly at the dispatch boundary.

Issue #113 introduced `[This]` to mark the first parameter of a
delegate or inlinable method as the synthetic receiver. The
attribute is parameter-only, applies strictly to the index-0 slot,
and rejects `ref` / `out` / `in` / `params` modifiers (MS0018).

## Decision

The TypeScript backend lowers `[This]` through a thin runtime
helper, **`bindReceiver`**, instead of synthesizing
`function (this: T, ...) { ... }` blocks at every call site.

### Helper

```typescript
export function bindReceiver<Receiver, Rest extends readonly unknown[], R>(
  fn: (receiver: Receiver, ...rest: Rest) => R,
): (this: Receiver, ...args: Rest) => R {
  return function (this: Receiver, ...args: Rest): R {
    return fn(this, ...args);
  };
}
```

The wrapper is a classic `function` so the dispatching runtime
(DOM event loop, native API, manual `.call`) rebinds `this` on
invocation. It then forwards that runtime `this` as the first
positional argument to the wrapped arrow. The arrow itself stays
lexically scoped — any `this` captured from the enclosing C# class
flows through ordinary closure semantics, never aliased to the
runtime receiver.

### Generated shapes

- **Delegate type declaration**: `(this: T, ...rest) => R` — keeps
  the TS-idiomatic surface so consumers see the receiver shape on
  the type line.
- **Lambda assignment**: `bindReceiver((self, ...rest) => body)` —
  the lambda's first parameter (the receiver, named by the user)
  stays in the positional list; the helper feeds the dispatcher's
  `this` into it. No body rewriting, no `function` keyword on the
  generated lambda.
- **Method-group assignment**: `bindReceiver(handler)` — the
  reference is wrapped at the assignment site so the runtime can
  trampoline the receiver through the named method.
- **Direct invocation**: `handler.call(receiver, ...rest)` — when
  C# code invokes a `[This]` delegate directly, the call is
  rewritten to use `Function.prototype.call`, supplying the first
  argument as the JS `this`.
- **Dart**: no-op — the receiver re-introduces as a regular
  positional parameter at index 0.

### Rejected alternative: `function`-keyword emission with body rewrite

The first iteration of Slice B emitted lambdas as
`function (this: T, ...) { ... }` and rewrote every identifier
referencing the receiver parameter into the keyword `this`. The
shape was structurally correct for the receiver itself but silently
collided with `this` references captured from the enclosing C#
class:

```csharp
OnClick = self => self.InnerHtml = this.Name;  // `this` = Widget
```

emitted as

```ts
this.onClick = function (this: Element) {
  return this.innerHtml = this.name;  // both `this` = Element. BUG.
};
```

Each `function` keyword rebinds `this`, defeating the closure of
the outer `Widget` instance. Patching this required emitting
`const _self = this;` shims, a body walker, name-collision logic,
and nested-scope bookkeeping — substantial machinery to defend
against a footgun the runtime helper avoids by construction.

`bindReceiver` keeps the lambda as a plain arrow, leaves the body
untouched, and lets the V8/JSC closure semantics handle outer
`this` for free.

## Consequences

- (+) DOM bindings emit ergonomic TypeScript (`element.onclick =
      bindReceiver((self, e) => self.innerHTML = e)`) without
      bespoke `function`-keyword printing or per-lambda body
      rewriting.
- (+) Outer-class `this` capture works through ordinary lexical
      scope. The generated lambda body matches the C# author's
      mental model.
- (+) Method-group and lambda paths share a single mechanism — the
      bridge wraps the reference / arrow in `bindReceiver` and the
      ImportCollector auto-imports the helper from
      `metano-runtime`. No new IR category for runtime
      requirements.
- (+) Cross-assembly works automatically — `SymbolHelper.HasThis`
      already namespace-qualifies against `Metano.Annotations`, so
      a delegate declared in a referenced library propagates the
      attribute through `CompilationReference` symbols.
- (+) Dart degrades cleanly — the receiver is re-introduced as a
      positional parameter via `IrToDartTypeMapper.BuildDartFunctionParameters`.
      No JS-only emission leaks into the Dart output.
- (−) Runtime dependency on `bindReceiver`. One import per file
      that uses a `[This]` lambda or method-group assignment. The
      helper itself is ~5 lines.
- (−) A handler-registration costs one extra closure allocation
      (the `function` trampoline). Negligible against typical DOM
      event registration paths; significant only in tight loops
      that re-register handlers per frame (none in our samples).
- (−) Stack traces include one extra frame (`bindReceiver`'s
      anonymous wrapper). Source maps do not yet annotate this
      frame; a follow-up issue can hide it via the
      [`x_google_ignoreList`](https://developer.chrome.com/blog/devtools-modern-web-debugging#x_google_ignorelist)
      source-map extension, which Chrome DevTools, Firefox, and
      Node's `--enable-source-maps` already honor for
      vendor/runtime frames. Listing the `metano-runtime` package
      under `x_google_ignoreList` collapses the trampoline frame
      automatically without per-call annotations.

## Alternatives considered

- **`function`-keyword emission with body rewrite** — rejected as
  documented above; closure of outer `this` collides with the
  runtime-rebound `this`.
- **`bindTo(this, fn)` with explicit placeholder** — semantically
  identical to `bindReceiver(fn)` but requires the user (or
  compiler) to surface a receiver placeholder. Less idiomatic;
  generics infer the receiver type from the wrapped arrow without
  any extra hint.
- **Compiler-side `Function.prototype.bind` instead of
  `bindReceiver`** — `bind` pre-binds a fixed receiver, defeating
  the dispatcher rebinding. Wrong shape for DOM event handlers.
- **Defer to a TS source-level construct** (e.g., emitting a
  thinly-wrapped class around each delegate) — every variant we
  considered required either body rewriting or tagging that the
  runtime helper avoids.

## References

- Code pointers:
  - `src/Metano/Annotations/ThisAttribute.cs`
  - `src/Metano.Compiler/SymbolHelper.cs` (`HasThis`)
  - `src/Metano.Compiler/IR/IrTypeRef.cs` (`IrFunctionTypeRef.ThisType`)
  - `src/Metano.Compiler/IR/IrExpression.cs` (`IrLambdaExpression.UsesThis` /
    `ThisType`)
  - `src/Metano.Compiler/Extraction/IrTypeRefMapper.cs`
    (`MapDelegateType` populates ThisType)
  - `src/Metano.Compiler/Extraction/IrExpressionExtractor.cs`
    (`ResolveLambdaReceiverType`,
    `WrapMethodGroupForThisDelegate`,
    direct-invocation `.call` lowering)
  - `src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.cs`
    (`MapLambda` wraps in `bindReceiver`)
  - `src/Metano.Compiler.TypeScript/TypeScript/Printer.cs`
    (`(this: T, ...) => R` printer branch)
  - `src/Metano.Compiler.Dart/Bridge/IrToDartTypeMapper.cs`
    (`BuildDartFunctionParameters` re-introduces receiver as
    positional)
  - `targets/js/metano-runtime/src/system/bind-receiver.ts`
    (`bindReceiver` helper)
- Issues / PRs:
  - #113 — `[This]` attribute (closes here)
  - #114 — Slice A delegate type signature
  - #123 — Slice B `bindReceiver` runtime wrap
- Related ADRs:
  - ADR-0015 — Attribute family for compile-time erasure (the
    naming convention `[This]` follows)
  - ADR-0014 — Loose null equality (similar runtime-side
    accommodation for a TS/JS asymmetry)
