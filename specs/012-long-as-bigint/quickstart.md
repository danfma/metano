# Quickstart: `long` and `ulong` as native `bigint`

How to see the feature work once its slices land. Commands follow CLAUDE.md (`dotnet run` for TUnit, Bun
for JavaScript).

## 1. A 64-bit id survives

```csharp
using Metano.Annotations;

[assembly: TranspileAssembly]

namespace Demo;

public record Snowflake(long Id, ulong Mask, long? Parent)
{
    public const long Epoch = 1288834974657L;

    public long Timestamp => (Id >> 22) + Epoch;

    public static long Next(long last) => checked(last + 1);
}
```

```ts
export class Snowflake {
  static readonly epoch: bigint = 1288834974657n;

  constructor(readonly id: bigint, readonly mask: bigint, readonly parent: bigint | null) { }

  get timestamp(): bigint {
    return BigInt.asIntN(64, (this.id >> 22n) + Snowflake.epoch);
  }

  static next(last: bigint): bigint {
    return checkedInt64(last + 1n);
  }
  // equals / hashCode / with as today; hashCode hashes each bigint with .NET's formula
}
```

```ts
const s = new Snowflake(1152921504606846977n, 18446744073709551615n, null);
s.id === 1152921504606846977n;          // true: no digit lost
Snowflake.next(9223372036854775807n);   // throws OverflowException
```

## 2. Arithmetic, conversions and BCL calls

| C# | TypeScript |
|---|---|
| `long Add(long a, int b) => a + b;` | `return BigInt.asIntN(64, a + BigInt(b));` |
| `int Low(long a) => (int)a;` | `return Number(BigInt.asIntN(32, a));` |
| `double Ratio(long a) => a / 3.0;` | `return Number(a) / 3.0;` |
| `long Half(long a) => a / 2;` | `return a / 2n;` |
| `long Div(long a, long b) => a / b;` | `return divInt64(a, b);` |
| `ulong Rot(ulong x, int n) => (x << n) \| (x >> (64 - n));` | `return BigInt.asUintN(64, x << BigInt(n & 63)) \| (x >> BigInt((64 - n) & 63));` |
| `string Show(long a) => $"{a:N0}";` | `` return `${formatInt64(a, "N0")}`; `` |
| `long Parse(string s) => long.Parse(s);` | `return parseInt64(s);` |
| `long Total(long[] xs) => xs.Sum();` | `return linq(xs, sumInt64());` |

## 3. JSON

```csharp
[JsonSerializable(typeof(Snowflake))]
public partial class DemoJson : JsonSerializerContext;
```

```ts
const tree = JsonSerializer.serialize(s, DemoJson.default.snowflake);
JSON.stringify(tree);
// {"id":1152921504606846977,"mask":18446744073709551615,"parent":null}   (byte-identical to STJ)
```

Reading that text back is exact through #457's exact path (FR-053). A tree produced by a plain
`JSON.parse` already rounded `1152921504606846977` to `1152921504606846976`.

## 4. Run the checks

```sh
dotnet run --project tests/Metano.Tests/                                    # goldens + diagnostics
dotnet run --project tests/Int64Reference/                                  # regenerate the .NET fixtures
cd targets/js/metano-runtime && bun test test/system/int64.test.ts          # helpers
cd targets/js/sample-int64-conformance && bun run build && bun test         # transpiled twin vs fixtures
cd targets/js/sample-issue-tracker && bun run build && bun test             # 64-bit id round-trips
cd targets/js/metano-runtime && bun run bench:int64                         # FR-066 benchmark (manual)
```

## 5. What to expect in a 2.x consumer

- TS errors where hand-written code passes a `number` to a generated `bigint` parameter: pass `5n` or
  `BigInt(n)`.
- `TypeError: Do not know how to serialize a BigInt` where a transpiled object goes straight into
  `JSON.stringify`: serialize through the generated context.
- `MS0065` on `[Import]` / `[Emit]` declarations with 64-bit types: the JavaScript side now exchanges
  `bigint`.
