# HelloWorld

The smallest possible Metano sample. It shows how top-level C# statements are
transpiled into a TypeScript entry module.

## What You Will Find

- `Program.cs` — a top-level `Console.WriteLine("Hello, World!")`.
- `AssemblyInfo.cs` — enables whole-assembly transpilation with
  `[TranspileAssembly]`.
- `HelloWorld.csproj` — wires `Metano.Build.TypeScript` to generate TypeScript into the
  target package on build.

## Generated Code

The related TypeScript output is in
[targets/js/hello-world/src](../../targets/js/hello-world/src/):

- `program.ts` — the generated top-level module.

The target package lives in
[targets/js/hello-world](../../targets/js/hello-world/).

## Regenerate

```bash
dotnet build samples/HelloWorld/HelloWorld.csproj
```

