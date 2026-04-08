namespace MetaSharp.Annotations;

/// <summary>
/// Promotes a local variable inside a <see cref="ModuleEntryPointAttribute"/> method
/// body to a module-level export of the generated TypeScript file. Use this for the
/// "construct an instance, configure it, then export it as the module's main artifact"
/// pattern common to JS frameworks (e.g., Hono apps, Express apps, server entry points).
///
/// <para>Example:</para>
/// <code>
/// [ExportedAsModule]
/// public static class Program
/// {
///     [ModuleEntryPoint, ExportVarFromBody("app", AsDefault = true, InPlace = false)]
///     public static void Main()
///     {
///         var app = new Hono();
///         app.Get("/", c =&gt; c.Text("Hello"));
///     }
/// }
/// </code>
///
/// <para>Generates:</para>
/// <code>
/// import { Hono } from "hono";
///
/// const app = new Hono();
/// app.get("/", c =&gt; c.text("Hello"));
///
/// export default app;
/// </code>
///
/// <para><strong>Note:</strong> The transpiler logic that consumes this attribute is
/// not yet implemented. Currently this attribute exists only as a declaration so that
/// consumer code referencing it compiles. End-to-end behavior comes in a follow-up
/// commit.</para>
/// </summary>
/// <param name="name">Name of the local variable inside the entry point body to export.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ExportVarFromBodyAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>
    /// When true, the variable is exported as the module's default export
    /// (<c>export default name;</c>). When false, it becomes a named export.
    /// </summary>
    public bool AsDefault { get; init; }

    /// <summary>
    /// When true, the export modifier is folded into the declaration site
    /// (<c>export const app = new Hono();</c>). When false, the export is emitted as
    /// a separate statement at the end of the file (<c>export default app;</c>),
    /// leaving the original declaration untouched.
    /// </summary>
    public bool InPlace { get; init; }
}
