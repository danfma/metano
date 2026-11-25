// Declarative BCL → JavaScript mapping for System.IDisposable (#800, spec FR-111): the
// `metano-runtime` interface `IDisposable { dispose(): void }`. `Dispose()` lowers to
// `dispose()` by the ordinary member-name policy; `using` lowers to `try`/`finally`.
//
// Convention: an interface bound by [ExportFromBcl] is declared by the target runtime, which
// also exports `<ExportedName>$runtimeType` (an `InterfaceType`) beside it. The compiler then
// treats the interface like one of a referenced [EmitPackage] library: it stays in
// `implements` / `extends`, in the descriptors' `interfaces`, and `is` / `as` test it
// nominally.

using Metano.Annotations;

[assembly: ExportFromBcl(
    typeof(System.IDisposable),
    ExportedName = "IDisposable",
    FromPackage = "metano-runtime"
)]
