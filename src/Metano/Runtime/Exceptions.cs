// Declarative BCL → JavaScript mappings for System.Exception members.
// Read at compile time by the Metano transpiler via DeclarativeMappingRegistry.
//
// BCL exception types lower to the JS `Error` built-in, so their members map onto
// Error's shape: `Message` already lines up by camelCase (`message`); the inner
// exception travels as the ES2022 `cause` option and the stack trace as `stack`.
// Both are optional on the JS side (`cause` is typed `unknown`), so the templates
// narrow them back to the C# contract — an `Error` (or string) or `null`. They use
// conditionals rather than `?? null`, which TypeScript flags as always nullish
// (TS2871) once a caller adds its own `?? fallback`.

using Metano.Annotations;

[assembly: MapProperty(
    typeof(Exception),
    nameof(Exception.InnerException),
    JsTemplate = "($this.cause instanceof Error ? $this.cause : null)"
)]
[assembly: MapProperty(
    typeof(Exception),
    nameof(Exception.StackTrace),
    JsTemplate = "($this.stack !== undefined ? $this.stack : null)"
)]
