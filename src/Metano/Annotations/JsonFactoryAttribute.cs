namespace Metano.Annotations;

/// <summary>
/// Marks the static method a <c>[Branded]</c> / <c>[InlineWrapper]</c> type's generated
/// <c>SerializerContext</c> descriptor calls to turn the raw JSON primitive into the
/// branded value. Without it the descriptor calls the companion's non-validating
/// <c>create</c>; with it the descriptor calls this method, so the C# validation it
/// performs (<c>From</c>) also runs when JSON is deserialized in TypeScript.
/// <para>
/// A valid factory is an ordinary static method (not an operator), emitted for the
/// target, with exactly one parameter of the wrapper's underlying type, returning the
/// containing type, neither generic nor overloaded, and the only <c>[JsonFactory]</c>
/// of its type. Exceptions it throws propagate unchanged out of <c>deserialize</c>.
/// Anything else, or the attribute on a non-branded type, is <c>MS0034</c>.
/// </para>
/// <para>
/// TypeScript-only: Metano ships no System.Text.Json converter that honors it.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class JsonFactoryAttribute : Attribute;
