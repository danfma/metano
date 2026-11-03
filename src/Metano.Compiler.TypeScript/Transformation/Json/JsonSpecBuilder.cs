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
/// contract or a <c>ValueSpec</c> for a value root, with the runtime
/// <c>TypeDescriptor</c> of every property and value root. Every
/// <c>ref</c> descriptor and <c>base</c> link names its target through
/// <see cref="JsonTypeInfoNames"/>.
/// </summary>
internal sealed class JsonSpecBuilder(
    TypeScriptTransformContext context,
    JsonTypeInfoNames names,
    JsonPropertyCollector properties
)
{
    private static readonly TsTypeOrigin RuntimeJsonOrigin = new("metano-runtime", "system/json");

    private readonly TypeScriptTransformContext _context = context;
    private readonly JsonTypeInfoNames _names = names;
    private readonly JsonPropertyCollector _properties = properties;

    private TsType MapType(ITypeSymbol symbol) =>
        IrToTsTypeMapper.Map(
            IrTypeRefMapper.Map(symbol, _context.OriginResolver, TargetLanguage.TypeScript),
            _context.BclOverrides
        );

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
        var (specType, factoryName, spec) = BuildSpec(entry);

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
                        new TsCallExpression(
                            new TsPropertyAccess(new TsIdentifier("this"), factoryName),
                            [spec]
                        )
                    )
                ),
            ]
        );
    }

    private (TsType SpecType, string FactoryName, TsObjectLiteral Spec) BuildSpec(
        JsonContractEntry entry
    )
    {
        if (entry.Kind == JsonPropertyKind.Ref)
        {
            var objectType = (INamedTypeSymbol)entry.Type;
            var tsTypeName = SpecTypeReferenceName(objectType);

            return (
                new TsNamedType("TypeSpec", [new TsNamedType(tsTypeName)], RuntimeJsonOrigin),
                "createSpec",
                BuildTypeSpecObject(objectType, tsTypeName)
            );
        }

        return (
            new TsNamedType("ValueSpec", [ValueSpecTypeArgument(entry.Type)], RuntimeJsonOrigin),
            "createValueSpec",
            new TsObjectLiteral([
                new TsObjectProperty("descriptor", BuildTypeDescriptor(entry.Type, entry.Kind)),
            ])
        );
    }

    /// <summary>
    /// The <c>T</c> of a value root's <c>ValueSpec&lt;T&gt;</c>: the root's
    /// full mapped TS type (FR-032), plus <c>| null</c> for a reference type,
    /// whose JSON <c>null</c> reads back as <c>null</c> (FR-037). Value types
    /// add nothing; <c>Nullable&lt;T&gt;</c> already maps to <c>T | null</c>.
    /// </summary>
    private TsType ValueSpecTypeArgument(ITypeSymbol root)
    {
        var mapped = MapType(root);

        return root.IsReferenceType ? IrToTsTypeMapper.MakeNullable(mapped) : mapped;
    }

    /// <summary>
    /// The TS type reference a spec names in <c>TypeSpec&lt;T&gt;</c>,
    /// <c>type: T</c> and <c>new T(...)</c>: the mapped type name, which is
    /// qualified for a nested type (<c>Outer.Inner</c>) exactly as property
    /// types reference it.
    /// </summary>
    private string SpecTypeReferenceName(INamedTypeSymbol type) =>
        MapType(type) is TsNamedType namedTs ? namedTs.Name : _context.ResolveTsName(type);

    /// <summary>
    /// Builds the TypeSpec object literal for a given type:
    /// { type: T, factory: (p) => new T(...), properties: [...] }
    /// </summary>
    private TsObjectLiteral BuildTypeSpecObject(INamedTypeSymbol targetType, string tsTypeName)
    {
        var properties = _properties.PropertiesOf(targetType);
        var propSpecs = new List<TsExpression>();

        foreach (var (prop, jsonName, tsName) in properties)
        {
            var descriptor = JsonTypeClassifier.Classify(prop.Type);
            propSpecs.Add(BuildPropertySpec(tsName, jsonName, descriptor, prop));
        }

        var isPlainObject = SymbolHelper.HasPlainObject(targetType);

        // type: TodoItem — only for class-backed types. [PlainObject] types
        // lower to TS interfaces with no runtime constructor, so there's no
        // value to reference. Their specs are accessed via the context getter
        // (e.g., ctx.storedTodo), not via resolve(Class).
        TsObjectProperty? typeProperty = isPlainObject
            ? null
            : new TsObjectProperty("type", new TsIdentifier(tsTypeName));

        // base: this.baseSpec (if type has a transpilable base)
        TsObjectProperty? baseProperty = null;
        if (_properties.LinkedBaseOf(targetType) is { } baseType)
        {
            baseProperty = new TsObjectProperty(
                "base",
                new TsPropertyAccess(new TsIdentifier("this"), _names.GetterOf(baseType))
            );
        }

        // factory: (p) => new T(...) for classes, (p) => ({ ... }) for PlainObject
        var factoryParam = new TsParameter(
            "p",
            new TsNamedType("Record", [new TsStringType(), new TsNamedType("unknown")])
        );

        var factoryProperty = new TsObjectProperty(
            "factory",
            new TsArrowFunction(
                [factoryParam],
                [BuildFactoryStatement(targetType, tsTypeName, isPlainObject, properties)]
            )
        );

        // properties: [...]
        var propertiesProperty = new TsObjectProperty("properties", new TsArrayLiteral(propSpecs));

        var objectProps = new List<TsObjectProperty>();
        if (typeProperty is not null)
            objectProps.Add(typeProperty);
        if (baseProperty is not null)
            objectProps.Add(baseProperty);
        objectProps.Add(factoryProperty);
        objectProps.Add(propertiesProperty);

        return new TsObjectLiteral(objectProps);
    }

    /// <summary>
    /// The single statement of the spec's <c>factory</c> arrow:
    /// <list type="bullet">
    ///   <item><c>[PlainObject]</c> → <c>return { title: p.title as string, ... }</c>;</item>
    ///   <item>abstract class → <c>throw new Error(...)</c>: TypeScript rejects
    ///     <c>new</c> on an abstract class, and choosing the concrete subtype
    ///     needs polymorphic-JSON support (<c>[JsonPolymorphic]</c>), which is
    ///     not modeled yet;</item>
    ///   <item>class → <c>return new TodoItem(p.title as string, ...)</c>, with
    ///     the arguments in the emitted constructor's positional order.</item>
    /// </list>
    /// </summary>
    private TsStatement BuildFactoryStatement(
        INamedTypeSymbol targetType,
        string tsTypeName,
        bool isPlainObject,
        IReadOnlyList<JsonSerializableProperty> properties
    )
    {
        if (isPlainObject)
            return new TsReturnStatement(BuildPlainObjectLiteral(properties));

        if (targetType.IsAbstract)
            return BuildAbstractFactoryThrow(tsTypeName);

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
                .Select(x => new TsObjectProperty(
                    x.TsName,
                    CastProperty(x.TsName, x.Property.Type)
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
            return properties.Select(x => CastProperty(x.TsName, x.Property.Type)).ToList();

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
        new TsCastExpression(new TsPropertyAccess(new TsIdentifier("p"), tsName), MapType(type));

    /// <summary>
    /// Wire-format string for the runtime <c>TypeDescriptor.kind</c>
    /// field. Kept as a single-source-of-truth mapping so the on-disk
    /// shape can never drift from the enum.
    /// </summary>
    private static string GetWireKind(JsonPropertyKind kind) =>
        kind switch
        {
            JsonPropertyKind.Primitive => "primitive",
            JsonPropertyKind.Nullable => "nullable",
            JsonPropertyKind.Branded => "branded",
            JsonPropertyKind.Decimal => "decimal",
            JsonPropertyKind.Temporal => "temporal",
            JsonPropertyKind.Map => "map",
            JsonPropertyKind.HashSet => "hashSet",
            JsonPropertyKind.Array => "array",
            JsonPropertyKind.Enum => "enum",
            JsonPropertyKind.NumericEnum => "numericEnum",
            JsonPropertyKind.Ref => "ref",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
        };

    /// <summary>
    /// Builds a property spec object literal: { ts: "name", json: "name", type: { kind: "..." } }
    /// </summary>
    private TsObjectLiteral BuildPropertySpec(
        string tsName,
        string jsonName,
        JsonPropertyKind descriptorKind,
        IPropertySymbol prop
    )
    {
        var props = new List<TsObjectProperty>
        {
            new("ts", new TsStringLiteral(tsName)),
            new("json", new TsStringLiteral(jsonName)),
            new("type", BuildTypeDescriptor(prop.Type, descriptorKind)),
        };

        if (
            prop.Type.NullableAnnotation == NullableAnnotation.Annotated
            || prop.Type
                is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                }
        )
        {
            props.Add(new TsObjectProperty("optional", new TsLiteral("true")));
        }

        return new TsObjectLiteral(props);
    }

    /// <summary>
    /// Dispatches to the per-kind descriptor builder. Compiler-enforced
    /// exhaustiveness: a new <see cref="JsonPropertyKind"/> value will
    /// fail to build if no arm is added here.
    /// </summary>
    private TsObjectLiteral BuildTypeDescriptor(ITypeSymbol type, JsonPropertyKind kind) =>
        kind switch
        {
            JsonPropertyKind.Nullable => BuildNullableDescriptor(type),
            JsonPropertyKind.Array => BuildArrayDescriptor(type),
            JsonPropertyKind.Map => BuildMapDescriptor(type),
            JsonPropertyKind.HashSet => BuildHashSetDescriptor(type),
            JsonPropertyKind.Temporal => BuildTemporalDescriptor(type),
            JsonPropertyKind.Branded => BuildBrandedDescriptor(type),
            JsonPropertyKind.Enum => BuildEnumValuesDescriptor(type, JsonPropertyKind.Enum),
            JsonPropertyKind.NumericEnum => BuildEnumValuesDescriptor(
                type,
                JsonPropertyKind.NumericEnum
            ),
            JsonPropertyKind.Ref => BuildRefDescriptor(type),
            JsonPropertyKind.Primitive or JsonPropertyKind.Decimal => BuildBareKindDescriptor(kind),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
        };

    private TsObjectLiteral BuildNullableDescriptor(ITypeSymbol type)
    {
        var innerType = JsonTypeClassifier.UnwrapNullable(type);
        var innerKind = JsonTypeClassifier.Classify(innerType);
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.Nullable)),
            new TsObjectProperty("inner", BuildTypeDescriptor(innerType, innerKind)),
        ]);
    }

    private TsObjectLiteral BuildArrayDescriptor(ITypeSymbol type)
    {
        var elementType = JsonTypeClassifier.ElementTypeOf(type);
        var elementKind = JsonTypeClassifier.Classify(elementType);
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.Array)),
            new TsObjectProperty("element", BuildTypeDescriptor(elementType, elementKind)),
        ]);
    }

    private TsObjectLiteral BuildMapDescriptor(ITypeSymbol type)
    {
        var named = (INamedTypeSymbol)type;
        var keyType = named.TypeArguments[0];
        var valueType = named.TypeArguments[1];
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.Map)),
            new TsObjectProperty(
                "key",
                BuildTypeDescriptor(keyType, JsonTypeClassifier.Classify(keyType))
            ),
            new TsObjectProperty(
                "value",
                BuildTypeDescriptor(valueType, JsonTypeClassifier.Classify(valueType))
            ),
        ]);
    }

    private TsObjectLiteral BuildHashSetDescriptor(ITypeSymbol type)
    {
        var named = (INamedTypeSymbol)type;
        var elementType = named.TypeArguments[0];
        var elementKind = JsonTypeClassifier.Classify(elementType);
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.HashSet)),
            new TsObjectProperty("element", BuildTypeDescriptor(elementType, elementKind)),
        ]);
    }

    private TsObjectLiteral BuildTemporalDescriptor(ITypeSymbol type)
    {
        var tsType = MapType(type);
        var typeName = tsType is TsNamedType namedTs ? namedTs.Name : "Temporal.PlainDateTime";
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.Temporal)),
            new TsObjectProperty("parse", new TsPropertyAccess(new TsIdentifier(typeName), "from")),
        ]);
    }

    /// <summary>
    /// System.Guid maps to the UUID branded type from metano-runtime.
    /// Other branded types come from user-defined <c>[Branded]</c>
    /// structs and use their TS type name directly.
    /// </summary>
    private TsObjectLiteral BuildBrandedDescriptor(ITypeSymbol type)
    {
        var named = (INamedTypeSymbol)type;
        var tsTypeName =
            named.ToDisplayString() == "System.Guid" ? "UUID" : _context.ResolveTsName(named);
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.Branded)),
            new TsObjectProperty(
                "create",
                new TsPropertyAccess(new TsIdentifier(tsTypeName), "create")
            ),
        ]);
    }

    private TsObjectLiteral BuildEnumValuesDescriptor(ITypeSymbol type, JsonPropertyKind kind)
    {
        var tsTypeName = _context.ResolveTsName((INamedTypeSymbol)type);
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(kind)),
            new TsObjectProperty("values", new TsIdentifier(tsTypeName)),
        ]);
    }

    private TsObjectLiteral BuildRefDescriptor(ITypeSymbol type)
    {
        var getterName = _names.GetterOf(type);
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.Ref)),
            new TsObjectProperty(
                "spec",
                new TsArrowFunction(
                    [],
                    [
                        new TsReturnStatement(
                            new TsPropertyAccess(new TsIdentifier("this"), getterName)
                        ),
                    ]
                )
            ),
        ]);
    }

    /// <summary>
    /// Builds a kind-only descriptor for leaf wire kinds with no
    /// payload (primitive / decimal). The runtime <c>SerializerContext</c>
    /// reads the <c>kind</c> field and routes by it without further
    /// fields.
    /// </summary>
    private static TsObjectLiteral BuildBareKindDescriptor(JsonPropertyKind kind) =>
        new([new TsObjectProperty("kind", WireKindLiteral(kind))]);

    private static TsStringLiteral WireKindLiteral(JsonPropertyKind kind) => new(GetWireKind(kind));

    private static TsThrowStatement BuildAbstractFactoryThrow(string tsTypeName) =>
        new(
            new TsNewExpression(
                new TsIdentifier("Error"),
                [
                    new TsStringLiteral(
                        $"Cannot deserialize abstract type '{tsTypeName}': "
                            + "polymorphic JSON deserialization is not supported yet."
                    ),
                ]
            )
        );
}
