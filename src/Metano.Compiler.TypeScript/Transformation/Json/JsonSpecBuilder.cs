using System.Globalization;
using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Builds the members of the generated context for its claimed entries: one
/// lazy field + getter pair per entry, over a <c>TypeSpec</c> for an object
/// contract or a <c>ValueSpec</c> for a value root. Descriptors come from
/// <see cref="JsonTypeDescriptorBuilder"/>; every <c>base</c> link names its
/// target through <see cref="JsonTypeInfoNames"/>; a polymorphic base ends
/// with its <c>polymorphism</c> member (<see cref="JsonPolymorphismSpecBuilder"/>).
/// </summary>
internal sealed class JsonSpecBuilder
{
    private const string PayloadParameterName = "p";

    private readonly TypeScriptTransformContext _context;
    private readonly JsonTypeInfoNames _names;
    private readonly JsonPropertyCollector _properties;
    private readonly IReadOnlyDictionary<ITypeSymbol, JsonPolymorphism> _polymorphisms;
    private readonly JsonTypeDescriptorBuilder _descriptors;
    private readonly JsonPolymorphismSpecBuilder _polymorphismSpecs;

    public JsonSpecBuilder(TypeScriptTransformContext context, JsonContextPlan plan)
    {
        _context = context;
        _names = plan.Names;
        _properties = plan.Properties;
        _polymorphisms = plan.Polymorphisms;
        _descriptors = new JsonTypeDescriptorBuilder(context, plan.Names, plan.ReadOptions);
        _polymorphismSpecs = new JsonPolymorphismSpecBuilder(
            _descriptors,
            new JsonGuardReferences(context)
        );
    }

    /// <summary>
    /// The lazy field + getter pair of one context entry:
    /// <c>private _todoItem?: TypeSpec&lt;TodoItem&gt;</c> and
    /// <c>get todoItem() { return this._todoItem ??= this.createSpec({...}); }</c>
    /// for an object contract, or the same shape over
    /// <c>ValueSpec&lt;T&gt;</c> / <c>createValueSpec({ descriptor })</c> for a
    /// value root.
    /// </summary>
    public IEnumerable<TsClassMember> BuildEntryMembers(JsonContractEntry entry)
    {
        var getterName = _names.GetterOf(entry.Type);
        var fieldName = JsonTypeInfoNames.FieldOf(getterName);
        var (specType, factory) = BuildSpec(entry);

        yield return new TsFieldMember(
            fieldName,
            specType,
            Optional: true,
            Accessibility: TsAccessibility.Private
        );

        yield return new TsGetterMember(
            getterName,
            specType,
            [
                new TsReturnStatement(
                    new TsBinaryExpression(
                        new TsPropertyAccess(new TsIdentifier("this"), fieldName),
                        "??=",
                        factory
                    )
                ),
            ]
        );
    }

    private (TsType SpecType, TsCallExpression Factory) BuildSpec(JsonContractEntry entry)
    {
        if (entry.Kind == JsonPropertyKind.Ref)
            return BuildTypeSpec((INamedTypeSymbol)entry.Type);

        var descriptor = _descriptors.Build(entry.Type, entry.Kind);

        return (
            new TsNamedType(
                "ValueSpec",
                [ValueSpecTypeArgument(entry.Type)],
                JsonRuntimeModule.Origin
            ),
            ContextFactoryCall("createValueSpec", BuildValueSpecObject(entry.Type, descriptor))
        );
    }

    /// <summary>
    /// <c>{ descriptor }</c>, plus <c>valueType: true</c> and the CLR
    /// <c>typeName</c> for a value type (FR-037).
    /// </summary>
    private static TsObjectLiteral BuildValueSpecObject(
        ITypeSymbol type,
        TsExpression descriptor
    ) => new([new TsObjectProperty("descriptor", descriptor), .. ValueTypeProperties(type)]);

