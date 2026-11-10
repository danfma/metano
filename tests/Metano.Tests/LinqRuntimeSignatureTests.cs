using System.Reflection;
using System.Text.RegularExpressions;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Tests;

/// <summary>
/// The overloads <see cref="IrLinqMapping"/> declares each runtime operator
/// implements (#648) hold against both sides: every declared overload exists
/// in <c>System.Linq.Enumerable</c> or <c>System.Linq.Queryable</c>, and the
/// runtime operator in <c>targets/js/metano-runtime/src/system/linq</c>
/// takes as many arguments as it passes, a comparer only if it has a
/// <c>comparer</c> parameter, and its queryable tree at the declared slot.
/// The runtime side is read from the operator's implementation signature.
/// </summary>
public partial class LinqRuntimeSignatureTests
{
    private const string SolutionFile = "Metano.slnx";

    private const string QueryableMetaType = "QueryableMeta";

    private static readonly string[] RuntimeSources = ["operators.ts", "terminals.ts"];

    private static readonly Lazy<string> RuntimeSource = new(ReadRuntimeSource);

    public static IEnumerable<IrLinqOperator> Operators() => Enum.GetValues<IrLinqOperator>();

    [Test]
    [MethodDataSource(nameof(Operators))]
    public async Task EveryDeclaredOverload_ExistsInDotNet(IrLinqOperator op)
    {
        var dotNetOverloads = IrLinqMapping
            .MethodNamesOf(op)
            .SelectMany(DotNetOverloadsNamed)
            .ToList();

        foreach (var overload in IrLinqMapping.RuntimeSignatureOf(op).Overloads)
        {
            await Assert
                .That(dotNetOverloads.Any(candidate => candidate.SequenceEqual(overload)))
                .IsTrue()
                .Because($"{op}({string.Join(", ", overload)}) is no .NET overload");
        }
    }

    [Test]
    [MethodDataSource(nameof(Operators))]
    public async Task EveryDeclaredOverload_FitsTheRuntimeOperator(IrLinqOperator op)
    {
        var parameters = RuntimeParametersOf(IrLinqMapping.EmittedName(op));
        var values = parameters.Where(parameter => !parameter.IsQueryable).ToList();
        var required = values.Count(parameter => !parameter.IsOptional);

        foreach (var overload in IrLinqMapping.RuntimeSignatureOf(op).Overloads)
        {
            var shape = $"{op}({string.Join(", ", overload)})";

            await Assert.That(overload.Count).IsLessThanOrEqualTo(values.Count).Because(shape);
            await Assert.That(overload.Count).IsGreaterThanOrEqualTo(required).Because(shape);

            if (overload.Contains(IrLinqMapping.ComparerParameterName))
            {
                await Assert
                    .That(values.Any(value => value.Name == IrLinqMapping.ComparerParameterName))
                    .IsTrue()
                    .Because(shape);
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(Operators))]
    public async Task QueryableSlot_IsTheRuntimeOperatorsQueryableParameter(IrLinqOperator op)
    {
        var parameters = RuntimeParametersOf(IrLinqMapping.EmittedName(op));
        var slot = parameters.FindIndex(parameter => parameter.IsQueryable);

        await Assert
            .That(IrLinqMapping.RuntimeSignatureOf(op).QueryableSlot)
            .IsEqualTo(slot < 0 ? null : slot);
    }

    private static IEnumerable<IReadOnlyList<string>> DotNetOverloadsNamed(string name) =>
        new[] { typeof(Enumerable), typeof(Queryable) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name == name)
            .Select(method =>
                (IReadOnlyList<string>)[.. method.GetParameters().Skip(1).Select(p => p.Name!)]
            );

    /// <summary>
    /// The parameters of the last <c>export function</c> named
    /// <paramref name="name"/> — the implementation signature, after any
    /// overload declarations.
    /// </summary>
    private static List<RuntimeParameter> RuntimeParametersOf(string name)
    {
        var source = RuntimeSource.Value;
        var start = source.LastIndexOf($"export function {name}<", StringComparison.Ordinal);

        if (start < 0)
            throw new InvalidOperationException($"No runtime operator '{name}'.");

        var open = source.IndexOf('(', SkipGenericParameters(source, start));
        var parameters = SplitTopLevel(source[(open + 1)..ClosingParenthesis(source, open)]);

        return [.. parameters.Select(RuntimeParameter.Parse)];
    }

    private static int SkipGenericParameters(string source, int start)
    {
        var index = source.IndexOf('<', start);
        var depth = 0;

        do
        {
            depth += source[index] switch
            {
                '<' => 1,
                '>' => -1,
                _ => 0,
            };

            index++;
        } while (depth > 0);

        return index;
    }

    private static int ClosingParenthesis(string source, int open)
    {
        var depth = 0;

        for (var index = open; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };

            if (depth == 0)
                return index;
        }

        throw new InvalidOperationException("Unbalanced parameter list.");
    }

    /// <summary>
    /// Splits <paramref name="text"/> at the commas outside any bracket
    /// (an arrow's <c>=&gt;</c> closes none).
    /// </summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var isArrow = text[index] == '>' && index > 0 && text[index - 1] == '=';

            depth += text[index] switch
            {
                '(' or '<' or '{' or '[' => 1,
                ')' or '}' or ']' => -1,
                '>' when !isArrow => -1,
                _ => 0,
            };

            if (text[index] == ',' && depth == 0)
            {
                parts.Add(text[start..index]);
                start = index + 1;
            }
        }

        parts.Add(text[start..]);

        return [.. parts.Select(part => part.Trim()).Where(part => part.Length > 0)];
    }

    private static string ReadRuntimeSource()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, SolutionFile)))
            root = root.Parent;

        if (root is null)
            throw new InvalidOperationException($"No {SolutionFile} above the test binaries.");

        var linq = Path.Combine(
            root.FullName,
            "targets",
            "js",
            "metano-runtime",
            "src",
            "system",
            "linq"
        );

        return string.Join(
            '\n',
            RuntimeSources.Select(file => File.ReadAllText(Path.Combine(linq, file)))
        );
    }

    [GeneratedRegex(@"^(?<name>\w+)(?<optional>\?)?\s*:(?<type>.*)$", RegexOptions.Singleline)]
    private static partial Regex ParameterPattern();

    /// <summary>
    /// One parameter of a runtime operator: its name, whether a call may
    /// omit it, and whether it is the operator's <c>QueryableMeta</c>.
    /// </summary>
    private sealed record RuntimeParameter(string Name, bool IsOptional, bool IsQueryable)
    {
        public static RuntimeParameter Parse(string text)
        {
            var match = ParameterPattern().Match(text);

            if (!match.Success)
                throw new InvalidOperationException($"Unparsed runtime parameter '{text}'.");

            var type = match.Groups["type"].Value;
            var defaultStart = Regex.Match(type, "=(?!>)");
            var declaredType = defaultStart.Success ? type[..defaultStart.Index] : type;

            return new RuntimeParameter(
                match.Groups["name"].Value,
                match.Groups["optional"].Success || defaultStart.Success,
                declaredType.Trim() == QueryableMetaType
            );
        }
    }
}
