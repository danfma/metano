// Declarative BCL → JavaScript mappings for the constructors of `System.Numerics.BigInteger`,
// which is a `bigint` (a type with no constructor of its own at runtime: an unmapped
// `new BigInteger(1)` would print `new Object(1)`).
//
// The integral sources convert exactly: `BigInt(x)` takes a `number` (an `int`, a `uint`) or
// a `bigint` (a `long`, a `ulong`). `float` and `double` truncate toward zero and throw .NET's
// `OverflowException` for NaN and the infinities, `decimal` truncates (`bigIntegerFromDouble`,
// `bigIntegerFromDecimal`). The default constructor is zero. The byte-array and span
// overloads, which read a little-endian two's complement value, stay unmapped.

using System.Numerics;
using Metano.Annotations;

[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(int)]
)]
[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(uint)]
)]
[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(long)]
)]
[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "BigInt($0)",
    WhenParameterTypes = [typeof(ulong)]
)]
[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "bigIntegerFromDouble($0)",
    RuntimeImports = "bigIntegerFromDouble",
    WhenParameterTypes = [typeof(double)]
)]
[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "bigIntegerFromDouble($0)",
    RuntimeImports = "bigIntegerFromDouble",
    WhenParameterTypes = [typeof(float)]
)]
[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "bigIntegerFromDecimal($0)",
    RuntimeImports = "bigIntegerFromDecimal",
    WhenParameterTypes = [typeof(decimal)]
)]
[assembly: MapMethod(
    typeof(BigInteger),
    MapMethodAttribute.ConstructorName,
    JsTemplate = "0n",
    WhenParameterTypes = new Type[0]
)]

// ─── Constants and state tests (#686) ──────────────────────
[assembly: MapProperty(typeof(BigInteger), nameof(BigInteger.Zero), JsTemplate = "0n")]
[assembly: MapProperty(typeof(BigInteger), nameof(BigInteger.One), JsTemplate = "1n")]
[assembly: MapProperty(typeof(BigInteger), nameof(BigInteger.MinusOne), JsTemplate = "BigInt(-1)")]
[assembly: MapProperty(
    typeof(BigInteger),
    nameof(BigInteger.IsZero),
    JsTemplate = "($this === 0n)"
)]
[assembly: MapProperty(typeof(BigInteger), nameof(BigInteger.IsOne), JsTemplate = "($this === 1n)")]
[assembly: MapProperty(
    typeof(BigInteger),
    nameof(BigInteger.IsEven),
    JsTemplate = "($this % 2n === 0n)"
)]
