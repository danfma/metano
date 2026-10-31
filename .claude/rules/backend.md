---
paths:
  - "**/*.cs"
  - "**/*.csproj"
---

# Backend rules (.NET / C#)

Reviewed by the `backend-reviewer` agent; structure across layers by the `architecture-reviewer`.

## Scope in this repository

- These are the maintainer's general backend rules. Metano-specific conventions in `CLAUDE.md` take precedence when they conflict.
- The Ports & Adapters section applies as a design principle. Metano's project layout (`Metano`, `Metano.Compiler`, `Metano.Compiler.TypeScript`, `Metano.Compiler.Dart`, `Metano.Build`) is defined by `CLAUDE.md`: the core is target-agnostic and each language target is its own project.
- Generated code and test fixtures that intentionally mirror user C# (inline sources in tests, `samples/`) are exempt from naming and value-object rules when the shape under test requires it.

## Principles

- Follow SOLID and Clean Code everywhere: small classes and methods, one responsibility, dependence on abstractions, no hidden side effects.
- Semantic, business names. Avoid generic and technical names: `Manager`, `Helper`, `Util`, `Utils`, `Data`, `Info`, `Processor`, `Common`, `Misc`, `Base` (outside real inheritance) and `Service` as an empty suffix.
  - Good: `NarrationSynthesizer`, `ContinuityBible`, `ShotPromptComposer`.
  - Bad: `AudioService`, `ProjectManager`, `PromptHelper`.

## Architecture: Ports & Adapters

- The first level may be technical, and only the first level may: `<Product>.Domain`, `<Product>.Application`, `<Product>.Hosting`, and one project per adapter, named after what it adapts (e.g. `<Product>.Providers.ElevenLabs`, `<Product>.Storage.FileSystem`).
- Below the first level, namespaces are business-only: `Studio.Domain.Projects.Timeline`, `Studio.Application.Narration`. Never `Entities`, `Services`, `Interfaces`, `Models`, `Dtos`, `Helpers`, `Extensions` as groupings.
- Avoid shallow namespaces. Create sub-namespaces when there is specialization or a group (e.g. `Projects.Timeline.Tracks`, `Projects.Timeline.Clips`).
- Dependency direction: `Domain` depends on nothing; `Application` depends on `Domain` and defines the ports; adapters depend on `Application`/`Domain`; `Hosting` only composes (composition root).
- Ports are named after the capability, not the technology: `ISpeechSynthesizer`, not `IElevenLabsClient`. The adapter carries the technology in its name: `ElevenLabsSpeechSynthesizer`.
- Types shared with the frontend (transpiled by Metano) stay free of infrastructure.

## Primitive obsession: value objects

- Concepts with their own rules or meaning become `readonly record struct`, not loose `string`/`int`/`Guid`.
- Creation only through a static factory with validation (`From`, or a semantic name such as `FromSeconds`). Private constructor.
- Implicit conversion only to the raw type: `public static implicit operator TRaw(TSelf self)`. Never from the raw type to the value object.
- `default(TSelf)` is invalid: do not use it, and reject it at boundaries (deserialization, API input).

```csharp
public readonly record struct ProjectName
{
    private ProjectName(string value) => Value = value;

    public string Value { get; }

    public static ProjectName From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new ProjectName(value.Trim());
    }

    public static implicit operator string(ProjectName self) => self.Value;
}
```

## Variables

- Declare with `var`, always with an assigned value.
- Prefer not to mutate after declaration.
- Conditional instantiation goes into a method or factory (e.g. a `switch` expression in a method), not an `if` that assigns a variable declared earlier.

```csharp
// Bad
IFrameSource source;
if (file.IsVariableFrameRate) source = new PtsFrameSource(file);
else source = new ConstantFrameSource(file);

// Good
var source = FrameSourceFor(file);
```

## Formatting and readability

- Every block (`if`, `for`, `foreach`, `while`, `switch`, `try`, block `using`, `lock`) is separated by a blank line above and below, except when it is the first or last item in its scope.
- A declaration or call spanning more than one line is also isolated by a blank line above and below, except next to the scope's brace.

```csharp
public Timeline Build(Script script)
{
    var shots = script.Shots;

    if (shots.Count == 0)
    {
        return Timeline.Empty;
    }

    var timeline = Timeline.Start(
        script.Name,
        script.FrameRate,
        script.Narration);

    return timeline.WithShots(shots);
}
```

## Member order

- Top-down reading: fields, constructors, properties, methods.
- Static fields preferably first; static methods preferably last (preference, not a rule).
- Fields only at the top of the class. Group `readonly` fields together and mutable fields together, no `#region`.
