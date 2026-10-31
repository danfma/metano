using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Transforms a C# <c>JsonSerializerContext</c> subclass into a TypeScript
/// <c>SerializerContext</c> subclass with pre-computed <c>TypeSpec</c> definitions.
///
/// Reads <c>[JsonSourceGenerationOptions]</c> for the naming policy and
/// <c>[JsonSerializable(typeof(T))]</c> to discover the root types. Like
/// System.Text.Json, the context also carries a spec for every object type
/// reachable from those roots (see <see cref="CollectContractClosure"/>), so each
/// <c>ref</c> descriptor and <c>base</c> link resolves to an emitted getter.
/// JSON property names are pre-computed at transpile time.
/// </summary>
public sealed class JsonSerializerContextTransformer(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    private TsType MapType(ITypeSymbol symbol) =>
        IrToTsTypeMapper.Map(
            IrTypeRefMapper.Map(symbol, _context.OriginResolver, TargetLanguage.TypeScript),
            _context.BclOverrides
        );

    public void Transform(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        var contextName = type.Name;
        var namingPolicy = ReadNamingPolicy(type);
        var rootTypes = ReadSerializableTypes(type);

        if (rootTypes.Count == 0)
            return;

        var specTypes = CollectContractClosure(rootTypes, namingPolicy);

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

        // Per-type: private _typeName? field + get typeName() lazy getter
        foreach (var targetType in specTypes)
        {
            var tsTypeName = SpecTypeReferenceName(targetType);
            var getterName = SpecGetterName(targetType);
            var fieldName = "_" + getterName;
            var runtimeJsonOrigin = new TsTypeOrigin("metano-runtime", "system/json");
            var specType = new TsNamedType(
                "TypeSpec",
                [new TsNamedType(tsTypeName)],
                runtimeJsonOrigin
            );

            // private _todoItem?: TypeSpec<TodoItem>;
            members.Add(
                new TsFieldMember(
                    fieldName,
                    specType,
                    Optional: true,
                    Accessibility: TsAccessibility.Private
                )
            );

            // get todoItem(): TypeSpec<TodoItem> { return this._todoItem ??= this.createSpec({...}); }
            var specObject = BuildTypeSpecObject(targetType, tsTypeName, namingPolicy);
            members.Add(
                new TsGetterMember(
                    getterName,
                    specType,
                    [
                        new TsReturnStatement(
                            new TsBinaryExpression(
                                new TsPropertyAccess(new TsIdentifier("this"), fieldName),
                                "??=",
                                new TsCallExpression(
                                    new TsPropertyAccess(new TsIdentifier("this"), "createSpec"),
                                    [specObject]
                                )
                            )
                        ),
                    ]
                )
            );
        }

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

    /// <summary>
    /// Collects all types from [JsonSerializable(typeof(T))] attributes on the context.
    /// </summary>
    private static List<INamedTypeSymbol> ReadSerializableTypes(INamedTypeSymbol contextType)
    {
        var types = new List<INamedTypeSymbol>();

        foreach (var attr in contextType.GetAttributes())
        {
            if (
                attr.AttributeClass?.Name is not ("JsonSerializableAttribute" or "JsonSerializable")
            )
                continue;

            if (
                attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol targetType
            )
            {
                types.Add(targetType);
            }
        }

        return types;
    }

    /// <summary>
    /// Computes the contract closure of the <c>[JsonSerializable]</c> roots:
    /// the roots in declaration order, followed by every object type reached
    /// through their serializable properties (walking the same nullable /
    /// array / map / hash-set shapes <see cref="BuildTypeDescriptor"/> emits)
    /// or through their <c>base</c> link, in breadth-first discovery order.
    /// Entries are deduplicated by <see cref="SpecGetterName"/>, the same name
    /// <see cref="BuildRefDescriptor"/> and the <c>base</c> link reference, so
    /// cycles (<c>A → B → A</c>) terminate and each getter is emitted once.
    /// Through properties, only non-generic transpilable classes and structs
    /// of the current assembly (nested ones included) are discovered;
    /// interfaces (which need polymorphic JSON), generic types, and types
    /// from referenced packages are not. Base types follow the <c>base</c>-link
    /// rule (<see cref="TranspilableBaseOf"/>), so every emitted <c>base</c>
    /// link has a matching getter.
    /// </summary>
    private List<INamedTypeSymbol> CollectContractClosure(
        IReadOnlyList<INamedTypeSymbol> rootTypes,
        Func<string, string>? namingPolicy
    )
    {
        var specTypes = new List<INamedTypeSymbol>();
        var seenGetterNames = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<INamedTypeSymbol>();

        foreach (var root in rootTypes)
            Enqueue(root);

        while (pending.TryDequeue(out var current))
        {
            if (TranspilableBaseOf(current) is { } baseType)
                Enqueue(baseType);

            foreach (var property in CollectSpecProperties(current, namingPolicy))
            {
                foreach (var reached in ReachableObjectTypes(property.Property.Type))
                    Enqueue(reached);
            }
        }

        return specTypes;

        void Enqueue(INamedTypeSymbol type)
        {
            if (!seenGetterNames.Add(SpecGetterName(type)))
                return;

            specTypes.Add(type);
            pending.Enqueue(type);
        }
    }

    /// <summary>
    /// The context getter that holds a type's spec (<c>get todoItem()</c>);
    /// its backing field is the same name prefixed with <c>_</c>. Roots,
    /// <c>ref</c> descriptors and <c>base</c> links all derive the name here,
    /// from the type's resolved TS name, which honors <c>[Name]</c> and is the
    /// unqualified simple name for a nested type (so it is always a valid
    /// identifier, unlike the qualified <c>Outer.Inner</c> type reference).
    /// </summary>
    private string SpecGetterName(INamedTypeSymbol type) =>
        TypeScriptNaming.ToCamelCase(_context.ResolveTsName(type));

    /// <summary>
    /// The TS type reference a spec names in <c>TypeSpec&lt;T&gt;</c>,
    /// <c>type: T</c> and <c>new T(...)</c>: the mapped type name, which is
    /// qualified for a nested type (<c>Outer.Inner</c>) exactly as property
    /// types reference it.
    /// </summary>
    private string SpecTypeReferenceName(INamedTypeSymbol type) =>
        MapType(type) is TsNamedType namedTs ? namedTs.Name : _context.ResolveTsName(type);

    /// <summary>
    /// Yields the object types a property type points at through
    /// <c>ref</c> descriptors, recursing through <see cref="ChildTypesOf"/>.
    /// </summary>
    private IEnumerable<INamedTypeSymbol> ReachableObjectTypes(ITypeSymbol type)
    {
        var kind = ClassifyPropertyType(type);

        if (kind == JsonPropertyKind.Ref)
            return IsDiscoverableObjectType(type) ? [(INamedTypeSymbol)type] : [];

        return ChildTypesOf(type, kind).SelectMany(ReachableObjectTypes);
    }

    /// <summary>
    /// The types a container descriptor nests: nullable inner, array /
    /// hash-set element, map key and value. Keep in sync with the
    /// <see cref="BuildTypeDescriptor"/> dispatch, which builds the nested
    /// descriptors for exactly these types.
    /// </summary>
    private static IEnumerable<ITypeSymbol> ChildTypesOf(ITypeSymbol type, JsonPropertyKind kind) =>
        kind switch
        {
            JsonPropertyKind.Nullable => [UnwrapNullable(type)],
            JsonPropertyKind.Array => [GetElementType(type)],
            JsonPropertyKind.HashSet => [((INamedTypeSymbol)type).TypeArguments[0]],
            JsonPropertyKind.Map => ((INamedTypeSymbol)type).TypeArguments.Take(2),
            _ => [],
        };

    private bool IsDiscoverableObjectType(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                TypeKind: TypeKind.Class or TypeKind.Struct,
                IsGenericType: false,
            } named
        && !IsSystemObject(named)
        && IsInCurrentAssembly(named)
        && SymbolHelper.IsTranspilable(
            named,
            _context.AssemblyWideTranspile,
            _context.CurrentAssembly
        );

    private bool IsInCurrentAssembly(INamedTypeSymbol type) =>
        _context.CurrentAssembly is null
        || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _context.CurrentAssembly);

    /// <summary>
    /// Builds the TypeSpec object literal for a given type:
    /// { type: T, factory: (p) => new T(...), properties: [...] }
    /// </summary>
    private TsObjectLiteral BuildTypeSpecObject(
        INamedTypeSymbol targetType,
        string tsTypeName,
        Func<string, string>? namingPolicy
    )
    {
        var properties = CollectSpecProperties(targetType, namingPolicy);
        var propSpecs = new List<TsExpression>();

        foreach (var (prop, jsonName, tsName) in properties)
        {
            var descriptor = ClassifyPropertyType(prop.Type);
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
        if (TranspilableBaseOf(targetType) is { } baseType)
        {
            baseProperty = new TsObjectProperty(
                "base",
                new TsPropertyAccess(new TsIdentifier("this"), SpecGetterName(baseType))
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
        IReadOnlyList<SerializableProperty> properties
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
        IReadOnlyList<SerializableProperty> properties
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
        IReadOnlyList<SerializableProperty> properties
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
    /// The properties a type's spec declares. A spec with a <c>base</c> link
    /// declares only the type's own properties — the runtime reads the
    /// inherited ones through the base spec. Without a link (the base emits
    /// no spec), the inherited properties of every toolchain-declared ancestor
    /// are inlined ahead of the own ones, base-most first like the runtime's
    /// base walk, so they are still written, read and passed to the factory.
    /// A property redeclared down the chain (an override) is declared once, at
    /// its base-most position with its most-derived declaration, whose
    /// <c>[JsonPropertyName]</c> wins as in System.Text.Json.
    /// </summary>
    private List<SerializableProperty> CollectSpecProperties(
        INamedTypeSymbol type,
        Func<string, string>? namingPolicy
    )
    {
        if (TranspilableBaseOf(type) is not null)
            return CollectSerializableProperties(type, namingPolicy);

        return InlinedAncestorsOf(type)
            .Reverse()
            .Append(type)
            .SelectMany(declaring => CollectSerializableProperties(declaring, namingPolicy))
            .GroupBy(property => property.TsName, StringComparer.Ordinal)
            .Select(redeclarations => redeclarations.Last())
            .ToList();
    }

    /// <summary>
    /// The ancestors whose properties an unlinked spec inlines, nearest first:
    /// the base chain up to <c>object</c> or the first type the toolchain does
    /// not declare (<see cref="SymbolHelper.IsToolchainDeclared"/>: a BCL,
    /// foreign-package, <c>[External]</c> or <c>[Import]</c> base).
    /// </summary>
    private IEnumerable<INamedTypeSymbol> InlinedAncestorsOf(INamedTypeSymbol type)
    {
        for (var ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (!SymbolHelper.IsToolchainDeclared(ancestor, _context.CurrentAssembly))
                yield break;

            yield return ancestor;
        }
    }

    /// <summary>
    /// Collects public properties from the type, excluding [JsonIgnore], and computes
    /// both the TS field name and the JSON wire name.
    /// </summary>
    private static List<SerializableProperty> CollectSerializableProperties(
        INamedTypeSymbol type,
        Func<string, string>? namingPolicy
    )
    {
        var result = new List<SerializableProperty>();

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.IsStatic)
                continue;
            if (prop.IsImplicitlyDeclared)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (SymbolHelper.HasAttribute(prop, "JsonIgnore"))
                continue;
            if (prop.IsIndexer)
                continue;

            var tsName = IrToTsNamingPolicy.ToDataMemberName(
                prop.Name,
                SymbolHelper.GetNameOverride(prop, TargetLanguage.TypeScript)
            );

            // JSON name: [JsonPropertyName] wins, otherwise apply naming policy
            var jsonName = GetJsonPropertyName(prop) ?? ApplyNamingPolicy(prop.Name, namingPolicy);

            result.Add(new SerializableProperty(prop, jsonName, tsName));
        }

        return result;
    }

    /// <summary>
    /// Reads [JsonPropertyName("name")] from a property if present.
    /// </summary>
    private static string? GetJsonPropertyName(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (
                attr.AttributeClass?.Name is not ("JsonPropertyNameAttribute" or "JsonPropertyName")
            )
                continue;

            if (attr.ConstructorArguments.Length > 0)
                return attr.ConstructorArguments[0].Value?.ToString();
        }

        return null;
    }

    /// <summary>
    /// Applies the naming policy to a C# property name to get the JSON wire name.
    /// If no policy is set, the original PascalCase name is preserved (System.Text.Json default).
    /// </summary>
    private static string ApplyNamingPolicy(string csharpName, Func<string, string>? policy)
    {
        return policy?.Invoke(csharpName) ?? csharpName;
    }

    /// <summary>
    /// Classifies a C# property type into a <see cref="JsonPropertyKind"/>
    /// dispatch tag for the runtime spec. The wire string is derived from
    /// the enum value via <see cref="GetWireKind"/> so the on-disk shape
    /// stays byte-identical with the prior magic-string implementation.
    /// </summary>
    private JsonPropertyKind ClassifyPropertyType(ITypeSymbol type)
    {
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            }
        )
            return JsonPropertyKind.Nullable;

        if (
            type.NullableAnnotation == NullableAnnotation.Annotated
            && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
        )
            return JsonPropertyKind.Nullable;

        if (
            type.SpecialType
            is SpecialType.System_String
                or SpecialType.System_Int16
                or SpecialType.System_Int32
                or SpecialType.System_Int64
                or SpecialType.System_UInt16
                or SpecialType.System_UInt32
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Byte
                or SpecialType.System_SByte
                or SpecialType.System_Boolean
        )
            return JsonPropertyKind.Primitive;

        if (type is INamedTypeSymbol named)
        {
            var fullName = named.ToDisplayString();

            // Guid → UUID branded type. At runtime it's a string (JSON wire:
            // primitive string), but the deserialize side wraps via UUID.create.
            if (fullName is "System.Guid")
                return JsonPropertyKind.Branded;

            // Uri → primitive string (no special handling needed).
            if (fullName is "System.Uri")
                return JsonPropertyKind.Primitive;

            if (fullName is "System.Decimal" || type.SpecialType == SpecialType.System_Decimal)
                return JsonPropertyKind.Decimal;

            if (
                fullName
                is "System.DateTime"
                    or "System.DateTimeOffset"
                    or "System.DateOnly"
                    or "System.TimeOnly"
                    or "System.TimeSpan"
            )
                return JsonPropertyKind.Temporal;

            if (named.IsDictionaryLike() && named.TypeArguments.Length >= 2)
                return JsonPropertyKind.Map;

            if (named.IsSetLike() && named.TypeArguments.Length > 0)
                return JsonPropertyKind.HashSet;

            // Collection-like / IEnumerable / IReadOnlyCollection → array.
            // Since #129 these no longer share the `IsCollectionLike`
            // predicate (they lower to TS `Iterable<T>` at the type
            // level), but JSON-wise they still serialize as arrays —
            // `JSON.stringify([...iter])` works the same as a plain
            // array.
            if (
                (
                    named.IsCollectionLike()
                    || fullName.StartsWith("System.Collections.Generic.IEnumerable")
                    || fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection")
                )
                && named.TypeArguments.Length > 0
            )
                return JsonPropertyKind.Array;

            if (named.TypeKind == TypeKind.Enum)
                return SymbolHelper.HasAttribute(named, "StringEnum")
                    ? JsonPropertyKind.Enum
                    : JsonPropertyKind.NumericEnum;

            // [Branded] (and its predecessor [InlineWrapper]) → branded
            if (SymbolHelper.HasBranded(named))
                return JsonPropertyKind.Branded;
        }

        if (type is IArrayTypeSymbol)
            return JsonPropertyKind.Array;

        return JsonPropertyKind.Ref;
    }

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
        var innerType = UnwrapNullable(type);
        var innerKind = ClassifyPropertyType(innerType);
        return new TsObjectLiteral([
            new TsObjectProperty("kind", WireKindLiteral(JsonPropertyKind.Nullable)),
            new TsObjectProperty("inner", BuildTypeDescriptor(innerType, innerKind)),
        ]);
    }

    private TsObjectLiteral BuildArrayDescriptor(ITypeSymbol type)
    {
        var elementType = GetElementType(type);
        var elementKind = ClassifyPropertyType(elementType);
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
                BuildTypeDescriptor(keyType, ClassifyPropertyType(keyType))
            ),
            new TsObjectProperty(
                "value",
                BuildTypeDescriptor(valueType, ClassifyPropertyType(valueType))
            ),
        ]);
    }

    private TsObjectLiteral BuildHashSetDescriptor(ITypeSymbol type)
    {
        var named = (INamedTypeSymbol)type;
        var elementType = named.TypeArguments[0];
        var elementKind = ClassifyPropertyType(elementType);
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

    /// <summary>
    /// The context getter a <c>ref</c> descriptor points at: the
    /// <see cref="SpecGetterName"/> of a named type, or the camel-cased
    /// mapped TS name for anything else (a type parameter).
    /// </summary>
    private string RefGetterName(ITypeSymbol type) =>
        type is INamedTypeSymbol named
            ? SpecGetterName(named)
            : TypeScriptNaming.ToCamelCase(
                MapType(type) is TsNamedType namedTs ? namedTs.Name : "unknown"
            );

    private TsObjectLiteral BuildRefDescriptor(ITypeSymbol type)
    {
        var getterName = RefGetterName(type);
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

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
        )
            return nullable.TypeArguments[0];

        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        return type;
    }

    private static ITypeSymbol GetElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;
        if (type is INamedTypeSymbol named && named.TypeArguments.Length > 0)
            return named.TypeArguments[0];
        return type;
    }

    private static bool IsSystemObject(INamedTypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_Object || type.ToDisplayString() == "object";
    }

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

    /// <summary>
    /// The base type a spec links to through <c>base: this.x</c>, or
    /// <c>null</c> when the base emits no spec. Uses the emitter's
    /// transpilability rule (including <c>[assembly: TranspileAssembly]</c>
    /// opt-in), so a base whose class is emitted always gets its spec. A base
    /// from a referenced package links only when it carries <c>[Transpile]</c>;
    /// one included by that package's <c>[assembly: TranspileAssembly]</c> is
    /// not opted in for this compilation, so its properties are inlined
    /// instead (<see cref="CollectSpecProperties"/>) — both serialize the same
    /// wire shape.
    /// </summary>
    private INamedTypeSymbol? TranspilableBaseOf(INamedTypeSymbol type) =>
        type.BaseType is { } baseType
        && baseType.SpecialType == SpecialType.None
        && !IsSystemObject(baseType)
        && SymbolHelper.IsTranspilable(
            baseType,
            _context.AssemblyWideTranspile,
            _context.CurrentAssembly
        )
            ? baseType
            : null;

    /// <summary>A serializable property with its JSON wire name and TS member
    /// name.</summary>
    private sealed record SerializableProperty(
        IPropertySymbol Property,
        string JsonName,
        string TsName
    );
}
