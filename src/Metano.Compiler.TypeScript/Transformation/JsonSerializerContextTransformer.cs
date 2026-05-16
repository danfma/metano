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
/// <c>[JsonSerializable(typeof(T))]</c> to discover which types need specs.
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
        var serializableTypes = ReadSerializableTypes(type);

        if (serializableTypes.Count == 0)
            return;

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
        foreach (var targetType in serializableTypes)
        {
            var tsTypeName = _context.ResolveTsName(targetType);
            var fieldName = "_" + TypeScriptNaming.ToCamelCase(tsTypeName);
            var getterName = TypeScriptNaming.ToCamelCase(tsTypeName);
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
    /// Builds the TypeSpec object literal for a given type:
    /// { type: T, factory: (p) => new T(...), properties: [...] }
    /// </summary>
    private TsObjectLiteral BuildTypeSpecObject(
        INamedTypeSymbol targetType,
        string tsTypeName,
        Func<string, string>? namingPolicy
    )
    {
        var properties = CollectSerializableProperties(targetType, namingPolicy);
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
        if (
            targetType.BaseType is { } baseType
            && baseType.SpecialType == SpecialType.None
            && !IsSystemObject(baseType)
            && SymbolHelper.IsTranspilable(baseType)
        )
        {
            var baseTsName = _context.ResolveTsName(baseType);
            var baseGetterName = TypeScriptNaming.ToCamelCase(baseTsName);
            baseProperty = new TsObjectProperty(
                "base",
                new TsPropertyAccess(new TsIdentifier("this"), baseGetterName)
            );
        }

        // factory: (p) => new T(...) for classes, (p) => ({ ... }) for PlainObject
        var factoryParam = new TsParameter(
            "p",
            new TsNamedType("Record", [new TsStringType(), new TsNamedType("unknown")])
        );

        TsExpression factoryBody;
        if (isPlainObject)
        {
            // [PlainObject] → object literal: { title: p.title as string, ... }
            var objLiteralProps = properties
                .Select(x => new TsObjectProperty(
                    x.TsName,
                    new TsCastExpression(
                        new TsPropertyAccess(new TsIdentifier("p"), x.TsName),
                        MapType(x.Property.Type)
                    )
                ))
                .ToList();
            factoryBody = new TsObjectLiteral(objLiteralProps);
        }
        else
        {
            // Class → new TodoItem(p.title as string, ...)
            var factoryArgs = properties
                .Select(x =>
                    new TsCastExpression(
                        new TsPropertyAccess(new TsIdentifier("p"), x.TsName),
                        MapType(x.Property.Type)
                    ) as TsExpression
                )
                .ToList();
            factoryBody = new TsNewExpression(new TsIdentifier(tsTypeName), factoryArgs);
        }

        var factoryProperty = new TsObjectProperty(
            "factory",
            new TsArrowFunction([factoryParam], [new TsReturnStatement(factoryBody)])
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
    /// Collects public properties from the type, excluding [JsonIgnore], and computes
    /// both the TS field name and the JSON wire name.
    /// </summary>
    private static List<(
        IPropertySymbol Property,
        string JsonName,
        string TsName
    )> CollectSerializableProperties(INamedTypeSymbol type, Func<string, string>? namingPolicy)
    {
        var result = new List<(IPropertySymbol, string, string)>();

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.IsImplicitlyDeclared)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (SymbolHelper.HasAttribute(prop, "JsonIgnore"))
                continue;
            if (prop.IsIndexer)
                continue;

            var tsName =
                SymbolHelper.GetNameOverride(prop, TargetLanguage.TypeScript)
                ?? TypeScriptNaming.ToCamelCase(prop.Name);

            // JSON name: [JsonPropertyName] wins, otherwise apply naming policy
            var jsonName = GetJsonPropertyName(prop) ?? ApplyNamingPolicy(prop.Name, namingPolicy);

            result.Add((prop, jsonName, tsName));
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

    private TsObjectLiteral BuildRefDescriptor(ITypeSymbol type)
    {
        var tsType = MapType(type);
        var refName = tsType is TsNamedType namedTs ? namedTs.Name : "unknown";
        var getterName = TypeScriptNaming.ToCamelCase(refName);
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
}
