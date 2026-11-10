# Overloads: factories, fast paths and direct calls

C# resolves an overloaded constructor, method, operator or extension method at
compile time; JavaScript has one function per name. Metano keeps both worlds
(spec [013](../specs/013-overload-factories/spec.md), [ADR-0028](adr/0028-overload-factories.md)):

- every overload gets its own **addressable member** — a factory for a
  constructor overload, a fast path for a method, operator or module-function
  overload — named by one documented rule;
- **generated code calls those members directly**, exactly as C# bound the call,
  so it never pays for, or depends on, a runtime type check;
- the **runtime dispatcher stays public** as the fallback for hand-written
  TypeScript (`new Money(19.9)`, `money.add(1)`).

This page is the user-facing reference: what is emitted, how the names are
computed, how to pin a name, the diagnostics, the Dart mapping, and how to
migrate from 2.x.

## What is emitted (TypeScript)

```csharp
public class Dimension
{
    public int Size;
    public int Width;
    public int Height;

    public Dimension(int size) => Size = size;

    public Dimension(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public static Dimension Square(int side) => new Dimension(side, side);

    public int Add(int x) => Size + x;

    public int Add(int x, int y) => Size + x + y;
}
```

The generated TypeScript, verbatim except for the two runtime-dispatch bodies:

```ts
import { isInt32 } from "metano-runtime";

export class Dimension {
  protected static readonly byDimensionSize: unique symbol = Symbol("Dimension(int size)");

  protected static readonly byDimensionWidthHeight: unique symbol = Symbol("Dimension(int width, int height)");

  size: number = 0;

  width: number = 0;

  height: number = 0;

  constructor(overload: typeof Dimension.byDimensionSize, size: number);
  constructor(overload: typeof Dimension.byDimensionWidthHeight, width: number, height: number);
  constructor(width: number, height: number);
  constructor(size: number);
  constructor(...args: unknown[]) {
    // the guarded selector branches, then the runtime arity-and-type fallback
  }

  static fromSize(size: number): Dimension {
    return new Dimension(Dimension.byDimensionSize, size);
  }

  static fromWidthHeight(width: number, height: number): Dimension {
    return new Dimension(Dimension.byDimensionWidthHeight, width, height);
  }

  static square(side: number): Dimension {
    return Dimension.fromWidthHeight(side, side);
  }

  addXY(x: number, y: number): number {
    return this.size + x + y;
  }

  addX(x: number): number {
    return this.size + x;
  }

  add(x: number, y: number): number;
  add(x: number): number;
  add(...args: unknown[]): number {
    // the runtime dispatcher: calls this.addXY or this.addX by arity and type
  }
}
```

A non-public overload keeps its accessibility on its factory or fast path
(`private static fromWidthHeight`, a `/** @internal */` tag for `internal`); the
constructor itself takes the most permissive accessibility of the set (`MS0060`
when they differ).

| C# | TypeScript |
|---|---|
| a constructor overload | a **selector** `protected static readonly by<Type><Suffix>: unique symbol` (first among the statics) and a **factory** `static from<Suffix>(…)` with the overload's own accessibility |
| a method overload | a **fast path** `<method><Suffix>(…)` with the overload's own accessibility, beside the public dispatcher `<method>(…)` |
| an operator overload | a public static fast path `__<operator><Suffix>(a, b)` beside the operator helper `__<operator>` |
| an overloaded module function or extension method | an exported fast path `<function><Suffix>(…)` beside the exported dispatcher, re-exported from the barrel |
| an overloaded static method of a `[Branded]` type | an exported function `<method><Suffix>(…)` in the companion namespace, beside the exported dispatcher `<method>(…)` |
| `new Dimension(3)` in C# | `Dimension.fromSize(3)` |
| `: base(size)` | `super(Dimension.byDimensionSize, size)` |
| `d.Add(1)`, `base.Add(1)`, the method group `d.Add` | `d.addX(1)`, `super.addX(1)`, `d.addX.bind(d)` |
| `a + b` (user operator) | `Money.__addMoneyInt32(a, b)` |
| `Tag.S(new UId(1))` on a `[Branded]` type `Tag` | `Tag.sU(UId.create(1n))` |

