using Metano.Annotations;

[assembly: TranspileAssembly]

// No package of this name exists: the twin is transpiled into a folder of the
// sample-int64-conformance package (src/sample-checked-conformance), next to
// the default-context twin its fixtures run with. The identity only names
// the assembly's output; nothing imports it across packages.
[assembly: EmitPackage("sample-checked-conformance", Version = "workspace:*")]
