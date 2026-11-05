# Data model: Overload factories, selector symbols and direct call sites

The core (`Metano.Compiler`) computes the **candidate** names and the call-site decisions
(NFR-002); each target bridge resolves the claims and renders (DECIDED-18). Names are normative;
field order is indicative.

## 1. IR additions (`src/Metano.Compiler/`)

```csharp
/// <summary>
/// How one parameter type is spelled in an overload name (013 FR-002), captured
/// from the Roslyn symbol at extraction time, next to SignatureText, because the
/// IR type refs erase collections, delegates, unsigned primitives and containing
/// types (research R7). Leaf is the unqualified spelling ("Int32", "ListOfInt32",
/// "Decimal"); Qualifiers(depth) is the spelling climbed depth levels (FR-003).
/// </summary>
public sealed record IrNamingSpelling(
    string Leaf,
    IReadOnlyList<IrNamingComponent> Components // primitives: empty (they never climb)
);

/// <summary>A named component of a spelling and its own qualifier chain,
/// nearest first: containing types, namespace segments, then "Global".</summary>
public sealed record IrNamingComponent(string Name, IReadOnlyList<string> Chain);

/// <summary>
/// The candidate names OverloadNamePolicy computed for one overload of a
/// dispatched group (013 FR-001). Candidates only: the target bridge claims them
/// (DECIDED-18), so the emitted spelling may carry a `$`.
/// </summary>
public sealed record IrOverloadName(
    IrOverloadRef Root,         // the root overload this name belongs to (FR-025)
    string Suffix,              // "Amount", "WidthHeight", "NumericsDecimal", "Default"
    string? FastPathCandidate,  // methods / operators / module functions: "addXY", "__addMoneyInt32"; null on constructors
    bool QualifiedFactory       // FR-026: the factory candidate is from + TypeName + Suffix
);

/// <summary>A stable overload identity: the declaring type (for an interface root, the
/// constructed interface, DECIDED-30), the member's C# name, and the overload's
/// declaration ordinal in its root group.</summary>
public sealed record IrOverloadRef(IrTypeKey DeclaringType, string MemberName, int Ordinal);

// IrParameter gains:
public IrNamingSpelling? NamingSpelling { get; init; }   // null outside dispatched groups
// IrConstructorDeclaration and IrMethodDeclaration gain:
public IrOverloadName? OverloadName { get; init; }       // null: not part of a dispatched group (NFR-001)
public string? SignatureText { get; init; }              // constructors: "Money(decimal amount)" (D2)
// IrMethodDeclaration gains:
public bool IsOverride { get; init; }                    // emits `override` on the root's fast path (FR-030), with the root's mapped visibility
public bool IsHiding { get; init; }                      // `new`: a non-root candidate, claimed against inherited members (claim step 7)
// IrClassDeclaration gains:
public bool HasMultiplyConstructedInterface { get; init; } // DECIDED-30: runtime-dispatched root names, MS0069
// IrConstructorDeclaration gains, for a dispatched base call:
public IrOverloadRef? BaseOverload { get; init; }        // the statically resolved base overload (FR-017)
public IReadOnlyList<IrExpression>? BaseArguments { get; init; } // Roslyn's bound arguments, defaults explicit
```

- `OverloadName` is `null` whenever the member is not in a dispatched group — the no-churn rule
  of NFR-001 keys on it.
- `SignatureText` and `NamingSpelling` are read from Roslyn at extraction time, so neither target
  needs Roslyn. Roslyn spells `uint` as `UInt32` and `List<int>` as `List<int>` whatever the IR
  folds them to, so #475 is related but not a prerequisite.
- A struct's implicit parameterless constructor is not an `IrConstructorDeclaration`; the TS bridge
  adds its zero-argument branch (FR-027).

## 2. Direct call sites

```csharp
/// <summary>
/// A call Metano lowered to the Roslyn-resolved overload (013 FR-017, FR-018).
/// Target carries the overload's root identity, never a final string: the
/// target bridge resolves it through the declaring type's claim results
/// (DECIDED-18, M2). Produced only when the receiver, the overload and the
/// type arguments are static and the declaring type's IR is available
/// (source or project reference, FR-019); everything else keeps the dispatcher.
/// </summary>
public sealed record IrDirectOverloadCall(
    IrExpression? Receiver,             // null: static, module function or constructor factory
    IrOverloadRef Target,
    IrDirectCallKind Kind,              // Factory | Method | BaseMethod (super.) | Operator | ModuleFunction
    IReadOnlyList<IrArgument> Arguments, // Roslyn's bound form: expanded params as elements, normal form as one spread
    IReadOnlyList<IrTypeRef>? TypeArguments
) : IrExpression;

/// <summary>A method group converted to a delegate, resolved to one overload (FR-018).</summary>
public sealed record IrDirectOverloadGroupReference(
    IrExpression? Receiver,             // bound with .bind(receiver) when instance
    IrOverloadRef Target
) : IrExpression;
```

