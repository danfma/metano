using Metano.Annotations;
using Metano.Annotations.TypeScript;

namespace SampleQueryableSqlite.Js.BunSqlite;

/// <summary>
/// Façade for the prepared <c>Statement</c> handle that
/// <see cref="Database.Query"/> / <see cref="Database.Prepare"/>
/// return. Declared via <c>[External]</c> because we never
/// instantiate it ourselves — the runtime hands the value back from
/// <c>db.query(...)</c>; we only describe its shape so the C# call
/// sites type-check. The TS emit relies on bun:sqlite's own type
/// definitions to resolve the actual runtime API.
/// </summary>
[External]
public interface Statement;

/// <summary>
/// Variadic helpers that lower to <c>stmt.all(...args)</c> /
/// <c>stmt.run(...args)</c> / <c>stmt.get(...args)</c>. Modeling
/// these as extension methods with <c>[Emit]</c> templates lets the
/// printer spread the C# <c>params</c> array as JS rest arguments
/// instead of passing it as a single positional value.
/// </summary>
public static class StatementExtensions
{
    [Emit("$0.all(...$1)")]
    public static extern object?[] All(this Statement stmt, params object?[] parameters);

    [Emit("$0.get(...$1)")]
    public static extern object? Get(this Statement stmt, params object?[] parameters);

    [Emit("$0.run(...$1)")]
    public static extern void Run(this Statement stmt, params object?[] parameters);
}
