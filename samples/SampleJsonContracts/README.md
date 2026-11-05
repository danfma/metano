# SampleJsonContracts

The System.Text.Json conformance sample (spec 009, slice S8): one C# project whose types and
`JsonSerializerContext`s are transpiled by Metano and re-serialized by real source-generated STJ
contexts in the test suite.

- `samples/SampleJsonContracts/` — the model types (clip hierarchies, interface and `[PlainObject]`
  bases, closed generics, value roots, Web and naming-policy contexts, member options) plus seven
  contexts the transpiler lowers to `SerializerContext` files.
- `targets/js/sample-json-contracts/` — the generated TypeScript package. Its bun test
  (`test/stj-conformance.test.ts`) and the TUnit test
  (`tests/Metano.Tests/Json/StjConformanceFixtureTests.cs`) both load the same checked-in manifest,
  `targets/js/sample-json-contracts/test/fixtures/stj/cases.json`, and run each row against their
  own side: byte equality for write/roundtrip rows, the expected runtime type for read/roundtrip
  rows, and the thrown class + message prefix for error rows. A row without a builder fails that
  side; a builder without a row fails too.

The manifest carries the divergences listed in spec 009 "Documented divergences" as `divergent`
rows: the STJ text lives in `stjJson`, the TypeScript text in `json`. Message rows compare
prefixes, because STJ appends the `Path` suffix (DECIDED-5) and names namespaced types in some
messages (DECIDED-23 g).

Regenerate the STJ texts after a .NET upgrade with `METANO_UPDATE_STJ_FIXTURES=1 dotnet run
--project tests/Metano.Tests/ --no-build -- --treenode-filter "/*/*/StjConformanceFixtureTests/*"`
and review the diff deliberately.
