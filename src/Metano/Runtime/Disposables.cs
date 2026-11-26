// Declarative BCL → JavaScript mapping for System.IDisposable (#800, spec FR-111): the
// `metano-runtime` interface `IDisposable { dispose(): void }`. `Dispose()` lowers to
// `dispose()` by the ordinary member-name policy; `using` lowers to `try`/`finally`.
//
// Convention: an interface bound by [ExportFromBcl] to `metano-runtime` is declared by the
// runtime, which also exports `<ExportedName>$runtimeType` (an `InterfaceType`) beside it; an
// interface a user maps to another package keeps its 3.2.0 behaviour. The compiler then
// treats the interface like one of a referenced [EmitPackage] library: it stays in
// `implements` / `extends`, in the descriptors' `interfaces`, and `is` / `as` test it
// nominally.

using Metano.Annotations;

[assembly: ExportFromBcl(
    typeof(System.IDisposable),
    ExportedName = "IDisposable",
    FromPackage = "metano-runtime"
)]