An omitted optional argument is passed explicitly, with the default of the
overload C# bound. An abstract class gets selectors but no factories; a derived
class constructs its base part through the selectors. A class whose factory
keeps an inherited factory's name emits it `static override`; when it cannot be
redeclared compatibly, the factory is qualified with the type name
(`GiftReceipt.fromGiftReceiptAmount`). A derived class inherits the base
factories it does not redeclare, and they return the base type.

A call passes its arguments in parameter order, overloaded or not. When
named arguments are written in another order and that order can be observed,
the generated call evaluates them first, in the order you wrote them, through
an arrow invoked on the spot:
`(($arg1, $arg0) => c.twoAB($arg0, $arg1))(h.p, h.q)` for
`c.Two(b: h.P, a: h.Q)`. The order is observable when one argument has a side
effect, or runs code implicitly (a property getter that is not a plain
auto-property, a user-defined operator or conversion, a `ToString` called by a
string concatenation) beside a second argument that is not a constant. Field
and auto-property reads alone, or one such argument beside constants, stay a
plain call. A call or construction that is not overloaded follows the same
rule (`(($arg1, $arg0) => e.single($arg0, $arg1))(h.p, h.q)` for
`e.Single(d: h.P, f: h.Q)`), and it also stays plain when a single argument has
a side effect beside constants. An `out` argument passes no value, so
`d.TryGetValue(value: out var v, key: key)` stays plain too. Its target is
written after the call returns: a target that is itself a computation
(`out arr[F()]`, `out Get().field`) is evaluated then, not where it is written.

