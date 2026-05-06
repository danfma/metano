namespace Metano.Annotations;

/// <summary>
/// Marks a method's lambda parameters as candidates for compile-time
/// expression-tree capture. When applied, the transpiler walks each
/// lambda body via the Roslyn semantic model and emits an IR expression
/// tree alongside the runtime closure — landing in the descriptor's
/// optional <c>queryable</c> field (<c>QueryableMeta</c>) on the
/// TypeScript-side descriptor.
///
/// <code>
/// public static class IQueryableExt
/// {
///     [Queryable]
///     public static IEnumerable&lt;T&gt; Where&lt;T&gt;(
///         this IEnumerable&lt;T&gt; source,
///         Func&lt;T, bool&gt; predicate
///     ) =&gt; throw new NotSupportedException();
/// }
///
/// // Call site: lambda body is captured AND emitted as an expression
/// // tree, so an IQueryable provider can translate to SQL/GraphQL
/// // without parsing closure source.
/// items.Where(u =&gt; u.Age &gt; 18);
/// </code>
///
/// <para>
/// Targets:
/// </para>
/// <list type="bullet">
///   <item><b>Method</b> — every lambda parameter on the method captures
///   its expression tree.</item>
///   <item><b>Parameter</b> — only the annotated parameter captures its
///   tree; siblings stay as opaque closures.</item>
/// </list>
///
/// <para>
/// Without this attribute, lambdas keep the legacy LINQ-to-Objects
/// behavior — closures with no expression tree. Providers that need
/// introspection check for the <c>queryable</c> field before falling
/// back to runtime walking.
/// </para>
///
/// <para>
/// Implicit capture (without the attribute) also kicks in when the C#
/// parameter type is <c>Expression&lt;Func&lt;…&gt;&gt;</c> from the BCL,
/// matching how .NET LINQ-to-SQL providers signal "I need the tree".
/// </para>
///
/// <para>
/// Captured trees follow the MVP shape declared in
/// <c>metano-runtime/system/linq/expr-tree.ts</c>: param / literal /
/// member / call / binary / unary / conditional / lambda / new. Bodies
/// outside that subset emit a diagnostic so the user can refactor or
/// drop <c>[Queryable]</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter)]
public sealed class QueryableAttribute : Attribute;
