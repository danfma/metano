# Contract: TypeScript emission (goldens)

**Normative for**: FR-007, FR-009, FR-010, FR-014, FR-015, FR-018–FR-026, FR-030–FR-036, FR-044, FR-045. Each block is a golden under `tests/Metano.Tests/Expected/`; import lines are shown only where they are the point. Every golden passes the FR-043 golden bundle check: compiled with tsgo and bundled as a side-effect-only import with packages external, nothing but its external `import` statements remains.

## Collections (`collections-basic.ts`)

```csharp
var xs = new List<int> { 1, 2 }; xs.Add(3); xs[0] = 9; xs[1] += 5; var last = xs[^1]; var mid = xs[1..];
var byName = new Dictionary<string, Point> { ["a"] = new(1, 2) }; var added = new Dictionary<string, int> { { "x", 1 } };
var p = byName["a"]; byName.Keys.Contains("a"); byName["a"] = p; added["x"] ??= 2;
var q = new Queue<int>(); q.Enqueue(1); var head = q.Dequeue();
var s = new Stack<int>(); s.Push(1); foreach (var top in s) { }
IReadOnlyList<int> view = new[] { 1, 2 };
var copy = xs.ToJsArray(); var again = JsArray.From(xs); var shared = copy.AsList();
```

```ts
import { Dictionary, List, Queue, Stack } from "metano-runtime/system/collections";

const xs = List.of([1, 2]);
xs.add(3);
xs.set(0, 9);
xs.update(1, (v) => v + 5);
const last = xs.fromEnd(1);
const mid = xs.slice(1);
const byName = Dictionary.assign([["a", new Point(1, 2)]]);
const added = Dictionary.of([["x", 1]]);
const p = byName.get("a");
byName.containsKey("a");
byName.set("a", p);
added.update("x", (v) => v ?? 2);
const q = new Queue<number>();
q.enqueue(1);
const head = q.dequeue();
const s = new Stack<number>();
s.push(1);
for (const top of s) { }
const view = List.adopt([1, 2], { fixedSize: true });
const copy = xs.toArray();
const again = Array.from(xs);
const shared = List.adopt(copy);
```

No reflection import: this compilation asks no runtime question about any collection kind (FR-008). The golden `collections-type-arguments.ts` adds one `o is List<string>` to the same source and pins the result: `List.of([1, 2], [Int32Type])`, `new Queue<number>()` unchanged (no question about `Queue`), `Dictionary.assign([["a", new Point(1, 2)]], [StringType, UnknownType])` (`Point` is not reached), and `import { Int32Type, StringType, UnknownType } from "metano-runtime/system/reflection"`.

## Runtime helpers instead of IIFEs (`runtime-helpers.ts`)

```csharp
order?.Items.Clear(); var x = GetNext().Items[^1]; var o = Compute() as Order; var arr = GetArray(); var y = arr[^1];
var withCap = new List<int>(Next()); var n = GetDict()[Key()]++;
```

```ts
order?.items.clear();
const x = getNext().items.fromEnd(1);
const o = asType(compute(), Order.$runtimeType);
const arr = getArray();
const y = fromEnd(arr, 1);
const withCap = List.withCapacity<number>(next());
const n = getDict().updateReturningOld(key(), (v) => v + 1);
```

`null-conditional-guards.ts` and `index-and-range.ts` are regenerated; neither contains `(() =>` or `(($`.

## Exceptions and typed catch (`typed-catch.ts`)

```csharp
try { Run(); }
catch (ArgumentNullException e) { Log(e.ParamName); }
catch (ArgumentException e) when (e.Message.Length > 3) { throw; }
catch (Exception e) when (IsTransient(e)) { Retry(); }
catch (FormatException) { Skip(); }
catch (Exception e) { Log(e.Message); }
finally { Done(); }
throw new InvalidOperationException("busy");
```

```ts
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";
import { ArgumentNullException } from "metano-runtime/system/exceptions/argument-null-exception";
import { FormatException } from "metano-runtime/system/exceptions/format-exception";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import { toException } from "metano-runtime/system/exceptions/to-exception";
import { tryFilter } from "metano-runtime/system/reflection";

try {
  run();
} catch (e$) {
  if (e$ instanceof ArgumentNullException) {
    const e = e$;
    log(e.paramName);
  } else if (e$ instanceof ArgumentException && tryFilter(() => e$.message.length > 3)) {
    const e = e$;
    throw e$;
  } else if (tryFilter(() => isTransient(toException(e$)))) {
    const e = toException(e$);
    retry();
  } else if (e$ instanceof FormatException) {
    skip();
  } else {
    const e = toException(e$);
    log(e.message);
  }
} finally {
  done();
}
throw new InvalidOperationException("busy");
```

The filtered `Exception` arm reads `toException(e$)` in its filter and binds the same value in its body; `toException` returns its argument unchanged for an `Exception`, so the two calls agree. No descriptor and no reflection import other than `tryFilter` is needed by a typed catch.

## Runtime type tests (`runtime-types-factory.ts`)

