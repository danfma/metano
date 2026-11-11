using System.Reflection;
using System.Text.RegularExpressions;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests;

/// <summary>
/// What <see cref="IrLinqMapping"/> declares each runtime operator implements
/// (#648), checked against both sides. Against .NET: every declared overload
/// is the parameter-name list of a real <c>System.Linq.Enumerable</c> /
/// <c>Queryable</c> overload. Against the runtime implementation signatures
/// in <c>targets/js/metano-runtime/src/system/linq</c>: the operator takes at
/// least and at most as many value arguments as each overload passes, a
/// comparer only through a <c>comparer</c> parameter, and its
/// <c>QueryableMeta</c> at the declared slot; the 64-bit terminals of
/// <c>int64-terminals.ts</c>, which replace <c>sum</c> / <c>average</c> /
/// <c>min</c> / <c>max</c> for 64-bit values, take the same arguments. These
/// are counts and positions, not types or roles. The golden
/// <c>linq-supported-overloads.ts</c> must call every declared overload
/// (asserted here) and pins the emitted calls byte for byte; no workflow
/// type-checks it, so a wrong declaration whose call is emitted but
/// ill-typed is caught only when a maintainer runs <c>tsgo</c> on it.
/// </summary>
public partial class LinqRuntimeSignatureTests
{
    private const string SolutionFile = "Metano.slnx";

    private const string QueryableMetaType = "QueryableMeta";

    private static readonly string[] RuntimeSourceFiles =
    [
        "operators.ts",
        "terminals.ts",
        "int64-terminals.ts",
    ];

