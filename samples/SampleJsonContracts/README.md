# SampleJsonContracts

The System.Text.Json conformance sample (spec 009, slice S8): one C# project whose types and
`JsonSerializerContext`s are transpiled by Metano and run, unchanged, through their real
source-generated STJ `Default` instances in the test suite. Type names follow the
`contracts/wire-format.md` fixture ids.

- `samples/SampleJsonContracts/` — the model types (clip hierarchies, interface and `[PlainObject]`
  bases, closed generics, value roots, Web and naming-policy contexts, member options) plus seven
  contexts the transpiler lowers to `SerializerContext` files.
- `targets/js/sample-json-contracts/` — the generated TypeScript package. Its bun test
  (`test/stj-conformance.test.ts`, type-checked by `bun run build`) and the TUnit test
  (`tests/Metano.Tests/Json/StjConformanceFixtureTests.cs`) both load the same checked-in manifest,
  `targets/js/sample-json-contracts/test/fixtures/stj/cases.json`, and run each row against their
  own side: byte equality for write/roundtrip rows, the expected runtime type and field values for
  read/roundtrip rows, and the thrown class plus the whole message for error rows. Every row but a
  read error needs a builder on each side it runs on; a missing builder fails, and so does a
  builder without a row.

The manifest records the divergences listed in spec 009 "Documented divergences" as rows with a
`divergence` note. A written-text divergence keeps the STJ text in `stjJson` and the TypeScript
text in `json`, and says with `crossReadable` whether each side reads the other's text (`true`
for cosmetic differences such as V17 and WEB7, `false` for V11, V12 and V14). A message
divergence records `stjMessage` and `tsMessage`. Messages are compared after removing STJ's
`Path: …` suffix (DECIDED-5); only `namespaceInsensitive` rows (STJ's polymorphic messages,
DECIDED-23 g) also drop the `SampleJsonContracts.` namespace. `stjPath` pins STJ's JSON path where
its message names only the declaring type (DECIDED-23 c).

The `biome.json` override that turns off `noUnusedFunctionParameters` for the generated `src/`
covers the unused `p` parameter of the throwing factories of abstract and interface bases; it
goes away with the emitter follow-up that drops that parameter.

Regenerate the STJ texts after a .NET upgrade with `METANO_UPDATE_STJ_FIXTURES=1 dotnet run
--project tests/Metano.Tests/ --no-build -- --treenode-filter "/*/*/StjConformanceFixtureTests/*"`
and review the diff deliberately. Error messages are not regenerated.
