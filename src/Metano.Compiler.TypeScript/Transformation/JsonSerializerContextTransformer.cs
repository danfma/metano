using Metano.Annotations;
using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation.Json;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Transforms a C# <c>JsonSerializerContext</c> subclass into a TypeScript
/// <c>SerializerContext</c> subclass with pre-computed spec definitions.
///
/// Orchestrates the <c>Json/</c> units: reads <c>[JsonSourceGenerationOptions]</c>
/// for the naming policy, the <c>[JsonSerializable]</c> roots
/// (<see cref="JsonContractRoots"/>), their contract closure
/// (<see cref="JsonContractClosure"/>) and the member names
/// (<see cref="JsonTypeInfoNames"/>), then emits one lazy getter per entry
/// (<see cref="JsonSpecBuilder"/>): a <c>TypeSpec</c> for an object contract
/// and a <c>ValueSpec</c> for every other root kind (FR-030). JSON property
/// names are pre-computed at transpile time.
/// </summary>
public sealed class JsonSerializerContextTransformer(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    public void Transform(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        var contextName = type.Name;
        var admission = new JsonContractAdmission(_context);
        var roots = new JsonContractRoots(_context, admission).Read(type);

        if (roots.Count == 0)
            return;

        var properties = new JsonPropertyCollector(_context, ReadNamingPolicy(type));
        var names = JsonTypeInfoNames.Create(_context, roots);
        var contract = new JsonContractClosure(properties, admission).Collect(roots);
        var specs = new JsonSpecBuilder(_context, names, properties);
        var members = new List<TsClassMember>();

        // private static readonly _default = new XxxContext();
        members.Add(
            new TsFieldMember(
                "_default",
                new TsNamedType(contextName),
                new TsNewExpression(new TsIdentifier(contextName), []),
                Readonly: true,
                Static: true,
                Accessibility: TsAccessibility.Private
            )
        );

        // static get default(): XxxContext { return this._default; }
        members.Add(
            new TsGetterMember(
                "default",
                new TsNamedType(contextName),
                [new TsReturnStatement(new TsPropertyAccess(new TsIdentifier("this"), "_default"))],
                Static: true
            )
        );

        foreach (var entry in names.Claim(contract))
            members.AddRange(specs.BuildEntryMembers(entry));

        statements.Add(
            new TsClass(
                contextName,
                Constructor: null,
                Members: members,
                Exported: true,
                Extends: new TsNamedType(
                    "SerializerContext",
                    Origin: new TsTypeOrigin("metano-runtime", "system/json")
                )
            )
        );
    }

    /// <summary>
    /// The runtime helpers the context's root getters need for the types they
    /// name in <c>ValueSpec&lt;T&gt;</c> / <c>TypeSpec&lt;T&gt;</c> (<c>HashSet</c>,
    /// <c>UUID</c>, <c>Temporal</c>, …; FR-041). The System.Text.Json source
    /// generator declares a <c>JsonTypeInfo&lt;T&gt;</c> member per root, whose
    /// scan yields the same helpers, but the context must not depend on the
    /// generator having run.
    /// </summary>
    public static IReadOnlySet<IrRuntimeRequirement> RuntimeRequirementsOf(
        INamedTypeSymbol contextType
    ) =>
        JsonContractRoots
            .DeclaredTypesOf(contextType)
            .SelectMany(root =>
                IrRuntimeRequirementScanner.Scan(
                    IrTypeRefMapper.Map(root, target: TargetLanguage.TypeScript)
                )
            )
            .ToHashSet();

    /// <summary>
    /// Reads the PropertyNamingPolicy from [JsonSourceGenerationOptions] if present.
    /// </summary>
    private static Func<string, string>? ReadNamingPolicy(INamedTypeSymbol contextType)
    {
        foreach (var attr in contextType.GetAttributes())
        {
            if (
                attr.AttributeClass?.Name
                is not ("JsonSourceGenerationOptionsAttribute" or "JsonSourceGenerationOptions")
            )
                continue;

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key != "PropertyNamingPolicy")
                    continue;

                // The value is a JsonKnownNamingPolicy enum member (int).
                // We need the enum member name, not the int value.
                var enumValue = namedArg.Value;
                if (enumValue.Type is INamedTypeSymbol enumType)
                {
                    // Get the enum member name from the int value
                    var intVal = (int)(enumValue.Value ?? 0);
                    foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
                    {
                        if (
                            member.HasConstantValue
                            && member.ConstantValue is int memberVal
                            && memberVal == intVal
                        )
                        {
                            return JsonNamingPolicy.FromKnownPolicy(member.Name);
                        }
                    }
                }
            }
        }

        return null;
    }
}
