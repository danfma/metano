namespace MetaSharp.Annotations;

/// <summary>
/// Marks a type as <em>declaration-only</em>: it exists in the C# type system so user
/// code can reference it (parameters, return types, lambdas), but the transpiler does
/// NOT generate a .ts file for it AND does NOT emit any import for it in consumers.
///
/// Useful for ambient types that describe the structural shape of an external library
/// without naming an actual export. The canonical example is a callback context type
/// from a JS framework:
///
/// <code>
/// [Import(name: "Hono", from: "hono")]
/// public class Hono
/// {
///     public void Get(string path, Action&lt;IHonoContext&gt; handler) =&gt; throw new();
/// }
///
/// [NoEmit]
/// public interface IHonoContext
/// {
///     IHonoContext Text(string text);
/// }
/// </code>
///
/// In the generated TS, <c>app.get("/", c =&gt; c.text("Hello"))</c> works because TS
/// infers <c>c</c>'s type structurally from the real <c>hono</c> .d.ts. MetaSharp never
/// needs to emit <c>IHonoContext</c> as a TS interface or import it from anywhere.
///
/// Contrast with <see cref="NoTranspileAttribute"/>: <c>[NoTranspile]</c> excludes the
/// type from discovery entirely (the compiler pretends it doesn't exist), while
/// <c>[NoEmit]</c> keeps the type discoverable so other transpiled code can reference it
/// — only the emission step is skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface)]
public sealed class NoEmitAttribute : Attribute;
