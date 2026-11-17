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
                    .Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(string))
            )
            {
                var name = t.Name + "." + char.ToLowerInvariant(m.Name[0]) + m.Name.Substring(1);
                string r;
                try
                {
                    r = "= " + (string?)m.Invoke(null, null);
                }
                catch (TargetInvocationException e)
                {
                    r =
                        "THROWS "
                        + e.InnerException!.GetType().Name
                        + ": "
                        + e.InnerException.Message;
                }
                lines.Add(name + " " + r.Replace("\r", "\\r").Replace("\n", "\\n"));
            }
        }
        lines.Sort(StringComparer.Ordinal);
        foreach (var l in lines)
            Console.WriteLine(l);
    }
}
