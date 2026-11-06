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
/// emitted by <see cref="JsonReadOptionsGetterBuilder"/>), reports what it does
/// not mirror (<see cref="JsonUnmirroredContextOptions"/>,
/// <see cref="JsonUnmirroredAttributes"/>), the <c>[JsonSerializable]</c> roots
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
        var specs = new JsonSpecBuilder(_context, plan);
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

        if (JsonReadOptionsGetterBuilder.Build(plan.ReadOptions) is { } readOptions)
            members.Add(readOptions);

        foreach (var entry in plan.Entries)
            members.AddRange(specs.BuildEntryMembers(entry));

        statements.Add(
            new TsClass(
                contextName,
                Constructor: null,
                Members: members,
                Exported: true,
                Extends: new TsNamedType("SerializerContext", Origin: JsonRuntimeModule.Origin)
            )
        );
    }

    /// <summary>
    /// The runtime helpers the emitted context spells as values (FR-041),
    /// from three surfaces:
    /// <list type="bullet">
    ///   <item>every claimed entry's own type, spelled in
    ///     <c>ValueSpec&lt;T&gt;</c> / <c>TypeSpec&lt;T&gt;</c> — a set value
    ///     root names <c>HashSet</c> there, a temporal root <c>Temporal</c>;
    ///     closure contracts and polymorphic variants are entries too, not
    ///     only <c>[JsonSerializable]</c> roots. Only emitted entries count:
    ///     rejected (<c>MS0035</c>) and dropped (<c>MS0036</c>) roots never
    ///     reach the plan.</item>
    ///   <item>the types the factories cast, spelled in
    ///     <c>p.x as T</c> (<see cref="JsonSpecBuilder.FactoryCastTypesOf"/>) —
    ///     a <c>HashSet&lt;int&gt;</c> member casts <c>HashSet&lt;number&gt;</c>
    ///     without ever naming it in a descriptor.</item>
    ///   <item>the temporal descriptors' parse value
    ///     (<c>parse: Temporal.X.from</c>), the value positions no cast
    ///     spells — a member the runtime assigns after the factory ran
    ///     (<c>assignIfPresent</c>), a dictionary key.</item>
    /// </list>
    /// The plan is computed silently; <see cref="Transform"/> reports its
    /// diagnostics. The System.Text.Json source generator declares a
    /// <c>JsonTypeInfo&lt;T&gt;</c> member per root, whose scan yields the
    /// same helpers, but the context must not depend on the generator having
    /// run.
    /// </summary>
    public IReadOnlySet<IrRuntimeRequirement> RuntimeRequirementsOf(INamedTypeSymbol contextType)
    {
        if (Plan(contextType, report: _ => { }) is not { } plan)
            return new HashSet<IrRuntimeRequirement>();

        return DescriptorRequirements(plan);
    }

    /// <summary>
    /// The requirement set of <paramref name="plan"/>'s three helper surfaces
    /// (see <see cref="RuntimeRequirementsOf"/>). Scans run on
    /// <see cref="IrTypeRefMapper"/> refs, whose mapping mirrors what the
    /// emitted identifiers spell (<c>Guid</c> → <c>UUID</c>,
    /// <c>System.DateTime</c> → <c>Temporal</c>, a set → <c>HashSet</c>).
    /// The temporal-descriptor scan stays temporal-only: a container
    /// descriptor names no other runtime value (a <c>hashSet</c> descriptor
    /// is its <c>kind</c> alone), and every helper a factory casts is
    /// collected through the cast types, so a wider scan would import a
    /// helper the file never spells.
    /// </summary>
    private static IReadOnlySet<IrRuntimeRequirement> DescriptorRequirements(JsonContextPlan plan)
    {
        var requirements = new HashSet<IrRuntimeRequirement>();

        foreach (var entry in plan.Entries)
            requirements.UnionWith(ScanRequirements(entry.Type));

        foreach (var castType in JsonSpecBuilder.FactoryCastTypesOf(plan))
            requirements.UnionWith(ScanRequirements(castType));

        if (ReferencesTemporalType(plan))
            requirements.Add(new IrRuntimeRequirement("Temporal", IrRuntimeCategory.Temporal));

        return requirements;
    }

    private static IReadOnlySet<IrRuntimeRequirement> ScanRequirements(ITypeSymbol type) =>
        IrRuntimeRequirementScanner.Scan(
            IrTypeRefMapper.Map(type, target: TargetLanguage.TypeScript)
        );

    /// <summary>
    /// Whether any descriptor of <paramref name="plan"/> names a temporal
    /// type: a temporal descriptor parses through <c>Temporal.X.from</c>, and
    /// <c>Temporal</c> has no BCL export mapping, so the identifier must come
    /// from the requirement set. The walk reads
    /// <see cref="JsonDescriptorTypes"/>, the same graph the descriptor
    /// builder nests through.
    /// </summary>
    private static bool ReferencesTemporalType(JsonContextPlan plan) =>
        JsonDescriptorTypes
            .Of(plan)
            .Any(type => JsonTypeClassifier.Classify(type) == JsonPropertyKind.Temporal);

    /// <summary>
    /// The classes the generated context passes as constructor values: every
    /// object entry that is neither <c>[PlainObject]</c> nor an interface
    /// gets a <c>TypeSpec</c>
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
            .Where(JsonContractAdmission.HasRuntimeConstructor)
            .Select(type => type.OriginalDefinition);
    }

    /// <summary>
    /// Reads the roots, names them and computes their closure, reporting every
    /// <c>MS0035</c> / <c>MS0036</c> / <c>MS0037</c> to <paramref name="report"/>. <c>null</c>
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
        var guards = new TypeGuardBuilder(_context);

        var polymorphismValidator = new JsonPolymorphismValidator(
            properties,
            admission,
            new JsonStructuralVariants(guards),
            guards,
            report
        );

        var contract = new JsonContractClosure(
            properties,
            admission,
            polymorphismValidator
        ).Collect(roots);

        ReportUnmirroredSettings(contextType, properties, contract, report);

        return new JsonContextPlan(
            names,
            properties,
            names.Claim(contract),
            contract.Polymorphisms,
            JsonOptionsReader.ReadOptionsOf(contextType)
        );
    }

    /// <summary>
    /// Reports <c>MS0037</c> for every context option and every attribute of
    /// the closure the generated context does not mirror (FR-046).
    /// </summary>
    private static void ReportUnmirroredSettings(
        INamedTypeSymbol contextType,
        JsonPropertyCollector properties,
        JsonContract contract,
        Action<MetanoDiagnostic> report
    )
    {
        var warnings = JsonUnmirroredContextOptions
            .WarningsOf(contextType)
            .Concat(new JsonUnmirroredAttributes(properties).WarningsOf(contract.Entries));

        foreach (var warning in warnings)
            report(warning);
    }
}
