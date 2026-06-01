# Contract: Diagnostics

**Covers**: FR-024, SC-006

## MS0026 — JSX renderable unrecognized / marker insufficient

**Severity**: Error
**Carries**: the Roslyn `Location` of the offending expression or declaration.

### When raised

1. A type is used in a JSX-renderable position (a `Children` element, a `Render()` return, the `render` entry lambda) but is **not** classifiable as any of:
   - a component (`IsJsxComponent`),
   - a native element (`JsxNativeElementTag is not null`),
   - an imported renderable (`[Import]`/`[External]` typed as / convertible to the marked `JsxElement`).
2. A `[JsxComponentBuilder]`-derived type whose render method does **not** return the marked element type (misapplied builder).

### Message shape

> `MS0026: 'TypeName' cannot be used as a JSX element. Mark it with [JsxNativeElement("tag")], derive it from a [JsxComponentBuilder] base, or declare it as an imported renderable with [Import(...)] typed as JsxElement.`

### Contract examples

| Input situation | Expected |
|-----------------|----------|
| `Children = [ new PlainPoco() ]` where `PlainPoco` has no JSX marker and isn't `JsxElement`-typed | `MS0026` at the `new PlainPoco()` location; no `.tsx` emitted for that file path with broken JSX |
| `record Bad : JsxComponent { public override JsxElement Render() => null!; }` returning a non-element | compiles (returns null is valid C#); **not** MS0026 — null return is a runtime concern, not a marker error |
| `record Bad : JsxComponent { /* missing Render */ }` | C# itself fails (abstract not implemented) — no Metano diagnostic needed |

### Invariants

- The transpiler MUST NOT emit silently-wrong JSX when recognition fails; it raises `MS0026` instead. [FR-024, Constitution V]
- A second code (`MS0027`) is introduced only if implementation reveals a condition that is genuinely distinct and not expressible as an `MS0026` message variant.
