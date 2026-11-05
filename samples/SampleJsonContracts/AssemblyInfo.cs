using Metano.Annotations;

[assembly: TranspileAssembly]
[assembly: EmitPackage("sample-json-contracts", Version = "workspace:*")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Metano.Tests")]
