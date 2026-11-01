# Contract: TypeScript emission (goldens)

**Normative for**: FR-007, FR-009, FR-010, FR-014, FR-015, FR-020–FR-026, FR-030–FR-034, FR-040, FR-041. Each block is a golden under `tests/Metano.Tests/Expected/`; import lines are shown only where they are the point.

## Collections (`collections-basic.ts`)

```csharp
var xs = new List<int> { 1, 2 }; xs.Add(3); xs[0] = 9; var last = xs[^1]; var mid = xs[1..];
var byName = new Dictionary<string, Point> { ["a"] = new(1, 2) }; var p = byName["a"]; byName.Keys.Contains("a");
var q = new Queue<int>(); q.Enqueue(1); var head = q.Dequeue();
var s = new Stack<int>(); s.Push(1); foreach (var top in s) { }
IReadOnlyList<int> view = new[] { 1, 2 };
var copy = xs.ToJsArray(); var again = JsArray.From(xs); var shared = copy.AsList();
```

```ts
import { Dictionary, List, Queue, Stack } from "metano-runtime/system/collections";
import { Int32, String as String$, Unknown } from "metano-runtime/system/reflection";   // only because descriptors are passed (FR-008)

const xs = List.of([1, 2], [Int32]);
xs.add(3);
xs.set(0, 9);
const last = xs.fromEnd(1);
const mid = xs.slice(1);
const byName = Dictionary.of([["a", new Point(1, 2)]], [String$, Unknown]);   // Point is not reached, so Unknown (see note)
const p = byName.get("a");
byName.containsKey("a");
const q = new Queue<number>(null, [Int32]);
q.enqueue(1);
const head = q.dequeue();
const s = new Stack<number>(null, [Int32]);
s.push(1);
for (const top of s) { }
const view = List.adopt([1, 2]);
const copy = xs.toArray();
const again = Array.from(xs);
const shared = List.adopt(copy);
```

Note on class-typed arguments: passing `Point[runtimeType]` would make every `Dictionary<string, Point>` reach `Point` and defeat D14. FR-008 passes the class descriptor only when `Point` is reached for another reason (FR-027); otherwise `Unknown`. The golden `collections-type-arguments.ts` pins `Unknown` for an unreached class and the descriptor for a reached one. The `import { Int32, String as String$, listType }` line exists only for the descriptors; a program whose collections hold only unreached classes imports `Unknown` alone.

## Runtime helpers instead of IIFEs (`runtime-helpers.ts`)

```csharp
order?.Items.Clear(); var x = GetNext().Items[^1]; var o = Compute() as Order; var arr = GetArray(); var y = arr[^1];
var withCap = new List<int>(Next());
```

```ts
order?.items.clear();
const x = getNext().items.fromEnd(1);
const o = asType(compute(), Order[runtimeType]);
const arr = getArray();
const y = fromEnd(arr, 1);
const withCap = List.withCapacity<number>(next(), [Int32]);
```

`null-conditional-guards.ts` and `index-and-range.ts` are regenerated; neither contains `(() =>` or `(($`.

## Exceptions and typed catch (`typed-catch.ts`)

```csharp
try { Run(); }
catch (ArgumentNullException e) { Log(e.ParamName); }
catch (ArgumentException e) when (e.Message.Length > 3) { throw; }
catch (Exception e) { Log(e.Message); }
finally { Done(); }
throw new InvalidOperationException("busy");
```

```ts
import { ArgumentException, ArgumentNullException, InvalidOperationException, toException } from "metano-runtime/system/exceptions";

try {
  run();
} catch (e$) {
  if (e$ instanceof ArgumentNullException) {
    const e = e$;
    log(e.paramName);
  } else if (e$ instanceof ArgumentException && e$.message.length > 3) {
    const e = e$;
    throw e$;
  } else {
    const e = toException(e$);
    log(e.message);
  }
} finally {
  done();
}
throw new InvalidOperationException("busy");
```

A `when` filter that reads the variable reads `e$` (the arm binds `e` after the test).

## Runtime type tests (`runtime-types-factory.ts`)

```csharp
public abstract class Shape { public static Shape Create(string k) => k == "c" ? new Circle() : new Square(); }
public class Circle : Shape, IHasArea { … } public class Square : Shape { … }
var s = Shape.Create("c");
var n = s.GetType().Name; var same = s.GetType() == typeof(Circle);
var a = s is IHasArea; var c = s as Circle; var d = (Circle)s;
var k = s switch { Circle c1 => 1, Square s1 => 2, _ => 0 };
```

