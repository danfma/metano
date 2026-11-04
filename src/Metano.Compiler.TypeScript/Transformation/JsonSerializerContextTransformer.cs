using Metano.Annotations;
using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
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
/// for the naming policy and the read options (<see cref="JsonOptionsReader"/>,
/// emitted by <see cref="JsonReadOptionsEmitter"/>), the <c>[JsonSerializable]</c> roots
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
        if (Plan(type, _context.ReportDiagnostic) is not { } plan)
            return;

        var contextName = _context.ResolveTsName(type);
        var specs = new JsonSpecBuilder(
            _context,
            plan.Names,
            plan.Properties,
            plan.Polymorphisms,
            plan.ReadOptions.ReadsNumbersFromStrings
        );

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

        if (JsonReadOptionsEmitter.Build(plan.ReadOptions) is { } readOptions)
            members.Add(readOptions);

        foreach (var entry in plan.Entries)
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
    /// The runtime helpers the emitted root getters need for the types they
    /// name in <c>ValueSpec&lt;T&gt;</c> / <c>TypeSpec&lt;T&gt;</c> (<c>HashSet</c>,
    /// <c>UUID</c>, <c>Temporal</c>, …; FR-041). Only roots that are emitted
    /// count: rejected (<c>MS0035</c>) and dropped (<c>MS0036</c>) ones do not.
    /// The plan is computed silently; <see cref="Transform"/> reports its
    /// diagnostics. The System.Text.Json source generator declares a
    /// <c>JsonTypeInfo&lt;T&gt;</c> member per root, whose scan yields the same
    /// helpers, but the context must not depend on the generator having run.
    /// </summary>
    public IReadOnlySet<IrRuntimeRequirement> RuntimeRequirementsOf(INamedTypeSymbol contextType)
    {
        if (Plan(contextType, report: _ => { }) is not { } plan)
            return new HashSet<IrRuntimeRequirement>();

        return plan
            .Entries.Where(entry => entry.Root is not null)
            .SelectMany(entry =>
                IrRuntimeRequirementScanner.Scan(
                    IrTypeRefMapper.Map(entry.Type, target: TargetLanguage.TypeScript)
                )
            )
            .ToHashSet();
    }

    /// <summary>
    /// The classes the generated context passes as constructor values: every
    /// object entry that is not <c>[PlainObject]</c> gets a <c>TypeSpec</c>
    /// naming the class (<c>type: T</c>, typed <c>abstract new (...) =&gt; T</c>)
    /// and calling it (<c>factory: (p) =&gt; new T(...)</c>), both outside the
    /// class body, so the class must keep a public constructor (FR-049). The
    /// plan is computed silently, as in <see cref="RuntimeRequirementsOf"/>.
    /// </summary>
    public IEnumerable<INamedTypeSymbol> ConstructedTypesOf(INamedTypeSymbol contextType)
    {
        if (Plan(contextType, report: _ => { }) is not { } plan)
            return [];

        return plan
            .Entries.Where(entry => entry.Kind == JsonPropertyKind.Ref)
            .Select(entry => entry.Type)
            .OfType<INamedTypeSymbol>()
            .Where(type => !SymbolHelper.HasPlainObject(type))
            .Select(type => type.OriginalDefinition);
    }

    /// <summary>
    /// Reads the roots, names them and computes their closure, reporting every
    /// <c>MS0035</c> / <c>MS0036</c> to <paramref name="report"/>. <c>null</c>
    /// when the context has no admitted root (nothing is emitted).
    /// </summary>
    private JsonContextPlan? Plan(INamedTypeSymbol contextType, Action<MetanoDiagnostic> report)
    {
        var admission = new JsonContractAdmission(_context);
        var roots = new JsonContractRoots(admission, report).Read(contextType);

        if (roots.Count == 0)
            return null;

        var properties = new JsonPropertyCollector(
            _context,
            admission,
            JsonOptionsReader.NamingPolicyOf(contextType)
        );

        var names = JsonTypeInfoNames.Create(_context, roots, report);
        var polymorphismValidator = new JsonPolymorphismValidator(properties, admission, report);

        var contract = new JsonContractClosure(
            properties,
            admission,
            polymorphismValidator
        ).Collect(roots);

        return new JsonContextPlan(
            names,
            properties,
            names.Claim(contract),
            contract.Polymorphisms,
            JsonOptionsReader.ReadOptionsOf(contextType)
        );
    }
}
