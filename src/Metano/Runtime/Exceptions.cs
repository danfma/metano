// Declarative BCL → JavaScript mappings for System.Exception members.
// Read at compile time by the Metano transpiler via DeclarativeMappingRegistry.
//
// BCL exception types lower to the JS `Error` built-in, so their members map onto
// Error's shape: `Message` already lines up by camelCase (`message`); the inner
// exception travels as the ES2022 `cause` option and the stack trace as `stack`.
// Both are optional on the JS side (`cause` is typed `unknown`), so the templates
// narrow them back to the C# contract — an `Error` (or string) or `null`.

using Metano.Annotations;

[assembly: MapProperty(
    typeof(Exception),
    nameof(Exception.InnerException),
    JsTemplate = "(($this.cause as Error | undefined) ?? null)"
)]
[assembly: MapProperty(
    typeof(Exception),
    nameof(Exception.StackTrace),
    JsTemplate = "($this.stack ?? null)"
)]