    /// <summary>
    /// <c>valueType: true, typeName: "MyApp.SPoint"</c> for a C# value type,
    /// whose JSON <c>null</c> throws <c>JsonException</c> naming that type as
    /// System.Text.Json does (FR-037, <see cref="Analysis.ClrTypeName"/>); nothing
    /// for a reference type and for <c>Nullable&lt;T&gt;</c>, which read
    /// <c>null</c> as <c>null</c>.
    /// </summary>
    private static IEnumerable<TsObjectProperty> ValueTypeProperties(ITypeSymbol type)
    {
        if (!RejectsNull(type))
            yield break;

        yield return new TsObjectProperty("valueType", new TsLiteral("true"));
        yield return new TsObjectProperty("typeName", new TsStringLiteral(ClrTypeName.Of(type)));
    }

    private static bool RejectsNull(ITypeSymbol type) =>
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

    /// <summary>
    /// <c>TypeSpec&lt;T&gt;</c> and its <c>createSpec</c> call, where <c>T</c> is
    /// the full mapped type of the contract (FR-032). A closed generic
    /// contract names <c>T</c> explicitly (<c>createSpec&lt;Page&lt;EpisodeSummary&gt;&gt;</c>):
    /// <c>type: Page</c> alone makes TypeScript infer <c>Page&lt;unknown&gt;</c>,
    /// which the getter's type rejects (FR-033). Other contracts keep the bare
    /// call.
    /// </summary>
    private (TsType SpecType, TsCallExpression Factory) BuildTypeSpec(INamedTypeSymbol objectType)
    {
        var contractType = JsonTsTypes.MapContract(_context, objectType);
        var spec = BuildTypeSpecObject(objectType, SpecTypeReferenceName(objectType, contractType));

        return (
            new TsNamedType("TypeSpec", [contractType], JsonRuntimeModule.Origin),
            ContextFactoryCall("createSpec", spec, ExplicitTypeArgumentsOf(contractType))
        );
    }

    private static IReadOnlyList<TsType>? ExplicitTypeArgumentsOf(TsType contractType) =>
        contractType is TsNamedType { TypeArguments.Count: > 0 } ? [contractType] : null;

    private static TsCallExpression ContextFactoryCall(
        string factoryName,
        TsObjectLiteral spec,
        IReadOnlyList<TsType>? typeArguments = null
    ) =>
        new(
            new TsPropertyAccess(new TsIdentifier("this"), factoryName),
            [spec],
            TypeArguments: typeArguments
        );

    /// <summary>
    /// The <c>T</c> of a value root's <c>ValueSpec&lt;T&gt;</c>: the root's
    /// full mapped TS type (FR-032), plus <c>| null</c> for a reference type,
    /// whose JSON <c>null</c> reads back as <c>null</c> (FR-037). Value types
    /// add nothing; <c>Nullable&lt;T&gt;</c> already maps to <c>T | null</c>.
    /// </summary>
    private TsType ValueSpecTypeArgument(ITypeSymbol root)
    {
        var mapped = JsonTsTypes.Map(_context, root);

        return root.IsReferenceType ? IrToTsTypeMapper.MakeNullable(mapped) : mapped;
    }

    /// <summary>
    /// The TS value a spec names in <c>type: T</c> and <c>new T(...)</c>: the
    /// mapped type name without type arguments, which is qualified for a
    /// nested type (<c>Outer.Inner</c>) exactly as property types reference
    /// it. <c>TypeSpec&lt;T&gt;</c> carries the full mapped
    /// <paramref name="contractType"/> instead
    /// (<c>TypeSpec&lt;Page&lt;EpisodeSummary&gt;&gt;</c>, FR-032).
    /// </summary>
    private string SpecTypeReferenceName(INamedTypeSymbol type, TsType contractType) =>
        contractType is TsNamedType namedTs ? namedTs.Name : _context.ResolveTsName(type);

