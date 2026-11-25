// Declarative BCL → JavaScript mapping for System.HashCode (#749): the `metano-runtime`
// `HashCode` class, an accumulator that hashes each value by the runtime's rules (the same
// ones the synthesized record `hashCode()` uses). `new HashCode()`, `Add(value)` and
// `ToHashCode()` lower to the class's constructor and `add` / `toHashCode`;
// `HashCode.Combine(v1 … v8)` lowers to the variadic static `HashCode.combine(...)`.
// Like .NET, the values are consistent within a process; unlike .NET's per-process random
// seed, they are also deterministic across runs.

using Metano.Annotations;

[assembly: ExportFromBcl(
    typeof(System.HashCode),
    ExportedName = "HashCode",
    FromPackage = "metano-runtime"
)]