The constructor-initializer case (`: base(args)` → `super(Base.byBaseX, ...)`) is not an
expression: `IrConstructorDeclaration.BaseOverload` and `BaseArguments` record it, and the
constructor bridge renders the selector form in every branch that chains to that base overload.
`this(...)` chains keep today's inlining (DECIDED-7).

## 3. TypeScript rendering (`src/Metano.Compiler.TypeScript/Bridge/`)

| IR / C# | TS |
|---|---|
| constructor overload `OverloadName` | `protected static readonly by<TypeName><Suffix>: unique symbol = Symbol("<SignatureText>");` — first among the static members (FR-029) |
| every overload of a dispatched constructor | `constructor(overload: typeof T.by<TypeName><Suffix>, ...params);` before the retained plain signatures |
| the constructor implementation | guarded selector branches, then today's fallback branches, then the throw |
| constructor overload, concrete class | `static [override] from<Suffix>(...)` (or `from<TypeName><Suffix>`, FR-026) with the overload's #445 accessibility, `return new T(T.by…, ...)`; `override` when the name is inherited |
| method overload `OverloadName` | `<accessibility> [static] [override] [abstract] <fastPath>(...)`; an override takes the root's accessibility |
| a derived type's dispatcher over new overloads | `override` dispatcher with the base dispatcher's signatures plus its own; unmatched calls → `super.<dispatcher>(...args)` |
| interface method overload | the fast-path signature beside the dispatcher signatures (FR-031) |
| `IrDirectOverloadCall(Factory)` | `T.from<Suffix>(args)` |
| `IrDirectOverloadCall(Method)` | `<receiver>.<fastPath>(args)`, receiver evaluated once |
| `IrDirectOverloadCall(BaseMethod)` | `super.<fastPath>(args)` |
| `IrDirectOverloadCall(Operator)` | `T.__<op><Suffix>(left, right)` |
| `IrDirectOverloadCall(ModuleFunction)` | `<fastPath>(args)` plus a value import from the declaring module |
| `IrDirectOverloadGroupReference` | `<receiver>.<fastPath>.bind(<receiver>)` / `T.<fastPath>` |
| `BaseOverload` | `super(Base.by<BaseName><Suffix>, ...BaseArguments)` |

The claim results per type (`TsOverloadClaims`: overload ref → emitted names) are computed once
per declaring type from its IR and cached for the compilation, so every file spells a cross-file
call the way the declaring file emitted it.

## 4. Dart rendering (`src/Metano.Compiler.Dart/`, names only)

| IR | Dart |
|---|---|
| constructor overload `OverloadName` | the named constructor `T.from<Suffix>(...)` (no selector) |
| method overload `OverloadName` | the same candidate, claimed in Dart's single member namespace |
| `IrDirectOverloadCall(Factory)` | `T.from<Suffix>(args)` |
| `IrDirectOverloadCall(Method)` | `<receiver>.<fastPath>(args)` |

## 5. Diagnostics data

| Code | Raised by | Inputs |
|---|---|---|
| `MS0067` | each target's claim pass | the candidate, the claimant (user member or synthesized kind + name), the emitted name, the overload's location |
| `MS0067` reason 3 | each target's claim pass (step 7) | the non-root candidate, the inherited instance member |
| `MS0068` | each target's claim pass | the user member (instance or static), the inherited fast path or factory, its overload |
| `MS0069` | the core class extractor | the type, the interface, its constructions |
| `MS0003` | `OverloadNamePolicy` (dead end, FR-024) | the colliding overloads, the positional names |

## 6. Naming decision flow

```mermaid
flowchart TD
  A["Overload group (root group for virtual / interface members)"] --> B{"Pass 1: parameter-name suffixes distinct?"}
  B -- yes --> P["parameter-name suffixes"]
  B -- no --> C{"Pass 2: type spellings distinct?"}
  C -- yes --> T["type-spelling suffixes"]
  C -- no --> D["Climb the colliding overloads one level (every parameter, primitives excluded)"]
  D --> E{"Distinct?"}
  E -- yes --> L["climbed suffixes"]
  E -- "no, can climb" --> D
  E -- "no, dead end" --> X["MS0003 + $1 / $2"]
  P --> F["Candidates: by + Type + S, from + S (FR-026), fast paths"]
  T --> F
  L --> F
  X --> F
  F --> G["Target claim pass: user members, dispatchers, selectors, factories, fast paths, operators, inherited statics"]
  G -- "taken" --> H["S$ (per suffix) + MS0067; MS0068 if a user member would override a fast path"]
  G -- "free" --> I["emitted as computed"]
```

The parameterless overload's suffix is `Default` before pass 1 (DECIDED-5); it takes part in the
same distinctness checks.