    /// <summary>
    /// Builds the TypeSpec object literal for a given type:
    /// <c>{ type: T, factory: (p) => new T(...), properties: [...] }</c>.
    /// The factory reads the properties in declaration order; the
    /// <c>properties</c> list follows the wire order (FR-009). A polymorphic
    /// base adds <c>polymorphism</c> as the last member (FR-004).
    /// </summary>
    private TsObjectLiteral BuildTypeSpecObject(INamedTypeSymbol targetType, string tsTypeName)
    {
        var polymorphism = _polymorphisms.GetValueOrDefault(targetType);

        var factory = BuildFactoryBody(targetType, tsTypeName, polymorphism);

        // factory: (p) => new T(...) for classes, (p) => ({ ... }) for PlainObject,
        // () => { throw ... } for a spec that cannot construct. The payload
        // parameter exists only when the built body reads it (#480): a
        // declared-but-unused `p` trips biome's noUnusedFunctionParameters in
        // every consumer project.
        TsParameter[] factoryParameters = factory.ReadsPayload ? [PayloadParameter()] : [];

        var factoryProperty = new TsObjectProperty(
            "factory",
            new TsArrowFunction(factoryParameters, [factory.Statement])
        );

        var propertiesProperty = new TsObjectProperty(
            "properties",
            new TsArrayLiteral(
                _properties.WirePropertiesOf(targetType).Select(BuildPropertySpec).ToList()
            )
        );

        TsObjectProperty?[] leadingProperties =
        [
            TypeProperty(targetType, tsTypeName),
            BaseLinkProperty(targetType),
            factoryProperty,
            propertiesProperty,
        ];

        TsObjectProperty?[] trailingProperties =
        [
            IgnoredPropertiesProperty(targetType),
            AssignIfPresentProperty(targetType),
            polymorphism is null ? null : _polymorphismSpecs.Build(polymorphism),
        ];

        return new TsObjectLiteral([
            .. leadingProperties.OfType<TsObjectProperty>(),
            .. ValueTypeProperties(targetType),
            .. trailingProperties.OfType<TsObjectProperty>(),
        ]);
    }

    /// <summary>
    /// <c>type: TodoItem</c> — only for class-backed types. [PlainObject] types
    /// and C# interfaces lower to TS interfaces with no runtime constructor, so
    /// there's no value to reference. Their specs are accessed via the context
    /// getter (e.g., <c>ctx.storedTodo</c>), not via <c>resolve(Class)</c>.
    /// </summary>
    private static TsObjectProperty? TypeProperty(INamedTypeSymbol targetType, string tsTypeName) =>
        JsonContractAdmission.HasRuntimeConstructor(targetType)
            ? new TsObjectProperty("type", new TsIdentifier(tsTypeName))
            : null;

    /// <summary><c>base: this.baseSpec</c> when the type links to a base spec.</summary>
    private TsObjectProperty? BaseLinkProperty(INamedTypeSymbol targetType) =>
        _properties.LinkedBaseOf(targetType) is { } baseType
            ? new TsObjectProperty(
                "base",
                new TsPropertyAccess(new TsIdentifier("this"), _names.GetterOf(baseType))
            )
            : null;

    /// <summary>
    /// <c>ignoredProperties: ["name"]</c> when the type hides base
    /// declarations of a linked spec with a <c>[JsonIgnore]</c> override
    /// (DECIDED-19 e); absent otherwise.
    /// </summary>
    private TsObjectProperty? IgnoredPropertiesProperty(INamedTypeSymbol targetType)
    {
        var ignored = _properties.IgnoredBasePropertiesOf(targetType);

        if (ignored.Count == 0)
            return null;

        return new TsObjectProperty(
            "ignoredProperties",
            new TsArrayLiteral(
                ignored.Select(name => new TsStringLiteral(name)).ToList<TsExpression>()
            )
        );
    }

    /// <summary>
    /// The spec's <c>factory</c> arrow body and whether it reads the payload:
    /// the parameter exists only when it does (#480), so the decision comes
    /// from the build itself — a new body shape can never read a payload the
    /// arrow fails to declare.
    /// </summary>
    private sealed record FactoryBody(TsStatement Statement, bool ReadsPayload)
    {
        public static FactoryBody Throws(string message) =>
            new(BuildAbstractFactoryThrow(message), ReadsPayload: false);
    }

