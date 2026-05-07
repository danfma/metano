using Metano.Annotations;

namespace SampleQueryableSqlite.Js.BunSqlite;

/// <summary>
/// C# façade for Bun's built-in <c>Database</c> class
/// (<c>import { Database } from "bun:sqlite"</c>). Bun ships SQLite as
/// part of its runtime so there is no npm package version to pin —
/// the <c>[Import]</c> attribute simply tells Metano to emit the
/// import line and skip transpilation of this type.
///
/// <para>
/// Instance method bodies throw on the C# side because the real
/// implementation lives in the JS runtime. Metano replaces every
/// call site with the corresponding JS expression at emit time.
/// </para>
/// </summary>
[Import(name: "Database", from: "bun:sqlite")]
public class Database
{
    public Database(string filename) { }

    [Name("query")]
    public Statement Query(string sql) => throw new NotSupportedException();

    [Name("prepare")]
    public Statement Prepare(string sql) => throw new NotSupportedException();

    [Name("exec")]
    public void Exec(string sql) => throw new NotSupportedException();

    [Name("close")]
    public void Close() => throw new NotSupportedException();
}
