// Declarative BCL → JavaScript mappings for the instance members of `long` and `ulong`,
// which are `bigint` values (spec 012 FR-037, FR-040).
//
// ToString(format) / ToString(format, provider): `formatInt64` implements .NET's
// invariant-culture standard formats; the provider is ignored (#308) and, as in every
// provider overload, not evaluated. ToString(provider) is the native `toString()`, which
// would otherwise read the provider as a radix; ToString() keeps the native call.
//
// CompareTo(long) / Equals(long): `compareInt64` returns `-1`, `0` or `1`, as C# does;
// `===` compares two `bigint` values exactly. `Equals(object)` goes through
// `objectEquals`, as `object.Equals(a, b)` does, since a `bigint` has no `equals` method.

using Metano.Annotations;

[assembly: MapMethod(
    typeof(long),
    nameof(long.ToString),
    JsTemplate = "formatInt64($this, $0)",
    RuntimeImports = "formatInt64",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.ToString),
    JsTemplate = "formatInt64($this, $0)",
    RuntimeImports = "formatInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.CompareTo),
    JsTemplate = "compareInt64($this, $0)",
    RuntimeImports = "compareInt64",
    WhenParameterTypes = [typeof(long)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Equals),
    JsTemplate = "($this === $0)",
    WhenParameterTypes = [typeof(long)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.ToString),
    JsTemplate = "formatInt64($this, $0)",
    RuntimeImports = "formatInt64",
    WhenParameterTypes = [typeof(string)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.ToString),
    JsTemplate = "formatInt64($this, $0)",
    RuntimeImports = "formatInt64",
    WhenParameterTypes = [typeof(string), typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.CompareTo),
    JsTemplate = "compareInt64($this, $0)",
    RuntimeImports = "compareInt64",
    WhenParameterTypes = [typeof(ulong)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Equals),
    JsTemplate = "($this === $0)",
    WhenParameterTypes = [typeof(ulong)]
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.Equals),
    JsTemplate = "objectEquals($this, $0)",
    RuntimeImports = "objectEquals",
    WhenParameterTypes = [typeof(object)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.Equals),
    JsTemplate = "objectEquals($this, $0)",
    RuntimeImports = "objectEquals",
    WhenParameterTypes = [typeof(object)]
)]

// Array.LongLength (spec 012 FR-036): the `number` length as a `bigint`.
[assembly: MapProperty(
    typeof(Array),
    nameof(Array.LongLength),
    JsTemplate = "BigInt($this.length)"
)]
[assembly: MapMethod(
    typeof(long),
    nameof(long.ToString),
    JsTemplate = "$this.toString()",
    WhenParameterTypes = [typeof(IFormatProvider)]
)]
[assembly: MapMethod(
    typeof(ulong),
    nameof(ulong.ToString),
    JsTemplate = "$this.toString()",
    WhenParameterTypes = [typeof(IFormatProvider)]
)]