    /// <summary>
    /// The spec's <c>factory</c> arrow body, with whether it reads the
    /// payload (the parameter exists only then, #480):
    /// <list type="bullet">
    ///   <item>abstract polymorphic base (a class, a <c>[PlainObject]</c> type
    ///     or an interface) → <c>throw new Error(...)</c>: its reads dispatch
    ///     to a derived contract and never reach it (FR-011, FR-018);</item>
    ///   <item><c>[PlainObject]</c> → <c>return { title: p.title as string, ... }</c>,
    ///     inherited members included: a TS interface has no base constructor
    ///     to fill them; reads the payload when the literal is non-empty;</item>
    ///   <item>any other abstract class → <c>throw new Error(...)</c>:
    ///     TypeScript rejects <c>new</c> on an abstract class, so it cannot be
    ///     deserialized directly;</item>
    ///   <item>class → <c>return new TodoItem(p.title as string, ...)</c>, with
    ///     the arguments in the emitted constructor's positional order, inside
    ///     <c>Object.assign(…, { … })</c> when members are assigned after
    ///     construction; reads the payload when any argument casts it or any
    ///     member is assigned.</item>
    /// </list>
    /// </summary>
    private FactoryBody BuildFactoryBody(
        INamedTypeSymbol targetType,
        string tsTypeName,
        JsonPolymorphism? polymorphism
    )
    {
        if (FactoryOnlyThrows(targetType, polymorphism))
        {
            return FactoryBody.Throws(
                polymorphism is { IsAbstractBase: true }
                    ? PolymorphicAbstractFactoryMessage(targetType, tsTypeName)
                    : NonConstructibleFactoryMessage(targetType, tsTypeName)
            );
        }

        if (SymbolHelper.HasPlainObject(targetType))
        {
            var literalProperties = _properties.LiteralPropertiesOf(targetType);

            return new(
                new TsReturnStatement(BuildPlainObjectLiteral(literalProperties)),
                ReadsPayload: literalProperties.Count > 0
            );
        }

        var construction = BuildConstruction(targetType, tsTypeName, out var argumentsReadPayload);
        var assignedMembers = UnboundMembersOf(_properties, targetType, JsonReadAssignment.Always)
            .Select(ToCastProperty)
            .ToList();

        return new(
            new TsReturnStatement(WithAssignedMembers(construction, assignedMembers)),
            ReadsPayload: argumentsReadPayload || assignedMembers.Count > 0
        );
    }

