using System.Text.RegularExpressions;
using Metano.Compiler;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// The constructor roles the TypeScript target passes to each shipped
/// <c>metano-runtime</c> exception class (<see cref="BclExceptionClasses"/>,
/// spec 011 FR-014), checked against the constructor the runtime source
/// declares: <c>paramName</c> and <c>message</c> are both <c>string | null</c>,
/// so a swapped order would pass <c>tsc</c> and the goldens. Both sides read
/// a parameter's role through the extractor's own
/// <see cref="IrExceptionConstructionExtractor.RoleOfParameterNamed"/>. Every
/// exception class of the runtime must be mapped, and every parameter of a
/// mapped .NET constructor must have a role or be one the runtime drops.
/// </summary>
public partial class RuntimeExceptionConstructorTests
{
    private const string SolutionFile = "Metano.slnx";

    /// <summary>
    /// The runtime modules that are not an exception class of their own: the
    /// factory of the stand-in for an unmapped exception type, and the
    /// normalization of a caught value.
    /// </summary>
    private static readonly string[] NonClassModules = ["named-exception.ts", "to-exception.ts"];

    /// <summary>
    /// Parameters of the mapped .NET constructors that no runtime class takes,
    /// so a construction drops them (still evaluating a side-effecting one).
    /// </summary>
    private static readonly HashSet<string> DroppedDotNetParameters =
    [
        "path",
        "lineNumber",
        "bytePositionInLine",
        "token",
        "task",
        "errorCode",
        "className",
        "methodName",
    ];

    public static IEnumerable<(string TypeKey, IrBclExport Export)> ShippedExceptionMappings() =>
        BclExportReader
            .Read(ReferenceCompilation())
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
        var mapped = ShippedExceptionMappings()
            .Select(mapping => mapping.Export.ExportedName)
            .Order(StringComparer.Ordinal);

        var defined = Directory
            .GetFiles(RuntimeExceptionsDirectory(), "*exception.ts")
            .Where(path => !NonClassModules.Contains(Path.GetFileName(path)))
            .Select(path =>
                ExportedClassPattern().Match(File.ReadAllText(path)).Groups["name"].Value
            )
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(", ", mapped)).IsEqualTo(string.Join(", ", defined));
    }

    [Test]
    [MethodDataSource(nameof(ShippedExceptionMappings))]
    public async Task ConstructorRoles_MatchTheRuntimeConstructor(
        string typeKey,
        IrBclExport export
    )
    {
        var declared = RuntimeConstructorParameters(export)
            .Select(name => RoleOfRuntimeParameter(export, name))
            .ToList();

        var passed = BclExceptionClasses.RuntimeConstructorRoles(typeKey);

        await Assert
            .That(string.Join(", ", passed))
            .IsEqualTo(string.Join(", ", declared))
            .Because($"the compiler's role order for {export.ExportedName}");
    }

    [Test]
    [MethodDataSource(nameof(ShippedExceptionMappings))]
    public async Task EveryDotNetConstructorParameter_HasARoleOrIsDropped(
        string typeKey,
        IrBclExport export
    )
    {
        var type = ReferenceCompilation().GetTypeByMetadataName(typeKey)
            ?? throw new InvalidOperationException($"No {typeKey} in the reference assemblies.");

        var unclassified = type
            .InstanceConstructors.Where(constructor =>
                constructor.DeclaredAccessibility is Accessibility.Public
            )
            .SelectMany(constructor => constructor.Parameters)
            .Where(parameter =>
                IrExceptionConstructionExtractor.RoleOf(parameter)
                    is IrExceptionArgumentRole.Unmapped
                && !DroppedDotNetParameters.Contains(parameter.Name)
            )
            .Select(parameter => parameter.Name)
            .Distinct()
            .ToList();

        await Assert.That(unclassified).IsEmpty().Because($"{export.ExportedName}'s constructors");
    }

    private static CSharpCompilation ReferenceCompilation() =>
        CSharpCompilation.Create(
            "RuntimeExceptionConstructorTests",
            references: TranspileHelper.BaseReferences
        );

    /// <summary>
    /// The role of a runtime constructor parameter; a parameter without one
    /// is a contract the compiler cannot pass, so the test fails loudly.
    /// </summary>
    private static IrExceptionArgumentRole RoleOfRuntimeParameter(IrBclExport export, string name)
    {
        var role = IrExceptionConstructionExtractor.RoleOfParameterNamed(name);

        if (role is IrExceptionArgumentRole.Unmapped)
            throw new InvalidOperationException(
                $"{export.ExportedName}'s constructor parameter '{name}' has no role."
            );

        return role;
    }

    /// <summary>
    /// The parameter names of the constructor of <paramref name="export"/>'s
    /// class in its runtime module, split at the top-level commas of the
    /// parameter list (a function type or a parenthesised default stays whole).
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

        var open = source.IndexOf("constructor(", classStart, StringComparison.Ordinal);

        if (open < 0)
            throw new InvalidOperationException($"{export.ExportedName} declares no constructor.");

        return TopLevelSegments(source, open + "constructor(".Length)
            .Select(segment => ParameterNameOf(export, segment))
            .ToList();
    }

    private static string ParameterNameOf(IrBclExport export, string segment)
    {
        var match = ParameterNamePattern().Match(segment);

        if (!match.Success)
            throw new InvalidOperationException(
                $"Cannot read the parameter '{segment}' of {export.ExportedName}'s constructor."
            );

        return match.Groups["name"].Value;
    }

    /// <summary>
    /// The comma-separated segments from <paramref name="start"/> up to the
    /// parenthesis that closes the list, ignoring commas nested in brackets.
    /// </summary>
    private static List<string> TopLevelSegments(string source, int start)
    {
        var segments = new List<string>();
        var depth = 0;
        var segmentStart = start;

        for (var index = start; index < source.Length; index++)
        {
            var character = source[index];

            if (character is ')' && depth == 0)
            {
                var last = source[segmentStart..index].Trim();

                if (last.Length > 0)
                    segments.Add(last);

                return segments;
            }

            if (character is '(' or '[' or '{' or '<')
                depth++;
            else if (character is ')' or ']' or '}' or '>' && depth > 0)
                depth--;
            else if (character is ',' && depth == 0)
            {
                segments.Add(source[segmentStart..index].Trim());
                segmentStart = index + 1;
            }
        }

        throw new InvalidOperationException("Unterminated constructor parameter list.");
    }

    private static string RuntimeModulePath(IrBclExport export)
    {
        var module = export.FromPackage[BclExceptionClasses.RuntimeExceptionModules.Length..];

        return Path.Combine(RuntimeExceptionsDirectory(), module + ".ts");
    }

    private static string RuntimeExceptionsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, SolutionFile)))
            root = root.Parent;

        if (root is null)
            throw new InvalidOperationException($"No {SolutionFile} above the test binaries.");

        return Path.Combine(
            root.FullName,
            "targets",
            "js",
            "metano-runtime",
            "src",
            "system",
            "exceptions"
        );
    }

    [GeneratedRegex(@"export class (?<name>\w+) ")]
    private static partial Regex ExportedClassPattern();

    [GeneratedRegex(@"^(?<name>\w+)\??\s*:")]
    private static partial Regex ParameterNamePattern();
}
