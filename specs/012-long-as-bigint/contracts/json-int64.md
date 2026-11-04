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

When #457 lands, a root spec whose transitive closure contains an `int64` descriptor is read from the JSON
text by #457's exact path. The `int64` converter reads the exact token text that path hands it with the
`rawJSON` rules above. OQ-3 asks #457 to hand it as a `JSON.rawJSON`-compatible leaf, so that
`rawJsonTextOf` stays the one read path for `decimal` and 64-bit values.

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
