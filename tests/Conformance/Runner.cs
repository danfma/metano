using System.Globalization;
using System.Reflection;
using Metano.Annotations;

namespace Conformance;

[Ignore]
public static class Runner
{
    public static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var lines = new List<string>();
        foreach (
            var t in typeof(Runner)
                .Assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Probes"))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
        )
        {
            foreach (
                var m in t.GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
                    )
                    .Where(m => m.GetParameters().Length == 0 && IsRow(m.ReturnType))
            )
            {
                var name = t.Name + "." + char.ToLowerInvariant(m.Name[0]) + m.Name.Substring(1);
                string r;
                try
                {
                    r = "= " + Result(m.Invoke(null, null));
                }
                catch (TargetInvocationException e)
                {
                    r = Thrown(e.InnerException!);
                }
                catch (Exception e)
                {
                    r = Thrown(e);
                }
                lines.Add(name + " " + r.Replace("\r", "\\r").Replace("\n", "\\n"));
            }
        }
        lines.Sort(StringComparer.Ordinal);
        foreach (var l in lines)
            Console.WriteLine(l);
    }

    /// <summary>A row returns its text, or a task of it (an async row, awaited here).</summary>
    private static bool IsRow(Type returnType) =>
        returnType == typeof(string) || returnType == typeof(Task<string>);

    private static string? Result(object? value) =>
        value is Task<string> task ? task.GetAwaiter().GetResult() : (string?)value;

    private static string Thrown(Exception e) => "THROWS " + e.GetType().Name + ": " + e.Message;
}
