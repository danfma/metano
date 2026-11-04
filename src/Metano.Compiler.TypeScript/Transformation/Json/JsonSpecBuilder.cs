using Metano.Annotations;
using Metano.Compiler;
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
    private static readonly TsTypeOrigin RuntimeJsonOrigin = new("metano-runtime", "system/json");

    private readonly TypeScriptTransformContext _context;
    private readonly JsonTypeInfoNames _names;
    private readonly JsonPropertyCollector _properties;
    private readonly IReadOnlyDictionary<ITypeSymbol, JsonPolymorphism> _polymorphisms;
    private readonly JsonTypeDescriptorBuilder _descriptors;
    private readonly JsonPolymorphismSpecBuilder _polymorphismSpecs;

    public JsonSpecBuilder(
        TypeScriptTransformContext context,
        JsonTypeInfoNames names,
        JsonPropertyCollector properties,
        IReadOnlyDictionary<ITypeSymbol, JsonPolymorphism> polymorphisms
    )
    {
        _context = context;
        _names = names;
        _properties = properties;
        _polymorphisms = polymorphisms;
        _descriptors = new JsonTypeDescriptorBuilder(context, names);
        _polymorphismSpecs = new JsonPolymorphismSpecBuilder(
            _descriptors,
            new JsonGuardReferences(context)
        );
    }

    /// <summary>
    /// Whether the TS type of an object contract is a class the spec can name
    /// in <c>type: T</c> and construct: <c>[PlainObject]</c> types lower to
    /// TS interfaces, and a C# interface (FR-018) has no constructor either.
    /// </summary>
    public static bool HasRuntimeConstructor(INamedTypeSymbol type) =>
        type.TypeKind != TypeKind.Interface && !SymbolHelper.HasPlainObject(type);

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
            new TsNamedType("ValueSpec", [ValueSpecTypeArgument(entry.Type)], RuntimeJsonOrigin),
            ContextFactoryCall(
                "createValueSpec",
                new TsObjectLiteral([new TsObjectProperty("descriptor", descriptor)])
            )
        );
    }

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
            new TsNamedType("TypeSpec", [contractType], RuntimeJsonOrigin),
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
        var properties = _properties.DeclarationOrderPropertiesOf(targetType);
        var polymorphism = _polymorphisms.GetValueOrDefault(targetType);

        var factoryParameter = new TsParameter(
            "p",
            new TsNamedType("Record", [new TsStringType(), new TsNamedType("unknown")])
        );

        // factory: (p) => new T(...) for classes, (p) => ({ ... }) for PlainObject
        var factoryProperty = new TsObjectProperty(
            "factory",
            new TsArrowFunction(
                [factoryParameter],
                [BuildFactoryStatement(targetType, tsTypeName, properties, polymorphism)]
            )
        );

        var propertiesProperty = new TsObjectProperty(
            "properties",
            new TsArrayLiteral(
                _properties.WirePropertiesOf(targetType).Select(BuildPropertySpec).ToList()
            )
        );

        var specProperties = new[]
        {
            TypeProperty(targetType, tsTypeName),
            BaseLinkProperty(targetType),
            factoryProperty,
            propertiesProperty,
            IgnoredPropertiesProperty(targetType),
            polymorphism is null ? null : _polymorphismSpecs.Build(polymorphism),
        };

        return new TsObjectLiteral(specProperties.OfType<TsObjectProperty>().ToList());
    }

    /// <summary>
    /// <c>type: TodoItem</c> — only for class-backed types. [PlainObject] types
    /// and C# interfaces lower to TS interfaces with no runtime constructor, so
    /// there's no value to reference. Their specs are accessed via the context
    /// getter (e.g., <c>ctx.storedTodo</c>), not via <c>resolve(Class)</c>.
    /// </summary>
    private static TsObjectProperty? TypeProperty(INamedTypeSymbol targetType, string tsTypeName) =>
        HasRuntimeConstructor(targetType)
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
    /// The single statement of the spec's <c>factory</c> arrow:
    /// <list type="bullet">
    ///   <item>abstract polymorphic base (a class, a <c>[PlainObject]</c> type
    ///     or an interface) → <c>throw new Error(...)</c>: its reads dispatch
    ///     to a derived contract and never reach it (FR-011, FR-018);</item>
    ///   <item><c>[PlainObject]</c> → <c>return { title: p.title as string, ... }</c>,
    ///     inherited members included: a TS interface has no base constructor
    ///     to fill them;</item>
    ///   <item>any other abstract class → <c>throw new Error(...)</c>:
    ///     TypeScript rejects <c>new</c> on an abstract class, so it cannot be
    ///     deserialized directly;</item>
    ///   <item>class → <c>return new TodoItem(p.title as string, ...)</c>, with
    ///     the arguments in the emitted constructor's positional order.</item>
    /// </list>
    /// </summary>
    private TsStatement BuildFactoryStatement(
        INamedTypeSymbol targetType,
        string tsTypeName,
        IReadOnlyList<JsonSerializableProperty> properties,
        JsonPolymorphism? polymorphism
    )
    {
        if (polymorphism is { IsAbstractBase: true })
            return BuildAbstractFactoryThrow(
                PolymorphicAbstractFactoryMessage(targetType, tsTypeName)
            );

        if (SymbolHelper.HasPlainObject(targetType))
            return new TsReturnStatement(
                BuildPlainObjectLiteral(_properties.LiteralPropertiesOf(targetType))
            );

        if (targetType.IsAbstract)
            return BuildAbstractFactoryThrow(UnsupportedAbstractFactoryMessage(tsTypeName));

        return new TsReturnStatement(
            new TsNewExpression(
                new TsIdentifier(tsTypeName),
                BuildConstructorArguments(targetType, properties)
            )
        );
    }

    private TsObjectLiteral BuildPlainObjectLiteral(
        IReadOnlyList<JsonSerializableProperty> properties
    ) =>
        new(
            properties
                .Select(property => new TsObjectProperty(
                    property.TsName,
                    CastProperty(property.TsName, property.Property.Type)
                ))
                .ToList()
        );

    /// <summary>
    /// Builds the <c>new T(...)</c> arguments. When the type has a
    /// constructor with parameters, each parameter (in declaration order —
    /// the order the emitted TS constructor uses) reads the property it
    /// initializes, searched across the inheritance chain so a parameter
    /// forwarded to the base (<c>ImageClip(string Path, double Seconds) :
    /// Clip(Seconds)</c>) reads the inherited <c>seconds</c>. Types without
    /// a parameterized constructor keep passing their own properties.
    /// </summary>
    private List<TsExpression> BuildConstructorArguments(
        INamedTypeSymbol targetType,
        IReadOnlyList<JsonSerializableProperty> properties
    )
    {
        var parameters =
            IrConstructorExtractor.FindPrimaryConstructor(targetType)?.Parameters ?? [];

        if (parameters.Length == 0)
            return properties
                .Select(property => CastProperty(property.TsName, property.Property.Type))
                .ToList();

        return parameters.Select(BuildConstructorArgument).ToList();
    }

    private TsExpression BuildConstructorArgument(IParameterSymbol parameter)
    {
        var property = SymbolHelper.FindInstancePropertyInHierarchy(
            parameter.ContainingType,
            parameter.Name
        );

        if (property is null)
            return CastProperty(TypeScriptNaming.ToCamelCase(parameter.Name), parameter.Type);

        var tsName = IrToTsNamingPolicy.ToDataMemberName(
            property.Name,
            SymbolHelper.GetNameOverride(property, TargetLanguage.TypeScript)
        );

        return CastProperty(tsName, property.Type);
    }

    private TsExpression CastProperty(string tsName, ITypeSymbol type) =>
        new TsCastExpression(
            new TsPropertyAccess(new TsIdentifier("p"), tsName),
            JsonTsTypes.Map(_context, type)
        );

    /// <summary>
    /// Builds a property spec object literal:
    /// <c>{ ts: "name", json: "name", type: { kind: "..." }, optional?: true }</c>.
    /// </summary>
    private TsObjectLiteral BuildPropertySpec(JsonSerializableProperty property)
    {
        var type = property.Property.Type;

        var specProperties = new[]
        {
            new TsObjectProperty("ts", new TsStringLiteral(property.TsName)),
            new TsObjectProperty("json", new TsStringLiteral(property.JsonName)),
            new TsObjectProperty("type", _descriptors.Build(type)),
            JsonTypeClassifier.IsNullable(type)
                ? new TsObjectProperty("optional", new TsLiteral("true"))
                : null,
        };

        return new TsObjectLiteral(specProperties.OfType<TsObjectProperty>().ToList());
    }

    private static TsThrowStatement BuildAbstractFactoryThrow(string message) =>
        new(new TsNewExpression(new TsIdentifier("Error"), [new TsStringLiteral(message)]));

    /// <summary>
    /// The text the factory of an abstract polymorphic base throws: FR-011's,
    /// naming an interface base by its C# name (FR-018). A non-polymorphic
    /// abstract type keeps the #238 text.
    /// </summary>
    private static string PolymorphicAbstractFactoryMessage(
        INamedTypeSymbol targetType,
        string tsTypeName
    ) =>
        targetType.TypeKind == TypeKind.Interface
            ? $"Cannot deserialize interface type '{targetType.Name}' directly; deserialize it "
                + "through a polymorphic spec."
            : $"Cannot deserialize abstract type '{tsTypeName}' directly; deserialize it through "
                + "a polymorphic spec.";

    private static string UnsupportedAbstractFactoryMessage(string tsTypeName) =>
        $"Cannot deserialize abstract type '{tsTypeName}': polymorphic JSON deserialization is "
        + "not supported yet.";
}