    /// <summary>
    /// <c>new TodoItem(p.title as string, ...)</c>, or — for a type that
    /// constructs through its overload factories — the factory of the
    /// constructor System.Text.Json picks, with the same arguments:
    /// <c>Money.fromAmount(p.amount as Decimal)</c> (spec 013 FR-035).
    /// Reports whether any argument casts the payload.
    /// </summary>
    private TsExpression BuildConstruction(
        INamedTypeSymbol targetType,
        string tsTypeName,
        out bool readsPayload
    )
    {
        var constructor = JsonConstructorSelection.Of(targetType);
        var arguments = BuildConstructorArguments(constructor, out readsPayload);
        var factory = JsonConstructorSelection.FactoryOf(constructor, _context.Extraction);

        if (factory is null)
            return new TsNewExpression(new TsIdentifier(tsTypeName), arguments);

        return new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier(tsTypeName), factory),
            arguments
        );
    }

    /// <summary>
    /// <paramref name="construction"/>, or
    /// <c>Object.assign(new T(...), { c: p.c as number })</c> wrapping it
    /// when <paramref name="assignedMembers"/> is non-empty: the contract's
    /// unbound members System.Text.Json always sets through the object
    /// initializer (<c>init</c> or <c>required</c>,
    /// <see cref="JsonReadAssignment.Always"/>), each with its most-derived
    /// declaration. Inherited members count, since a linked spec's factory
    /// builds the whole object, except the base declarations a
    /// <c>[JsonIgnore]</c> override hides, which the payload never carries
    /// (measured on .NET 10: <c>record IncRec(int A) { [JsonInclude] internal
    /// int B { get; init; } public int C { get; init; } }</c> reads
    /// <c>{"A":5,"B":6,"C":7}</c> as <c>5, 6, 7</c>, and <c>record R(int A) {
    /// public string? Note { get; init; } = "x"; }</c> reads <c>{"A":1}</c>
    /// with <c>Note = null</c>). Members set only when present are assigned by
    /// the runtime instead (<see cref="AssignIfPresentProperty"/>).
    /// </summary>
    private static TsExpression WithAssignedMembers(
        TsExpression construction,
        List<TsObjectProperty> assignedMembers
    )
    {
        if (assignedMembers.Count == 0)
            return construction;

        return new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("Object"), "assign"),
            [construction, new TsObjectLiteral(assignedMembers)]
        );
    }

    /// <summary>
    /// <c>assignIfPresent: ["x", "y"]</c>: the TS names of the members of this
    /// contract, inherited ones included, that no parameter of its own
    /// constructor binds and that have a <c>set</c> accessor or are writable
    /// fields (<see cref="JsonReadAssignment.IfPresent"/>). The runtime
    /// assigns them after the factory ran, only when the payload carries
    /// their key, so a missing key keeps the constructed value (measured:
    /// <c>class Init { string? Note = "init"; int Count = 5; }</c> reads
    /// <c>{"Count":1}</c> as <c>{"Note":"init","Count":1}</c>, and
    /// <c>class DX : BX { DX() : base(0) {} }</c> reads <c>{"X":5,"Y":6}</c>
    /// with <c>X = 5</c> although <c>BX</c>'s constructor binds <c>X</c>).
    /// The list belongs to the contract, not to the spec declaring the
    /// member, because each contract's constructor binds different members.
    /// Absent when the factory constructs nothing (an abstract type, a
    /// <c>[PlainObject]</c> literal, an interface) or the list is empty.
    /// </summary>
    private TsObjectProperty? AssignIfPresentProperty(INamedTypeSymbol targetType)
    {
        var constructsInstance =
            targetType.TypeKind != TypeKind.Interface
            && !targetType.IsAbstract
            && !SymbolHelper.HasPlainObject(targetType);

        if (!constructsInstance)
            return null;

        var names = UnboundMembersOf(_properties, targetType, JsonReadAssignment.IfPresent)
            .Select(property => (TsExpression)new TsStringLiteral(property.TsName))
            .ToList();

        return names.Count == 0
            ? null
            : new TsObjectProperty("assignIfPresent", new TsArrayLiteral(names));
    }

    /// <summary>
    /// The contract's members no constructor parameter binds that a read
    /// assigns as <paramref name="assignment"/>.
    /// </summary>
    private static IEnumerable<JsonSerializableProperty> UnboundMembersOf(
        JsonPropertyCollector properties,
        INamedTypeSymbol targetType,
        JsonReadAssignment assignment
    )
    {
        var boundNames = ConstructorBoundMemberNames(targetType);

        return properties
            .LiteralPropertiesOf(targetType)
            .Where(property => !boundNames.Contains(property.Member.Name))
            .Where(property => properties.Access.ReadAssignmentOf(property.Member) == assignment);
    }

    /// <summary>
    /// The C# names of the members the constructor's parameters bind,
    /// matched ignoring case as System.Text.Json binds them.
    /// </summary>
    private static HashSet<string> ConstructorBoundMemberNames(INamedTypeSymbol targetType) =>
        ParametersOf(JsonConstructorSelection.Of(targetType))
            .Select(parameter =>
                SymbolHelper
                    .FindInstancePropertyInHierarchy(parameter.ContainingType, parameter.Name)
                    ?.Name
                ?? parameter.Name
            )
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every C# type the context's factories cast (<c>p.x as T</c>): for a
    /// <c>[PlainObject]</c> its literal members, otherwise the property each
    /// constructor parameter binds (the parameter's own type when no property
    /// matches, skipping the extension-data overflow that casts nothing) and
    /// the members a read assigns after construction
    /// (<see cref="JsonReadAssignment.Always"/>). A contract whose factory
    /// only throws is skipped in the same order the factory body build
    /// decides, so no import dangles for a cast that never happens (#496).
    /// The requirement scanner reads the result so the runtime helpers the
    /// cast types spell — <c>HashSet</c> in
    /// <c>p.items as HashSet&lt;number&gt;</c>, <c>Temporal</c> in
    /// <c>p.day as Temporal.PlainDate</c> — land in the context's import
    /// table (FR-041).
    /// </summary>
    internal static IReadOnlyCollection<ITypeSymbol> FactoryCastTypesOf(JsonContextPlan plan)
    {
        var types = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var entry in plan.Entries)
        {
            if (entry.Kind != JsonPropertyKind.Ref)
                continue;

            var contract = (INamedTypeSymbol)entry.Type;
            var polymorphism = plan.Polymorphisms.GetValueOrDefault(contract);

            if (FactoryOnlyThrows(contract, polymorphism))
                continue;

            types.UnionWith(CastTypesOf(plan, contract));
        }

        return types;
    }

    /// <summary>
    /// Whether a contract's factory only throws instead of constructing: an
    /// abstract polymorphic base, or any other abstract type (interfaces
    /// included, since Roslyn reports them abstract) that is not a
    /// [PlainObject], whose factory builds a literal instead. The factory
    /// build and the cast-type scan both decide through it (#496).
    /// </summary>
    private static bool FactoryOnlyThrows(
        INamedTypeSymbol contract,
        JsonPolymorphism? polymorphism
    ) =>
        polymorphism is { IsAbstractBase: true }
        || (contract.IsAbstract && !SymbolHelper.HasPlainObject(contract));

    /// <summary>
    /// The cast types of one constructible contract: its
    /// <c>[PlainObject]</c> literal members, or its constructor-bound
    /// parameter types plus the members assigned after construction.
    /// </summary>
    private static IEnumerable<ITypeSymbol> CastTypesOf(
        JsonContextPlan plan,
        INamedTypeSymbol contract
    )
    {
        if (SymbolHelper.HasPlainObject(contract))
            return plan.Properties.LiteralPropertiesOf(contract).Select(property => property.Type);

        var parameterTypes = ParametersOf(JsonConstructorSelection.Of(contract))
            .Select(CastTypeOf)
            .OfType<ITypeSymbol>();

        var assignedTypes = UnboundMembersOf(plan.Properties, contract, JsonReadAssignment.Always)
            .Select(property => property.Type);

        return parameterTypes.Concat(assignedTypes);
    }

    /// <summary>
    /// The type a constructor parameter's factory argument casts
    /// (<c>p.x as T</c>): the property it binds across the inheritance chain,
    /// or the parameter's own type when no property matches; <c>null</c> for
    /// the extension-data overflow member, which is left out of the spec, so
    /// the factory emits a bare <c>null</c> and casts nothing.
    /// </summary>
    private static ITypeSymbol? CastTypeOf(IParameterSymbol parameter)
    {
        var property = SymbolHelper.FindInstancePropertyInHierarchy(
            parameter.ContainingType,
            parameter.Name
        );

        if (
            property is not null
            && JsonSerializationAttributes.Has(property, JsonSerializationAttributes.ExtensionData)
        )
            return null;

        return property?.Type ?? parameter.Type;
    }

    private TsObjectLiteral BuildPlainObjectLiteral(
        IReadOnlyList<JsonSerializableProperty> properties
    ) => new(properties.Select(ToCastProperty).ToList());

    /// <summary><c>name: p.name as T</c>, one member of a factory's object literal.</summary>
    private TsObjectProperty ToCastProperty(JsonSerializableProperty property) =>
        new(property.TsName, CastProperty(property.TsName, property.Type));

    /// <summary>
    /// Builds the <c>new T(...)</c> arguments: each parameter of the
    /// constructor (in declaration order — the order the emitted TS
    /// constructor uses) reads the property it initializes, searched across
    /// the inheritance chain so a parameter forwarded to the base
    /// (<c>ImageClip(string Path, double Seconds) : Clip(Seconds)</c>) reads
    /// the inherited <c>seconds</c>. A type without a parameterized
    /// constructor is built with <c>new T()</c> and its members are assigned
    /// (<see cref="WithAssignedMembers"/>). Reports whether any argument
    /// casts the payload: an extension-data overflow emits a bare
    /// <c>null</c>, reading nothing.
    /// </summary>
    private List<TsExpression> BuildConstructorArguments(
        IMethodSymbol? constructor,
        out bool readsPayload
    )
    {
        var parameters = ParametersOf(constructor).ToList();

        readsPayload = parameters.Any(parameter => CastTypeOf(parameter) is not null);

        return parameters.Select(BuildConstructorArgument).ToList();
    }

    private static IEnumerable<IParameterSymbol> ParametersOf(IMethodSymbol? constructor) =>
        constructor?.Parameters ?? [];

    /// <summary>
    /// The payload parameter of a spec's factory arrow:
    /// <c>p: Record&lt;string, unknown&gt;</c>, declared only when the built
    /// body reads it (#480).
    /// </summary>
    private static TsParameter PayloadParameter() =>
        new(
            PayloadParameterName,
            new TsNamedType("Record", [new TsStringType(), new TsNamedType("unknown")])
        );

    private TsExpression BuildConstructorArgument(IParameterSymbol parameter)
    {
        var property = SymbolHelper.FindInstancePropertyInHierarchy(
            parameter.ContainingType,
            parameter.Name
        );

        if (property is null)
            return CastProperty(TypeScriptNaming.ToCamelCase(parameter.Name), parameter.Type);

        // The overflow member is left out of the spec, so p carries no value for it.
        if (JsonSerializationAttributes.Has(property, JsonSerializationAttributes.ExtensionData))
            return new TsLiteral("null");

        var tsName = IrToTsNamingPolicy.ToDataMemberName(
            property.Name,
            SymbolHelper.GetNameOverride(property, TargetLanguage.TypeScript)
        );

        return CastProperty(tsName, property.Type);
    }

    private TsExpression CastProperty(string tsName, ITypeSymbol type) =>
        new TsCastExpression(
            new TsPropertyAccess(new TsIdentifier(PayloadParameterName), tsName),
            JsonTsTypes.Map(_context, type)
        );

    /// <summary>
    /// Builds a property spec object literal:
    /// <c>{ ts: "name", json: "name", type: { kind: "..." }, optional?: true, order?: n }</c>,
    /// where <c>order</c> is a non-zero <c>[JsonPropertyOrder]</c>: the
    /// runtime sorts the whole base chain by it (FR-046).
    /// </summary>
    private TsObjectLiteral BuildPropertySpec(JsonSerializableProperty property)
    {
        var type = property.Type;

        var specProperties = new[]
        {
            new TsObjectProperty("ts", new TsStringLiteral(property.TsName)),
            new TsObjectProperty("json", new TsStringLiteral(property.JsonName)),
            new TsObjectProperty("type", _descriptors.Build(type)),
            JsonTypeClassifier.IsNullable(type)
                ? new TsObjectProperty("optional", new TsLiteral("true"))
                : null,
            property.Order == 0
                ? null
                : new TsObjectProperty(
                    "order",
                    new TsLiteral(property.Order.ToString(CultureInfo.InvariantCulture))
                ),
        };

        return new TsObjectLiteral(specProperties.OfType<TsObjectProperty>().ToList());
    }

    private static TsThrowStatement BuildAbstractFactoryThrow(string message) =>
        new(new TsNewExpression(new TsIdentifier("Error"), [new TsStringLiteral(message)]));

    /// <summary>
    /// The text the factory of an abstract polymorphic base throws: FR-011's,
    /// naming an interface base by its C# name (FR-018).
    /// </summary>
    private static string PolymorphicAbstractFactoryMessage(
        INamedTypeSymbol targetType,
        string tsTypeName
    )
    {
        var (kind, name) =
            targetType.TypeKind == TypeKind.Interface
                ? ("interface", targetType.Name)
                : ("abstract", tsTypeName);

        return $"Cannot deserialize {kind} type '{name}' directly; deserialize it through a "
            + "polymorphic spec.";
    }

    /// <summary>
    /// The text the factory of a non-polymorphic abstract type throws: the
    /// real reason is the abstractness itself — TypeScript rejects
    /// <c>new</c> on it — not the long-shipped polymorphism claim (#489).
    /// The runtime throws System.Text.Json's abstract-type wording
    /// (<c>Deserialization of interface or abstract types is not supported.
    /// Type 'X'.</c>, measured on .NET 10) when a read dispatches here, so
    /// this generated throw is unreachable and states only why the spec
    /// cannot build. The kind-aware naming mirrors FR-018 because
    /// <see cref="INamedTypeSymbol.IsAbstract"/> is true for interfaces too:
    /// no non-polymorphic interface entry exists today for this branch to
    /// build a factory from (an interface root is rejected with
    /// <c>MS0035</c>, and a ref to an interface member claims no entry), but
    /// an interface that ever reaches it must not read as abstract.
    /// </summary>
    private static string NonConstructibleFactoryMessage(
        INamedTypeSymbol targetType,
        string tsTypeName
    )
    {
        var (kind, name) =
            targetType.TypeKind == TypeKind.Interface
                ? ("interface", targetType.Name)
                : ("abstract", tsTypeName);

        return $"Cannot construct {kind} type '{name}'.";
    }
}
