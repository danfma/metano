# SampleOperatorOverloading

This sample demonstrates C# operator overloads and value-object behavior using a
small `Money` domain type.

## What You Will Find

- `Money.cs` — a readonly record struct with arithmetic operators, `with`
  expressions, `BigInteger` cents, and currency checks.
- `Currency.cs` — currency enum used by the value object.
- `NoSameMoneyCurrencyException.cs` — domain exception emitted as a TypeScript
  error class.
- `CurrencyExceptions.cs` — exceptions deriving from BCL exceptions: .NET-style
  message composition (`ArgumentException` parameter suffix), a `Message`
  override two levels below the BCL base, and `: this(...)` chained
  constructors forwarding the inner exception as `cause`.
- `Program.cs` — top-level code that creates money, applies `+=`, and writes the
  result.

This sample is useful for inspecting generated static operator helpers,
exception lowering, `BigInteger` mapping, record-struct output, and arithmetic
expressions.

## Generated Code

The related TypeScript source is in
[targets/js/sample-operator-overloading/src](../../targets/js/sample-operator-overloading/src/):

- `money.ts`
- `currency.ts`
- `no-same-money-currency-exception.ts`
- `invalid-currency-code-exception.ts`, `unsupported-currency-exception.ts`,
  `currency-conversion-exception.ts`
- `program.ts`
- `index.ts`

The target package lives in
[targets/js/sample-operator-overloading](../../targets/js/sample-operator-overloading/).

## Regenerate And Run

```bash
dotnet build samples/SampleOperatorOverloading/SampleOperatorOverloading.csproj
cd targets/js/sample-operator-overloading
bun install
bun run build
```