    private static readonly Lazy<string> RuntimeSourceText = new(ReadRuntimeSourceText);

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
                .That(dotNetOverloads.Contains(overload))
                .IsTrue()
                .Because($"{overload.ToString(op.ToString())} is no .NET overload");
        }
    }

    [Test]
    [MethodDataSource(nameof(Operators))]
    public async Task EveryDeclaredOverload_FitsTheRuntimeOperatorsArity(IrLinqOperator op)
    {
        var parameters = RuntimeParametersOf(IrLinqMapping.EmittedName(op));
        var values = parameters.Where(parameter => !parameter.IsQueryable).ToList();
        var required = values.Count(parameter => !parameter.IsOptional);

        foreach (var overload in IrLinqMapping.RuntimeSignatureOf(op).Overloads)
        {
            var shape = overload.ToString(op.ToString());

            await Assert
                .That(overload.Names.Count)
                .IsLessThanOrEqualTo(values.Count)
                .Because(shape);

            await Assert.That(overload.Names.Count).IsGreaterThanOrEqualTo(required).Because(shape);

            if (overload.PassesComparer)
            {
                await Assert
                    .That(values.Any(value => value.Name == ComparerParameter.Name))
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

    [Test]
    [Arguments("sumInt64", "sum")]
    [Arguments("averageInt64", "average")]
    [Arguments("averageNullableInt64", "average")]
    [Arguments("minInt64Of", "min")]
    [Arguments("maxInt64Of", "max")]
    [Arguments("minUInt64Of", "min")]
    [Arguments("maxUInt64Of", "max")]
    [Arguments("minNullableInt64Of", "min")]
    [Arguments("maxNullableInt64Of", "max")]
    public async Task SixtyFourBitTerminal_TakesTheArgumentsOfItsGenericTerminal(
        string sixtyFourBit,
        string generic
    )
    {
        await Assert.That(ArgumentShapeOf(sixtyFourBit)).IsEqualTo(ArgumentShapeOf(generic));
    }

    [Test]
    public async Task TheSupportedOverloadsGolden_CallsEveryDeclaredOverload()
    {
        var compilation = TranspileHelper.CompileLibrary(
            LinqUnimplementedOverloadTests.SupportedSource
        );

        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);

        var called = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => model.GetSymbolInfo(invocation).Symbol as IMethodSymbol)
            .SelectMany(CalledOverloadOf)
            .ToHashSet();

        foreach (var op in Operators())
        {
            foreach (var overload in IrLinqMapping.RuntimeSignatureOf(op).Overloads)
            {
                await Assert
                    .That(called.Contains((op, overload)))
                    .IsTrue()
                    .Because($"the golden calls no {overload.ToString(op.ToString())}");
            }
        }
    }

    private static IEnumerable<(IrLinqOperator, LinqOverloadParameters)> CalledOverloadOf(
        IMethodSymbol? method
    ) =>
        IrLinqMapping.TryResolve(method, out var op)
            ? [(op, LinqOverloadParameters.From(method))]
            : [];

    private static IEnumerable<LinqOverloadParameters> DotNetOverloadsNamed(string name) =>
        new[] { typeof(Enumerable), typeof(Queryable) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name == name)
            .Select(method =>
                LinqOverloadParameters.Named([
                    .. method.GetParameters().Skip(1).Select(parameter => parameter.Name!),
                ])
            );

    private static string ArgumentShapeOf(string name) =>
        string.Join(
            ", ",
            RuntimeParametersOf(name)
                .Select(parameter => $"{parameter.IsOptional}:{parameter.IsQueryable}")
        );

    /// <summary>
    /// The parameters of the last <c>export function</c> named
    /// <paramref name="name"/> — the implementation signature, after any
    /// overload declarations.
    /// </summary>
    private static List<RuntimeParameter> RuntimeParametersOf(string name)
    {
        var source = RuntimeSourceText.Value;
        var start = source.LastIndexOf($"export function {name}<", StringComparison.Ordinal);

        if (start < 0)
            throw Unparsed(name, "no generic `export function` declares it");

        var open = source.IndexOf('(', SkipGenericParameters(source, start, name));

        if (open < 0)
            throw Unparsed(name, "its parameter list does not open");

        var close = ClosingParenthesis(source, open, name);

        return [.. SplitTopLevel(source[(open + 1)..close]).Select(text => Parse(text, name))];
    }

    private static int SkipGenericParameters(string source, int start, string name)
    {
        var index = source.IndexOf('<', start);
        var depth = 0;

        do
        {
            if (index < 0 || index >= source.Length)
                throw Unparsed(name, "its type parameters do not close");

            depth += AngleDepthChange(source, index);
            index++;
        } while (depth > 0);

        return index;
    }

    private static int ClosingParenthesis(string source, int open, string name)
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

        throw Unparsed(name, "its parameter list does not close");
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
            depth += text[index] switch
            {
                '(' or '{' or '[' => 1,
                ')' or '}' or ']' => -1,
                '<' or '>' => AngleDepthChange(text, index),
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

    /// <summary>
    /// How an angle bracket at <paramref name="index"/> changes the nesting:
    /// the <c>&gt;</c> of an arrow <c>=&gt;</c> closes nothing.
    /// </summary>
    private static int AngleDepthChange(string text, int index) =>
        text[index] switch
        {
            '<' => 1,
            '>' when index > 0 && text[index - 1] == '=' => 0,
            '>' => -1,
            _ => 0,
        };

    private static RuntimeParameter Parse(string text, string name)
    {
        var match = ParameterPattern().Match(text);

        if (!match.Success)
            throw Unparsed(name, $"its parameter '{text}' has no name and type");

        var type = match.Groups["type"].Value;
        var defaultStart = DefaultValuePattern().Match(type);
        var declaredType = defaultStart.Success ? type[..defaultStart.Index] : type;

        return new RuntimeParameter(
            match.Groups["name"].Value,
            match.Groups["optional"].Success || defaultStart.Success,
            declaredType.Trim() == QueryableMetaType
        );
    }

    private static InvalidOperationException Unparsed(string name, string reason) =>
        new($"Cannot read the runtime LINQ operator '{name}': {reason}.");

    private static string ReadRuntimeSourceText()
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
            RuntimeSourceFiles.Select(file => File.ReadAllText(Path.Combine(linq, file)))
        );
    }

    [GeneratedRegex(@"^(?<name>\w+)(?<optional>\?)?\s*:(?<type>.*)$", RegexOptions.Singleline)]
    private static partial Regex ParameterPattern();

    /// <summary>The <c>=</c> of a default value, not of an arrow <c>=&gt;</c>.</summary>
    [GeneratedRegex("=(?!>)")]
    private static partial Regex DefaultValuePattern();

    /// <summary>
    /// One parameter of a runtime operator: its name, whether a call may
    /// omit it, and whether it is the operator's <c>QueryableMeta</c>.
    /// </summary>
    private sealed record RuntimeParameter(string Name, bool IsOptional, bool IsQueryable);
}