A stage of a LINQ chain follows the same rule: its arguments reach the
parameters they name, so `xs.Aggregate(func: (a, x) => a + x, seed: 0)` emits
`aggregate(0, (a, x) => a + x)`, and a stage with two or more order-sensitive
arguments binds them in its own position of the pipe
(`linq(xs, (($arg1, $arg0) => aggregate($arg0, $arg1))(F(), G()))` for
`xs.Aggregate(func: F(), seed: G())`), which the pipe evaluates where C#
evaluates that stage. A queryable stage captures each lambda for the parameter
it names and keeps its expression tree after the bound arguments. The static
call form (`Enumerable.Aggregate(xs, func: F(), seed: G())`) has no lowering
at all (#619).

These shapes are not covered:

- a null-conditional delegate call (`op?.Invoke(d: F(), f: G())`, #611)
  evaluates its arguments in parameter order;
- a method expanded by `[Inline]` evaluates its arguments where its body uses
  them;
- a BCL call lowered by a built-in rewrite instead of a declarative mapping
  (`Math.Round(mode: M(), value: V())`, `Math.Round(decimals: D(), d: V())`,
  `dict.GetValueOrDefault(defaultValue: D(), key: K())`) evaluates its
  arguments in parameter order;
- a call to a `[This]` delegate, which lowers to `.call(...)`, passes named
  arguments as written: out of parameter order they reach the wrong
  parameters (#376).

A `[Branded]` (or `[InlineWrapper]`) type over a primitive has no class: its
static methods are functions of a companion namespace. An overloaded group
there exports one function per overload beside the dispatcher, and generated
calls and method groups name the bound one, so `Tag.S(new UId(1))` runs
`S(UId)` even though a `UId` is a `bigint` at runtime and the dispatcher would
pick `S(long)`. A function of an overload that is not public is tagged
`/** @internal */`. Operators and conversions declared on a branded type are
not applied through their functions yet (#599), so an overloaded operator or
conversion group keeps its per-overload functions local to the namespace; and
a branded type's constructors get no factories.

Generated code only binds a type whose source the compiler can read — the
current project or a project reference. A type read from a DLL or an npm
package only keeps the dispatcher call. For a class that is correct, just
slower, unless the group is one `MS0063` reports. For a branded companion it
can run another overload than the one C# bound whenever the dispatcher cannot
tell the argument types apart at runtime (a branded value beside its
primitive, `long` beside `ulong`, `int` beside `double`), and the consumer's
compilation reports nothing: `MS0063` is reported where the type is declared.
Prefer a project reference for a library whose branded types overload static
methods.

## The naming rule

Every overload of a group gets a **suffix**; the member names are built from it.

1. **Parameter names.** The suffix is the parameter names, capitalized and
   concatenated with no joiner: `Add(int x, int y)` → `XY` → `addXY`. A
   parameterless overload's suffix is `Default` (`nowDefault`). An extension
   method's receiver is left out when every overload of the group declares the
   same receiver (`Twice(this Base b, int x)` → `twiceX`).
2. **Type names on any collision.** If two overloads of the group would get the
   same suffix, **every** overload of the group switches to its parameter types'
   exact CLR names: `F(int a)` / `F(string a)` → `fInt32` / `fString`. Operator
   groups always use the type names (`__addMoneyInt32`).

   | C# type | Spelling |
   |---|---|
   | `int`, `uint`, `long`, `ulong`, `short`, `byte`, … | `Int32`, `UInt32`, `Int64`, `UInt64`, `Int16`, `Byte`, … |
   | `float`, `double`, `decimal`, `bool`, `char`, `string`, `object` | `Single`, `Double`, `Decimal`, `Boolean`, `Char`, `String`, `Object` |
   | `T[]`, `params T[]` | `<T>Array` (`Int32Array`) |
   | `T?` | `<T>OrNull` (`Int32OrNull`) |
   | `List<int>`, `Dictionary<string, int>` | `ListOfInt32`, `DictionaryOfStringInt32` |
   | `(int, string)` | `TupleOfInt32String` |
   | a type parameter `T` | `T` |

3. **The climb.** If two overloads still collide (`K(Numerics.Decimal d)` /
   `K(Other.Decimal d)`), the colliding ones qualify their parameter types one
   level at a time — containing types first, then namespace segments — until
   they differ: `kNumericsDecimal` / `kOtherDecimal`. Primitives never climb.
4. **Dead end.** Overloads nothing can separate (two types with the same full
   name from different assemblies, overloads differing only in generic arity,
   two equal `[Name]` pins) report `MS0003` (error) and get positional markers
   `$1`, `$2`.
5. **Members.** From the suffix `S` of an overload of type `T`:

   | Member | Name |
   |---|---|
   | selector | `by` + `T` + `S` (`byMoneyAmount`) |
   | factory | `from` + `S` (`fromAmount`), or `from` + `T` + `S` when an inherited static would break the static side |
   | method fast path | camelCase method name + `S` (`addXY`) |
   | operator fast path | `__` + operator name + `S` (`__addMoneyInt32`) |
   | module fast path | camelCase function name + `S` (`twiceX`) |

6. **Overrides and interface members take their root's name.** An `override`
   uses the name of the overload it overrides, at the first declaration of the
   chain, and an interface implementation the interface member's name, so
   `base` and derived types always agree: a derived class overriding one
   overload emits `override addX(…)` and, when it adds no overload, no
   dispatcher. A type that adds overloads to an inherited group emits an
   `override` dispatcher over the whole visible group that forwards the rest
   to `super`.

The rule is deterministic: the same source always gives the same names, so the
names are API. Renaming a C# parameter or type renames the generated member.

### Pinning a name with `[Name]`

`[Name("x")]` on **one overload** of a group replaces its suffix only
([attribute reference](attributes.md#namex)):

```csharp
public Money(decimal amount) { /* … */ }   // Money.fromAmount, Money.byMoneyAmount

[Name("cents")]
public Money(int value) { /* … */ }        // Money.fromCents, Money.byMoneyCents

[Name("cents")]
public Money Add(int value) => /* … */;    // money.addCents, beside the dispatcher money.add
```

The dispatcher keeps the C# name. The value must match
`^[A-Za-z_][A-Za-z0-9_]*$` (otherwise `MS0003`, error, and the generated suffix
is kept); a pin never climbs, so a collision with it moves the other overloads.
Put `[Name]` on the root of an override chain or on the interface member: an
implementer's different `[Name]` is ignored with an `MS0003` warning. On a
conversion operator, a single (non-overloaded) member, or an overload of an
`[External]`, `[JsCallable]` or `[PlainObject]` type, `[Name]` keeps renaming
the whole member.

### When a name is taken

Each target resolves the candidate names against the members a class already
has, in one order: user members, dispatchers, selectors, factories, method and
module fast paths, operator fast paths, then inherited members. A taken
candidate gets `$` appended until it is free, per suffix (`fromCents$` /
`byMoneyCents$`), and `MS0067` reports the rename. A user-written member that
would take a generated name it does not own — overriding an inherited fast
path, or redeclaring an inherited factory — is `MS0068` (error): rename the
member, or pin the overload with `[Name]`.

## Using the generated API from hand-written TypeScript

```ts
const a = Dimension.fromSize(3);       // a factory: the exact overload
const b = new Dimension(3);            // still works: the runtime fallback
a.addXY(1, 2);                         // a fast path: no runtime check
a.add(1, 2);                           // still works: the dispatcher
new Dimension(Dimension.byDimensionSize, 3); // TS2445: selectors are protected
```

A hand-written subclass that overrides a generated dispatcher (`add`) is
bypassed by generated code, which calls the fast paths: override the fast path
(`override addX(…)`) instead. A hand-written class implementing a generated
interface must implement its fast paths too (TS2420).

`samples/SampleIssueTracker/Planning/Domain/Estimate.cs` is a complete
scenario — constructor factories, a static field initialized through one, a
virtual group with an override, an extension group — with bun tests and a Node
run in `targets/js/sample-issue-tracker`.

## Diagnostics

| Code | Severity | When |
|---|---|---|
| `MS0003` | error / warning | the climb's dead end; an invalid `[Name]` suffix (error); an override's `[Name]` ignored for its root's, a `[Name]` on a constructor that is not overloaded (warning) |
| `MS0034` | error | `[JsonFactory]` on a method of an overloaded group (the dispatcher is shared by its siblings) |
| `MS0067` | warning | a generated name was taken and took `$` |
| `MS0068` | error | a user member takes a generated fast path or factory name it does not own |
| `MS0069` | warning | a type implements one generic interface with overload groups more than once (`C : IConv<int>, IConv<string>`); calls to those members keep runtime dispatch |

The [diagnostic catalog](../specs/001-project-baseline-evolution/baseline/diagnostic-catalog.md)
lists every message.

## Dart

The Dart target (experimental) uses the same candidates, claimed in Dart's one
member namespace per class:

| C# | Dart |
|---|---|
| `Dimension(int size)` / `Dimension(int width, int height)` | named constructors `Dimension.fromSize(…)` / `Dimension.fromWidthHeight(…)`; no selectors; a `: this(…)` chain redirects (`: this.fromXY(v, v)`) |
| `Add(int x)` / `Add(int x, int y)` | `addX(…)` / `addXY(…)`, no dispatcher (Dart has no overloading) |
| `new Dimension(3)`, `d.Add(1)`, `base.Add(1)` | `Dimension.fromSize(3)`, `d.addX(1)`, `super.addX(1)` |
| an overloaded operator group | one `operator` declaration (the primary) and an `MS0001` warning |
| `k.Scale(n: Next(), d: 2)` | `k.scaleDN(2, Run.next())`: one order-sensitive evaluation, passed in parameter order |
| `new T(b: F(), a: G())` | `MS0001`: two order-sensitive evaluations Dart would run in parameter order (#591) |
| `new Box<object>(5)` | `Box<Object>.fromValue(5)` |

Dart claims in one table per class, where a named constructor conflicts with
another named constructor or a static member but not with an instance member,
and every other member conflicts with any member of its name. TypeScript keeps
separate static and instance tables, so the two targets can claim the same
overload differently: an instance fast path `addX` beside a user static
`AddX()` takes `$` in Dart only.

Limitations of the experimental Dart target:

- a derived class's constructor emits no `super(...)` initializer, so it never
  selects a base named constructor (and a base with only named constructors
  does not compile as a superclass yet);
- a `: this(...)` chain whose constructor also has a body keeps the body
  without the chain (`MS0001` at the constructor): Dart redirecting
  constructors have no body;
- an overloaded operator group keeps only its primary operator (`MS0001`);
- the overloads of a `[PlainObject]` or branded type keep only the primary
  member (`MS0001`), declared under the name its call sites spell;
- a containerless module's fast paths are claimed per file, not across the
  files a consumer imports together;
- named arguments out of parameter order (in a call, a construction or a
  `: this(...)` chain) are passed in parameter order. With at most one
  order-sensitive evaluation among them (a call, a read, an `await`, a
  user-defined conversion; constants, lambdas and constant collections do
  not count) the order cannot differ from C# and the call stays inline
  (`k.scaleDN(2, Run.next())`); with two or more, `MS0001` reports the call,
  which then evaluates them in parameter order. The count is deliberately
  conservative: it also counts a receiver that is a computation and an
  extension call's receiver, which run first in both languages, so
  `h.Inner.Two(b: Next(), a: 3)` reports although its Dart order matches C#.
  Property getters with side effects are not covered in Dart (#592; the
  TypeScript target binds them in source order). Keeping C#'s
  order, without these false positives, is #591.

Gaps of the Dart prototype that predate spec 013 and also touch overloads:

- when a base's unnamed constructor has only optional parameters, a derived
  constructor compiles but runs the base with its defaults, silently (#572);
- a `params` argument is passed element by element, not as one list (#585);
- out-of-order named arguments of a non-overloaded call print as Dart named
  arguments (`name: value`) against positional parameters (#586);
- a generic method call drops its type arguments (#587).

## Migrating from 2.x

Upgrade the packages and their consumers together. A 3.0 consumer with a
project reference emits factory and fast-path calls into the producer, which a
producer built with 2.x lacks (`tsc` reports the missing member); a
metadata-only reference keeps the dispatcher calls.

1. **Hand-written `new T(…)` and dispatcher calls keep working.** Generated code
   no longer goes through them: patches or wrappers around a generated
   constructor's or dispatcher's runtime branches are bypassed. Override the
   fast path, not the dispatcher; implement the fast paths when you implement a
   generated interface; redeclare an inherited factory with `static override`
   under `noImplicitOverride`.
2. **Generated calls bind statically.** With `F(object a)` / `F(string a)`,
   `object o = "s"; F(o);` now calls `F(object)`, as C# does; 2.x's dispatcher
   chose `F(string)` by runtime type. Hand-written dispatcher calls keep the
   runtime choice.
3. **Renamed fast paths.** Type spellings are exact CLR names: `fInt` →
   `fInt32`, `gLong` → `gInt64`, `wLong` / `wULong` → `wInt64` / `wUInt64`,
   `pickLong` / `pickULong` → `pickInt64` / `pickUInt64`, `sumIntArray` →
   `sumInt32Array`, `hIntOrNull` → `hInt32OrNull`, `identityValue` → `identityT`,
   `now` → `nowDefault`; operator helpers `__addAcctLong` → `__addAcctInt64`
   (every `Int`, `Long`, `ULong`, `Bool`, `Float` spelling); overrides take the
   root's name (`areaSide` → `areaS`); 2.x's duplicate names split (`uInt32` /
   `uUInt32`, `lListOfInt32` / `lInt32Array`); a `From`-style fast path that a
   factory claims takes `$` (`fromAmount$`).
4. **`[Name]` on an overload pins its suffix.** In 2.x it renamed differently:
   an operator overload's `[Name("scaled")]` named the whole group (`__scaled`),
   now `__multiply` plus the fast path `__multiplyScaled`; a module or extension
   overload's `[Name("text")]` gave `textS`, now `twiceText`; an interface
   overload's `[Name("side")]` gave a separate `side(s)` signature, now
   `area(s)` plus the fast path `areaSide`; a class method overload's
   `[Name("addText")]` was ignored (`addS`), now `addAddText`. Conversion
   operators keep their `[Name]` (`__toInt`).
5. **`[JsonFactory]` next to a pinned overload is an error.** `[JsonFactory]
   From(string)` beside `[Name("num")] From(int)` was accepted and generated a
   descriptor calling the dispatcher; it is now `MS0034`. Give the JSON factory
   its own, non-overloaded name.
6. **Inherited factories.** A derived class inherits the base factories it
   does not redeclare; they return the base type.
7. **`stripInternal`.** Supported for a package consumed only by hand-written
   TypeScript; unsupported for a package consumed by other Metano packages,
   whose generated code calls `internal` factories and fast paths.
8. **Larger declarations.** Each constructor overload adds a selector, a
   selector signature and a factory; each method overload an addressable fast
   path; each overloaded module group exported fast paths in the barrel.
9. **New diagnostics.** `MS0067` (warning), `MS0068` (error) and `MS0069`
   (warning), above.
10. **Dart.** Overloaded constructors and methods now emit named constructors
    and fast paths instead of the primary overload alone; `[Name]` on an
    overload pins its suffix there too, and call sites spell a method's `[Name]`.