```ts
import { runtimeType, classType, getType, isType, asType, castTo, type ClassType } from "metano-runtime/system/reflection";

export abstract class Shape {
  static readonly [runtimeType]: ClassType<Shape> = classType({ name: "Shape", fullName: "Geometry.Shape", namespace: "Geometry", ctor: Shape, interfaces: [], isAbstract: true, isValueType: false });
  static create(k: string): Shape { return k === "c" ? new Circle() : new Square(); }
}
export class Circle extends Shape implements IHasArea {
  static readonly [runtimeType]: ClassType<Circle> = classType({ …, base: () => Shape[runtimeType], interfaces: [() => IHasArea.$type], create: () => new Circle() });
}
export class Square extends Shape {
  static readonly [runtimeType]: ClassType<Square> = classType({ …, base: () => Shape[runtimeType], interfaces: [] });   // reached only through GetType() on a Shape receiver (FR-027); `instanceof Square` alone would not reach it
}
const s = Shape.create("c");
const n = getType(s).name;
const same = getType(s) === Circle[runtimeType];
const a = isType(s, IHasArea.$type);
const c = s instanceof Circle ? s : null;          // stable operand keeps the instanceof form (FR-026)
const d = castTo(s, Circle[runtimeType]);
const k = (($s) => { if ($s instanceof Circle) { return 1; } if ($s instanceof Square) { return 2; } return 0; })(s);
```

Reachability note: `GetType()` on a `Shape` receiver reaches `Shape` and every transpiled type derived from it in the compilation (otherwise the answer could not be the concrete type); the golden pins that `Square` gets a descriptor for this reason and that an unrelated class does not.

## Reified type parameters (`reified-type-parameters.ts`)

```csharp
public static bool IsA<T>(object o) => o is T;
public static T First<T>(IEnumerable<T> xs) => xs.First();
public static T Make<T>() where T : new() => new T();
public static T Zero<T>() => default;
public static bool Try<T>(object o, int n = 1, params string[] rest) => o is T;
Func<object, bool> f = IsA<Circle>;
IsA<int>(o); IsA<List<string>>(o); Make<Circle>(); Zero<int>(); Try<Circle>(o);
```

```ts
import { Int32, Unknown, type RuntimeType, isType, listType, String as String$ } from "metano-runtime/system/reflection";

export function isA<T>(o: unknown, T$type: RuntimeType<T>): boolean { return isType(o, T$type); }
export function first<T>(xs: Iterable<T>): T { return linq(xs, first()); }          // unchanged signature
export function make<T>(T$type: RuntimeType<T>): T { return T$type.create!(); }       // runtime MissingMethodException when absent
export function zero<T>(T$type: RuntimeType<T>): T { return T$type.defaultValue ?? null as T; }
export function try_<T>(o: unknown, n: number = 1, T$type: RuntimeType<T> = Unknown, ...rest: string[]): boolean { return isType(o, T$type); }
const f = (o: unknown) => isA(o, Circle[runtimeType]);
isA(o, Int32); isA(o, listType(String$)); make(Circle[runtimeType]); zero(Int32); try_(o, 1, Circle[runtimeType]);
```

(`try_` only illustrates the position rule; the real name follows the existing naming policy.)

## Plain objects (`plain-object-tests.ts`)

```csharp
[PlainObject] public record UserDto(string Name); [PlainObject] public record NamedDto(string Name, int Age);
var u = o is UserDto; var t = dto.GetType(); var g = o.GetType();
```

```ts
const u = isUserDto(o);                 // MS0046 warning: NamedDto covers UserDto
const t = UserDto.$type;                // constant
const g = getType(o);                   // MS0047 warning
```

## `dynamic`, inline code, escape hatches (`dynamic-and-interop.ts`)

```csharp
dynamic cfg = Js.Code("globalThis.__config");
string name = cfg.AppName; int n = (int)cfg.retries; var ok = cfg is Circle;
var el = Js.Code<HtmlElement>($"document.querySelector({selector})");
Js.Code($"{el}.scrollIntoView({{ block: {mode} }})");
Js.Code($"{Inferno.CreateElement}('div')");                 // [Import("createElement", "inferno-create-element")] facade
var dto = raw.UnsafeCast<UserDto>(); var any = order.AsDynamic(); any.extra = 1;
var unchecked1 = (List<string>)boxedJsArray;                 // MS0054
```

```ts
import { createElement } from "inferno-create-element";

const cfg: any = (globalThis.__config);
const name = castTo(cfg.AppName, String$);
const n = castTo(cfg.retries, Int32);
const ok = cfg instanceof Circle;
const el = (document.querySelector(selector)) as HtmlElement;
el.scrollIntoView({ block: mode });
createElement('div');
const dto = raw as unknown as UserDto;
const any = (order as any);
any.extra = 1;
const unchecked1 = boxedJsArray as unknown as List<string>;   // MS0054 at the cast
```

`castTo(cfg.AppName, String$)` follows OD-10 (a); under OD-10 (b) the first line would be `const name = cfg.AppName;`.