```csharp
public abstract class Shape { public static Shape Create(string k) => k == "c" ? new Circle() : new Square(); }
public class Circle : Shape, IHasArea { … } public class Square : Shape { … } public class Blob { … }
var s = Shape.Create("c");
var n = s.GetType().Name; var same = s.GetType() == typeof(Circle);
var a = s is IHasArea; var c = s as Circle; var d = (Circle)s;
var k = s switch { Circle c1 => 1, Square s1 => 2, _ => 0 };
```

```ts
import { getType, isType, castTo, type ClassType } from "metano-runtime/system/reflection";
import { IHasArea$runtimeType } from "./i-has-area";

let Shape$rt: ClassType<Shape> | undefined;
export abstract class Shape {
  static get $runtimeType(): ClassType<Shape> {
    return (Shape$rt ??= { kind: "class", name: "Shape", fullName: "Geometry.Shape", namespace: "Geometry", ctor: Shape, interfaces: [], typeParameters: [], isAbstract: true, isValueType: false, test: (v: unknown): v is Shape => v instanceof Shape });
  }
  static create(k: string): Shape { return k === "c" ? new Circle() : new Square(); }
}
let Circle$rt: ClassType<Circle> | undefined;
export class Circle extends Shape implements IHasArea {
  static get $runtimeType(): ClassType<Circle> {
    return (Circle$rt ??= { kind: "class", …, base: () => Shape.$runtimeType, interfaces: [() => IHasArea$runtimeType], create: () => new Circle(), test: (v: unknown): v is Circle => v instanceof Circle });
  }
}
let Square$rt: ClassType<Square> | undefined;
export class Square extends Shape {
  static get $runtimeType(): ClassType<Square> {
    return (Square$rt ??= { kind: "class", …, base: () => Shape.$runtimeType, interfaces: [], test: (v: unknown): v is Square => v instanceof Square });   // reached: GetType() on a Shape receiver reaches every derived class (FR-027)
  }
}
export class Blob { … }   // no getter: nothing reaches Blob (US3-7)
const s = Shape.create("c");
const n = getType(s).name;
const same = getType(s) === Circle.$runtimeType;
const a = isType(s, IHasArea$runtimeType);
const c = s instanceof Circle ? s : null;          // stable operand keeps the instanceof form (FR-026)
const d = castTo(s, Circle.$runtimeType);
const k = (($s) => { if ($s instanceof Circle) { return 1; } if ($s instanceof Square) { return 2; } return 0; })(s);
```

`i-has-area.ts`:

```ts
import { implementsInterface, type InterfaceType } from "metano-runtime/system/reflection";
export interface IHasArea { readonly area: number; }
export const IHasArea$runtimeType: InterfaceType<IHasArea> = { kind: "interface", name: "IHasArea", fullName: "Geometry.IHasArea", namespace: "Geometry", baseInterfaces: [], plainImplementers: [], test: (v: unknown): v is IHasArea => implementsInterface(v, IHasArea$runtimeType) };
```

A bundle of a program that uses `Circle` but never asks a runtime question contains none of `getType` / `isType` / `castTo`; the class getter is a literal (FR-018).

The companion `get-type-receivers.ts` pins the receiver rules of FR-027: `object o; o.GetType()` reaches nothing (plus `MS0048` when a `[PlainObject]` exists), `Exception e; e.GetType()` reaches nothing (exception classes carry their getter anyway), `IHasArea h; h.GetType()` reaches `Circle` (a transpiled implementer) and not `Blob`, `T t; t.GetType()` under `where T : Shape` reaches `Shape` and its derived classes.

## Reified type parameters (`reified-type-parameters.ts`)

```csharp
public static bool IsA<T>(object o) => o is T;
public static T First<T>(IEnumerable<T> xs) => xs.First();
public static T Make<T>() where T : new() => new T();
public static T Zero<T>() => default;
public static T ZeroOf<T>(object o) => o is T ? (T)o : default;
public static bool Try<T>(object o, int n = 1, params string[] rest) => o is T;
Func<object, bool> f = IsA<Circle>;
IsA<int>(o); IsA<List<string>>(o); Make<Circle>(); Zero<int>(); ZeroOf<Point>(o); Try<Circle>(o);
```

```ts
import { Int32Type, UnknownType, type RuntimeType, isType, castTo, StringType } from "metano-runtime/system/reflection";
import { listType } from "metano-runtime/system/collections";

export function isA<T>(o: unknown, T$type: RuntimeType<T>): boolean { return isType(o, T$type); }
export function first<T>(xs: Iterable<T>): T { return linq(xs, first()); }          // unchanged signature
export function make<T>(T$type: RuntimeType<T>): T { return T$type.create!(); }       // runtime MissingMethodException when absent
export function zero<T>(): T { return null as T; }                                    // default(T) alone does not reify (today's output, #291)
export function zeroOf<T>(o: unknown, T$type: RuntimeType<T>): T { return isType(o, T$type) ? castTo(o, T$type) : (T$type.default?.() ?? null as T); }
export function try_<T>(o: unknown, n: number = 1, T$type: RuntimeType<T> = UnknownType, ...rest: string[]): boolean { return isType(o, T$type); }
const f = (o: unknown) => isA(o, Circle.$runtimeType);
isA(o, Int32Type); isA(o, listType(StringType)); make(Circle.$runtimeType); zero(); zeroOf(o, Point.$runtimeType); try_(o, 1, Circle.$runtimeType);
```

