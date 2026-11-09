# Contract: JSON for 64-bit integers

**Normative for**: FR-050–FR-057. Runtime files: `targets/js/metano-runtime/src/system/json/` (`types.ts`,
`numeric-converters.ts`, `converters.ts`, `number-from-string.ts`, `exact-json-number.ts`). Generator:
`src/Metano.Compiler.TypeScript/Transformation/Json/JsonTypeDescriptorBuilder.cs`,
`JsonTypeClassifier.cs`.

## Descriptor

```ts
/** `long` / `ulong` (and `nint` / `nuint`): exact digits on write, integer tokens on read. */
export type Int64Descriptor = {
  readonly kind: "int64";
  readonly numberType: "System.Int64" | "System.UInt64";
};
```

`TypeDescriptor` gains `Int64Descriptor`. The generator emits it for every 64-bit property, element,
dictionary value and root (FR-050), whatever the read options (and for a `[Branded]` / `[InlineWrapper]` over a 64-bit integer); `primitive` descriptors no longer carry
`numberType: "System.Int64"` / `"System.UInt64"`. A nullable 64-bit slot keeps the existing nullable
wrapping, and its error names ``System.Nullable`1[System.Int64]``.

`nint` / `nuint` (and a `[Branded]` / `[InlineWrapper]` over one) emit the same descriptor with `numberType` `System.Int64` / `System.UInt64` (issue #529). The union stays two names: System.Text.Json has no converter for `IntPtr` / `UIntPtr` (it throws `NotSupportedException`, measured on .NET 10 and 11), so there is no CLR error text to mirror, and a rejected value names the 64-bit sibling. The one place the CLR name survives is a value root's `typeName` (the type a JSON `null` error names), which stays `System.IntPtr` / `System.UIntPtr`.

Generated shape:

```ts
{ name: "id", jsonName: "id", descriptor: { kind: "int64", numberType: "System.Int64" } }
```

## Write

| Value | Written leaf | `JSON.stringify` text |
|---|---|---|
| `1152921504606846977n` | `JSON.rawJSON("1152921504606846977")` | `1152921504606846977` |
| `-9223372036854775808n` | `JSON.rawJSON("-9223372036854775808")` | `-9223372036854775808` |
| `18446744073709551615n` (`ulong`) | `JSON.rawJSON("18446744073709551615")` | `18446744073709551615` |
| `null` (nullable slot) | `null` | `null` |
| a safe-integer `number` (untyped caller) | `JSON.rawJSON("42")` | `42` |
| any other value | throws `JsonException` † | — |

The leaf comes from `writeExactNumber(value.toString(), () => Number(value))`, so an engine without
`JSON.rawJSON` writes the approximate `number` (009 FR-048 item 6). STJ writes the same text (R7).

## Read

`deserialize(tree, spec)` accepts, for an `int64` descriptor:

| Leaf | Result | STJ on the same JSON text |
|---|---|---|
| `bigint` within range | the value | — (no text) |
| `rawJSON` leaf `"9223372036854775807"` | `9223372036854775807n` | same (R7) |
| `rawJSON` leaf `"-0"` | `0n` | `0` (R7) |
| `rawJSON` leaf `"1.0"`, `"1e3"` | `JsonException` | `JsonException` (R7) |
| `rawJSON` leaf `"9223372036854775808"` | `JsonException` | `JsonException` (R7) |
| `number` `5` (a safe integer) | `5n` | `5` |
| `number` `1152921504606846976` (from `JSON.parse("1152921504606846977")`) | `JsonException` naming the exact read path (OQ-3 (d): beyond `Number.MAX_SAFE_INTEGER`) | `1152921504606846977` |
| `number` `1.5`, `NaN`, `Infinity` | `JsonException` | `JsonException` |
| `string` `"5"`, read options without `allowReadingFromString` | `JsonException` | `JsonException` (R7) |
| `string` `"5"`, with `allowReadingFromString` | `5n` | `5` (R7) |
| `string` `"+5"` for `ulong` | `JsonException` | `JsonException` (009 FR-048 grammar) |
| `null` in a non-nullable slot | `JsonException` | `JsonException` |

The error is `The JSON value could not be converted to System.Int64.` (or `System.UInt64`, or the
``System.Nullable`1[…]`` name), with the path suffix the runtime already appends.

