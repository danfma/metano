using System.Text.RegularExpressions;
using Metano.Compiler;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// The constructor roles the TypeScript target passes to each shipped
/// <c>metano-runtime</c> exception class (<see cref="BclExceptionClasses"/>,
/// spec 011 FR-014), checked against the constructor the runtime source
/// declares: <c>paramName</c> and <c>message</c> are both <c>string | null</c>,
/// so a swapped order would pass <c>tsc</c> and the goldens. Each
/// <c>[ExportFromBcl]</c> entry of <c>Metano/Runtime/Exceptions.cs</c> must also
/// name a class its module exports.
/// </summary>
public partial class RuntimeExceptionConstructorTests
{
    private const string SolutionFile = "Metano.slnx";

    private static readonly Dictionary<string, IrExceptionArgumentRole> RolesByParameterName = new(
        StringComparer.Ordinal
    )
    {
        ["message"] = IrExceptionArgumentRole.Message,
        ["innerException"] = IrExceptionArgumentRole.InnerException,
        ["paramName"] = IrExceptionArgumentRole.ParamName,
        ["actualValue"] = IrExceptionArgumentRole.ActualValue,
        ["objectName"] = IrExceptionArgumentRole.ObjectName,
    };

    public static IEnumerable<(string TypeKey, IrBclExport Export)> ShippedExceptionMappings() =>
        BclExportReader
            .Read(
                CSharpCompilation.Create(
                    "RuntimeExceptionConstructorTests",
                    references: TranspileHelper.BaseReferences
                )
            )
            .Where(entry =>
                entry.Value.FromPackage.StartsWith(
                    BclExceptionClasses.RuntimeExceptionModules,
                    StringComparison.Ordinal
                )
            )
            .Select(entry => (entry.Key, entry.Value));

    [Test]
    public async Task EveryRuntimeExceptionClass_IsMapped()
    {
        var mapped = ShippedExceptionMappings().Select(mapping => mapping.Export.ExportedName);

        await Assert.That(mapped.Count()).IsEqualTo(21);
    }

    [Test]
    [MethodDataSource(nameof(ShippedExceptionMappings))]
    public async Task ConstructorRoles_MatchTheRuntimeConstructor(
        string typeKey,
        IrBclExport export
    )
    {
        var declared = RuntimeConstructorParameters(export)
            .Select(name => RolesByParameterName[name])
            .ToList();

        var passed = BclExceptionClasses.RuntimeConstructorRoles(typeKey);

        await Assert
            .That(string.Join(", ", passed))
            .IsEqualTo(string.Join(", ", declared))
            .Because($"the compiler's role order for {export.ExportedName}");
    }

    /// <summary>
    /// The parameter names of the constructor of <paramref name="export"/>'s
    /// class in its runtime module.
    /// </summary>
    private static IReadOnlyList<string> RuntimeConstructorParameters(IrBclExport export)
    {
        var source = File.ReadAllText(RuntimeModulePath(export));
        var classStart = source.IndexOf(
            $"export class {export.ExportedName} ",
            StringComparison.Ordinal
        );

        if (classStart < 0)
            throw new InvalidOperationException(
                $"{export.FromPackage} exports no {export.ExportedName}."
            );

        var constructor = ConstructorPattern().Match(source, classStart);

        if (!constructor.Success)
            throw new InvalidOperationException($"{export.ExportedName} declares no constructor.");

        return ParameterNamePattern()
            .Matches(constructor.Groups["parameters"].Value)
            .Select(match => match.Groups["name"].Value)
            .ToList();
    }

    private static string RuntimeModulePath(IrBclExport export)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, SolutionFile)))
            root = root.Parent;

        if (root is null)
            throw new InvalidOperationException($"No {SolutionFile} above the test binaries.");

        var module = export.FromPackage["metano-runtime/".Length..];

        return Path.Combine(
            root.FullName,
            "targets",
            "js",
            "metano-runtime",
            "src",
            module + ".ts"
        );
    }

    [GeneratedRegex(@"constructor\((?<parameters>[^)]*)\)", RegexOptions.Singleline)]
    private static partial Regex ConstructorPattern();

    [GeneratedRegex(@"(?<name>\w+)\??\s*:")]
    private static partial Regex ParameterNamePattern();
}