(`try_` only illustrates the position rule; the real name follows the existing naming policy. `ZeroOf<Point>` with a struct `Point` returns a zeroed `new Point()` through `default()`.)

## Plain objects (`plain-object-tests.ts`)

```csharp
[PlainObject] public record UserDto(string Name); [PlainObject] public record NamedDto(string Name, int Age);
var u = o is UserDto; var t = dto.GetType(); var g = o.GetType();
```

```ts
const u = isUserDto(o);                 // MS0047 warning: NamedDto covers UserDto
const t = UserDto$runtimeType;          // constant: no [PlainObject] derives from UserDto
const g = getType(o);                   // MS0048 warning
```

## Strict union guard module (`strict-union-guard-module`, FR-029)

```ts
// shape.ts — the base; no import of any variant
export abstract class Shape { … static get $runtimeType() { … } }
// circle-dto.ts
import { Shape } from "./shape";
export class CircleDto extends Shape { … }
export function isCircleDto(v: unknown): v is CircleDto { … }
// shape.guard.ts — the strict guard, in its own module
import { Shape } from "./shape";
import { isCircleDto } from "./circle-dto";
import { isSquareDto } from "./square-dto";
const variants = [() => isCircleDto, () => isSquareDto];
export function isShape(value: unknown): value is Shape { … }
```

A Bun test imports `./shape` first, unbundled, then `./shape.guard`, and calls `isShape` on a plain `{ kind: "circle", … }`: no `ReferenceError`, and the strict variant guard runs.

## Numeric enums (`numeric-enum-const.ts`, `numeric-enum-flags.ts`, FR-044)

```ts
export const Color = { Red: 0, Green: 1 } as const;
export type Color = (typeof Color)[keyof typeof Color];
```

```csharp
[Flags] public enum Perms { Read = 1, Write = 2, Execute = 4 }
var p = Perms.Read | Perms.Write; p |= Perms.Execute; bool canWrite = (p & Perms.Write) != 0;
```

```ts
export const Perms = { None: 0, Read: 1, Write: 2, Execute: 4 } as const;
export type Perms = number & { readonly __enum?: "Perms" };
let p: Perms = Perms.Read | Perms.Write;
p |= Perms.Execute;
const canWrite = (p & Perms.Write) !== 0;
const zero: Perms = 0;              // default(Perms)
const cast: Perms = 3 as Perms;     // (Perms)3
```

The golden also contains `p &= ~Perms.Read`, `p ^ Perms.Write`, a `switch` over members and an array of `Perms`; `tsgo --strict` accepts all of them and rejects `Perms p = other` where `other` is a different branded enum (research R13).

`[StringEnum]` output is unchanged (`Expected/string-enum.ts`: an `as const` object plus `type Color = typeof Color[keyof typeof Color]`).

## `package.json` (`package-json-side-effects.json`, FR-045)

A package with `[ModuleEntryPoint]` `Program.Main` in `app/program.ts` and no other entry point:

```json
{ "sideEffects": ["./src/app/program.ts", "./dist/app/program.js"] }
```

Both paths are listed because spec 008's `source` condition resolves the package to `.ts` for Vite / `customConditions` consumers and the `import` condition to `dist`.

A package without entry points gets `"sideEffects": []`.

## `dynamic`, inline code, escape hatches (`dynamic-and-interop.ts`)

```csharp
dynamic cfg = Js.Code("globalThis.__config");
string name = cfg.AppName; int n = (int)cfg.retries; var shape = (Circle)cfg.shape; var ok = cfg is Circle;
F(cfg); return cfg;   // void F(string s); string-returning method
var el = Js.Code<HtmlElement>($"document.querySelector({selector})");
Js.Code($"{el}.scrollIntoView({{ block: {mode} }})");
Js.Code($"{Inferno.CreateElement}('div')");                 // [Import("createElement", "inferno-create-element")] facade
var dto = raw.UnsafeCast<UserDto>(); var any = order.AsDynamic(); any.extra = 1;
var unchecked1 = (List<string>)boxedJsArray;                 // MS0055
```

```ts
import { createElement } from "inferno-create-element";

const cfg: any = (globalThis.__config);
const name = cfg.AppName;                                     // implicit conversion: nothing emitted (FR-030)
const n = Math.trunc(cfg.retries);                            // the static (int)double lowering
const shape = castTo(cfg.shape, Circle.$runtimeType);         // explicit reference cast: checked
const ok = cfg instanceof Circle;
f(cfg);
return cfg;
const el = (document.querySelector(selector)) as HtmlElement;
el.scrollIntoView({ block: mode });
createElement('div');
const dto = raw as unknown as UserDto;
const any = (order as any);
any.extra = 1;
const unchecked1 = boxedJsArray as unknown as List<string>;   // MS0055 at the cast
```