## Exact text path (#457, FR-053)

A root spec whose transitive closure contains an `int64` descriptor is read from the JSON text by #457's
exact path, `deserializeText` (slice 012h). The `int64` converter reads the exact token text that path hands
it with the `rawJSON` rules above. #457 shipped its own leaf (OQ-3): `JsonNumberText`, read by
`exactNumberTextOf` beside `rawJsonTextOf`. The tree keeps a number as that leaf by the closure's needs
(`specNumberTextLeaves`, `exact-number-reads.ts`):

| Closure reaches | Leaves (`NumberTextLeaves`) | Leaves in the tree | Tokenizer gate (else `JSON.parse`) |
|---|---|---|---|
| no `decimal`, no `int64` | — | none | never |
| `decimal` only | `inexact` | `JsonNumberText` (copied text) for more than 15 significant digits | a run of 16 `[0-9.]` characters |
| an `int64` | `inexactOrNonInteger` | that, and `JsonNonIntegerNumber` (its double, no text) for any other fraction or exponent | that, or a digit followed by `.` / `e` / `E` |

| Text (`deserializeText`) | `long` | `ulong` | STJ (.NET 10) |
|---|---|---|---|
| `9007199254740993` | `9007199254740993n` | `9007199254740993n` | same |
| `-9223372036854775808` | `-9223372036854775808n` | `JsonException` | same |
| `1.0`, `1e3`, `1E0`, `-0.0` | `JsonException` | `JsonException` | `JsonException` |
| `-0` | `0n` | `JsonException` | same |
| `{"$type":1.0,…}` (int tag, polymorphic base with an `int64` in its closure) | `JsonException` | — | `JsonException` |

The int64 converter rejects a `JsonNonIntegerNumber` (it has no integer token), and so does an int tag. A
`decimal` reads a short fraction from its double, as in a decimal-only spec. Every other converter of an exact
tree sees its value as `JSON.parse` builds it: an `array`, `hashSet`, `map` or `ref` converter gets its own
value with its leaf read as a `number` (`numberOfTreeValue`) and hands each child to a child converter, which
reads that child in turn; a custom converter and every other built-in one (`primitive`, including an
`object` slot, `branded`, `enum`, `numericEnum`, `temporal`, `bytes`) get the whole subtree with doubles
(`treeWithDoubles`), so a `double` member of the same contract still reads `1.5`, a `[Branded]` factory never
receives a leaf and an enum error prints the parsed value.

## Numbers from strings (009 FR-048 item 4)

`readNumberText(text, "System.Int64")` returns a `bigint` (the reader's return type becomes
`number | bigint | undefined`). The grammar and the range checks are unchanged (`number-from-string.ts`
L81-92, L149-154).

## Dictionary keys

| C# | Write | Read accepts | Read rejects |
|---|---|---|---|
| `Dictionary<long, V>` | `{"9223372036854775807":…,"-3":…}` (`key.toString()`) | `"5"`, `"+5"`, `"05"`, `"-3"` | `" 5"`, `"1e3"`, `"5.0"`, out of range |
| `Dictionary<ulong, V>` | `{"18446744073709551615":…}` | `"5"`, `"05"` | `"-0"`, `"+5"`, out of range |

Keys are read with the 009 FR-048 integer grammar whatever the read options (R7). The map descriptor's key
is an `int64` descriptor, and the map converter calls the key converter's dedicated key reader (a key is
always a property-name string) instead of the value reader.

## `object` slots

A `bigint` that reaches the converter of an `object`-typed member (or any descriptor that passes values
through) is written through `writeExactNumber` (FR-056). On read, an `object` slot keeps today's behavior
(the JSON value as parsed).
