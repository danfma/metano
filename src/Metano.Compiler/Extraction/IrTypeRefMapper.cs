using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Maps Roslyn <see cref="ITypeSymbol"/> to <see cref="IrTypeRef"/>.
/// Contains only <em>semantic</em> decisions — no target-specific rendering.
/// Each backend has its own bridge that maps <see cref="IrTypeRef"/> to
/// its target AST types.
/// <para>
/// An optional <see cref="IrTypeOriginResolver"/> lets the caller stamp
/// cross-package origins on <see cref="IrNamedTypeRef"/>s so backends can
/// emit the correct import statements without a second pass.
/// </para>
/// </summary>
public static class IrTypeRefMapper
{
    /// <summary>
    /// The deepest chain of constructed types an equality walk follows before it
    /// gives up (a generic that grows without end).
    /// </summary>
    private const int MaxEqualityWalkDepth = 32;

    /// <summary>
    /// Backends opt into named-alias delegate emission by setting this
    /// predicate; <c>null</c> keeps the mapper on the inline-function
    /// fallback path.
    /// </summary>
    private static readonly AsyncLocal<Func<INamedTypeSymbol, bool>?> _namedDelegatePredicate =
        new();

    public static Func<INamedTypeSymbol, bool>? NamedDelegatePredicate
    {
        get => _namedDelegatePredicate.Value;
        set => _namedDelegatePredicate.Value = value;
    }

    /// <summary>
    /// Maps a C# type to a semantic <see cref="IrTypeRef"/>. When
    /// <paramref name="originResolver"/> is provided, named-type references
    /// carry an <see cref="IrTypeOrigin"/> pointing at the producing package.
    /// </summary>
    public static IrTypeRef Map(
        ITypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        // Nullable<T> (value types: int?, bool?, etc.)
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
        )
        {
            return new IrNullableTypeRef(Map(nullable.TypeArguments[0], originResolver, target));
        }

        // Nullable reference types (string?, Money?, etc.)
        if (
            type.NullableAnnotation == NullableAnnotation.Annotated
            && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
        )
        {
            return new IrNullableTypeRef(
                Map(
                    type.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                    originResolver,
                    target
                )
            );
        }

        // Primitives via SpecialType
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return new IrPrimitiveTypeRef(IrPrimitive.Boolean);
            case SpecialType.System_Byte:
                return new IrPrimitiveTypeRef(IrPrimitive.Byte);
            case SpecialType.System_SByte:
                return new IrPrimitiveTypeRef(IrPrimitive.Byte) { ExactKind = IrNumericKind.SByte };
            case SpecialType.System_Int16:
                return new IrPrimitiveTypeRef(IrPrimitive.Int16);
            case SpecialType.System_UInt16:
                return new IrPrimitiveTypeRef(IrPrimitive.Int16)
                {
                    ExactKind = IrNumericKind.UInt16,
                };
            case SpecialType.System_Int32:
                return new IrPrimitiveTypeRef(IrPrimitive.Int32);
            case SpecialType.System_UInt32:
                return new IrPrimitiveTypeRef(IrPrimitive.Int32)
                {
                    ExactKind = IrNumericKind.UInt32,
                };

            // nint / nuint are 64-bit on every host Metano targets (spec 012
            // DECIDED-2).
            case SpecialType.System_Int64:
            case SpecialType.System_IntPtr:
                return new IrPrimitiveTypeRef(IrPrimitive.Int64);
            case SpecialType.System_UInt64:
            case SpecialType.System_UIntPtr:
                return new IrPrimitiveTypeRef(IrPrimitive.UInt64);
            case SpecialType.System_Single:
                return new IrPrimitiveTypeRef(IrPrimitive.Float32);
            case SpecialType.System_Double:
                return new IrPrimitiveTypeRef(IrPrimitive.Float64);
            case SpecialType.System_Decimal:
                return new IrPrimitiveTypeRef(IrPrimitive.Decimal);
            case SpecialType.System_Char:
                return new IrPrimitiveTypeRef(IrPrimitive.Char);
            case SpecialType.System_String:
                return new IrPrimitiveTypeRef(IrPrimitive.String);
            case SpecialType.System_Void:
                return new IrPrimitiveTypeRef(IrPrimitive.Void);
        }

        // Named types
        if (type is INamedTypeSymbol named)
        {
            var fullName = named.ToDisplayString();

            // Task<T> / ValueTask<T> → Promise
            if (IsTaskLike(named))
            {
                var inner =
                    named.TypeArguments.Length > 0
                        ? Map(named.TypeArguments[0], originResolver, target)
                        : new IrPrimitiveTypeRef(IrPrimitive.Void);
                return new IrPromiseTypeRef(inner, named.IsValueType);
            }

            // BigInteger
            if (named.IsBigInteger())
                return new IrPrimitiveTypeRef(IrPrimitive.BigInteger);

            // Date/time types
            if (fullName is "System.DateTime")
                return new IrPrimitiveTypeRef(IrPrimitive.DateTime);
            if (fullName is "System.DateTimeOffset")
                return new IrPrimitiveTypeRef(IrPrimitive.DateTimeOffset);
            if (fullName is "System.DateOnly")
                return new IrPrimitiveTypeRef(IrPrimitive.DateOnly);
            if (fullName is "System.TimeOnly")
                return new IrPrimitiveTypeRef(IrPrimitive.TimeOnly);
            if (fullName is "System.TimeSpan")
                return new IrPrimitiveTypeRef(IrPrimitive.TimeSpan);

            // Guid
            if (fullName is "System.Guid")
                return new IrPrimitiveTypeRef(IrPrimitive.Guid);

            // Uri → string semantically
            if (fullName is "System.Uri")
                return new IrPrimitiveTypeRef(IrPrimitive.String);

            // Object
            if (fullName is "System.Object")
                return new IrPrimitiveTypeRef(IrPrimitive.Object);

            // A stage 011c collection, its interfaces and the dictionary views
            // (spec 011 FR-001), by symbol identity. Checked before the
            // dictionary-like case, whose display-name prefix also matches
            // Dictionary<K,V>.KeyCollection / ValueCollection.
            if (CollectionKindOf(named, target) is { } kind)
                return MapCollection(named, kind, originResolver, target);

            // Metano.Interop.JsArray<T> and ReadOnlyJsArray<T> are the native array itself (spec 011 FR-004).
            if (IsJsArray(named))
                return new IrArrayTypeRef(Map(named.TypeArguments[0], originResolver, target))
                {
                    IsNative = true,
                };

            if (IsReadOnlyJsArray(named))
                return new IrArrayTypeRef(Map(named.TypeArguments[0], originResolver, target))
                {
                    IsNative = true,
                    IsReadOnly = true,
                };

            // Dictionary-like → Map
            if (named.IsDictionaryLike() && named.TypeArguments.Length >= 2)
                return new IrMapTypeRef(
                    Map(named.TypeArguments[0], originResolver, target),
                    Map(named.TypeArguments[1], originResolver, target)
                );

            // Set-like → Set
            if (named.IsSetLike() && named.TypeArguments.Length > 0)
                return new IrSetTypeRef(Map(named.TypeArguments[0], originResolver, target));

            // KeyValuePair<K,V> → KeyValuePair
            if (
                fullName.StartsWith("System.Collections.Generic.KeyValuePair")
                && named.TypeArguments.Length >= 2
            )
                return new IrKeyValuePairTypeRef(
                    Map(named.TypeArguments[0], originResolver, target),
                    Map(named.TypeArguments[1], originResolver, target)
                );

            // Tuple / ValueTuple
            var originalName = named.OriginalDefinition.ToDisplayString();
            if (
                (
                    originalName.StartsWith("System.Tuple")
                    || originalName.StartsWith("System.ValueTuple")
                    || named.IsTupleType
                )
                && named.TypeArguments.Length > 0
            )
                return new IrTupleTypeRef(
                    named.TypeArguments.Select(a => Map(a, originResolver, target)).ToList()
                );

            // IGrouping<K,V>
            if (fullName.StartsWith("System.Linq.IGrouping") && named.TypeArguments.Length >= 2)
                return new IrGroupingTypeRef(
                    Map(named.TypeArguments[0], originResolver, target),
                    Map(named.TypeArguments[1], originResolver, target)
                );

            // IEnumerable<T> / IReadOnlyCollection<T> / IQueryable<T> /
            // IOrderedQueryable<T> / IOrderedEnumerable<T> → Iterable.
            // C# `IEnumerable<T>` is a lazy, read-only, forward-only
            // sequence — the closest TS surface is `Iterable<T>`,
            // not `Array<T>`. `IQueryable<T>` and the `IOrdered*`
            // marker interfaces share the same runtime shape: Phase B
            // (#196) attaches the chain stages to the materialized
            // result so an introspecting consumer can read them, but
            // the surface is still iterable. Iterator return types
            // (yield) take a separate path through
            // `MapForGeneratorReturn` and lower to `Generator<T>`
            // instead.
            if (
                (
                    fullName.StartsWith("System.Collections.Generic.IEnumerable")
                    || fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection")
                    || fullName.StartsWith("System.Linq.IQueryable")
                    || fullName.StartsWith("System.Linq.IOrderedQueryable")
                    || fullName.StartsWith("System.Linq.IOrderedEnumerable")
                )
                && named.TypeArguments.Length > 0
            )
                return new IrIterableTypeRef(Map(named.TypeArguments[0], originResolver, target));

            // IEnumerator<T> → Iterator: the cursor `GetEnumerator()` returns. The
            // non-generic IEnumerator iterates `object`.
            if (
                named.OriginalDefinition.SpecialType
                == SpecialType.System_Collections_Generic_IEnumerator_T
            )
                return new IrIteratorTypeRef(Map(named.TypeArguments[0], originResolver, target));

            if (named.SpecialType == SpecialType.System_Collections_IEnumerator)
                return new IrIteratorTypeRef(new IrPrimitiveTypeRef(IrPrimitive.Object));

            // Collection-like → Array
            if (named.IsCollectionLike() && named.TypeArguments.Length > 0)
                return new IrArrayTypeRef(Map(named.TypeArguments[0], originResolver, target));

            // `[JsCallable]` interface in type position: the interface is erased
            // (no declaration / import), so a bare named reference would dangle.
            // Lower it to its call shape — a single function type for one Invoke,
            // or a multi-signature callable for overloaded Invoke. Roslyn returns
            // the Invoke overloads with their type arguments already substituted
            // (e.g. `ISignalSetter<int>` → `Invoke(int)`, `Invoke(Func<int,int>)`).
            if (named.TypeKind == TypeKind.Interface && SymbolHelper.HasJsCallable(named))
            {
                var signatures = named
                    .GetMembers(SymbolHelper.JsCallableInvokeMember)
                    .OfType<IMethodSymbol>()
                    .Select(invoke => BuildFunctionType(invoke, originResolver, target))
                    .ToList();
                if (signatures.Count == 1)
                    return signatures[0];
                if (signatures.Count > 1)
                    return new IrCallableTypeRef(signatures);
                // No Invoke (malformed — MS0028 covers this) → an empty callable.
                return new IrFunctionTypeRef([], new IrPrimitiveTypeRef(IrPrimitive.Void));
            }

            if (named.TypeKind == TypeKind.Delegate)
            {
                if (NamedDelegatePredicate?.Invoke(named) == true)
                {
                    return new IrNamedTypeRef(
                        BuildQualifiedName(named, target),
                        GetNamespace(named),
                        named.TypeArguments.Length > 0
                            ? named
                                .TypeArguments.Select(a => Map(a, originResolver, target))
                                .ToList()
                            : null,
                        originResolver?.Invoke(named),
                        BuildNamedTypeSemantics(named, target),
                        PrivateNestedSegmentCount(named)
                    );
                }
                return MapDelegateType(named, originResolver, target);
            }

            // User-defined named types: may carry a cross-package origin. A nested
            // type resolves through its top-level container, which the package exports.
            var origin = originResolver.ResolveThroughOutermost(named);
            var semantics = WithRuntimeDeclaration(BuildNamedTypeSemantics(named, target), origin);

            var containerArguments = ContainerTypeArgumentsOf(named, originResolver, target);

            if (named.TypeArguments.Length > 0)
            {
                var args = named.TypeArguments.Select(a => Map(a, originResolver, target)).ToList();

                return new IrNamedTypeRef(
                    BuildQualifiedName(named, target),
                    GetNamespace(named),
                    args,
                    origin,
                    semantics,
                    PrivateNestedSegmentCount(named)
                )
                {
                    ContainerTypeArguments = containerArguments,
                };
            }

            return new IrNamedTypeRef(
                BuildQualifiedName(named, target),
                GetNamespace(named),
                Origin: origin,
                Semantics: semantics,
                PrivateNestedSegments: PrivateNestedSegmentCount(named)
            )
            {
                ContainerTypeArguments = containerArguments,
            };
        }

        // Type parameters (T, K, V)
        if (type is ITypeParameterSymbol typeParam)
            return new IrTypeParameterRef(typeParam.Name);

        // Array types
        if (type is IArrayTypeSymbol array)
            return new IrArrayTypeRef(Map(array.ElementType, originResolver, target))
            {
                IsNative = true,
            };

        if (type is IDynamicTypeSymbol)
            return new IrDynamicTypeRef();

        return new IrUnknownTypeRef();
    }

    /// <summary>
    /// The collection kind <paramref name="type"/> maps to for
    /// <paramref name="target"/>: <c>IReadOnlySet&lt;T&gt;</c> and the stage
    /// 011h collections join the kinds only for a target whose collections are
    /// runtime classes (a target that keeps native collections renders them
    /// as their 2.x IR).
    /// </summary>
    private static IrCollectionKind? CollectionKindOf(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) =>
        IrExtractionContext.KeepsNativeCollectionsFor(target)
            ? CollectionKindResolver.NativeTargetKindOf(type)
            : CollectionKindResolver.KindOf(type);

    private static bool IsJsArray(INamedTypeSymbol type) =>
        type is { Name: "JsArray", Arity: 1 }
        && type.ContainingNamespace?.ToDisplayString() == "Metano.Interop";

    private static bool IsReadOnlyJsArray(INamedTypeSymbol type) =>
        type is { Name: "ReadOnlyJsArray", Arity: 1 }
        && type.ContainingNamespace?.ToDisplayString() == "Metano.Interop";

    private static IrCollectionTypeRef MapCollection(
        INamedTypeSymbol type,
        IrCollectionKind kind,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    ) =>
        new(
            kind,
            CollectionKindResolver
                .TypeArgumentsOf(type, kind)
                .Select(argument => Map(argument, originResolver, target))
                .ToList(),
            CollectionKindResolver.VarianceOf(type, kind)
        );

    /// <summary>
    /// Maps a return type for iterator methods (yield). IEnumerable/IEnumerator → Generator;
    /// the non-generic IEnumerable/IEnumerator yield <c>object</c>.
    /// </summary>
    public static IrTypeRef MapForGeneratorReturn(
        ITypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        if (type is INamedTypeSymbol named)
        {
            var fullName = named.ToDisplayString();
            if (
                (
                    fullName.StartsWith("System.Collections.Generic.IEnumerable")
                    || fullName.StartsWith("System.Collections.Generic.IEnumerator")
                )
                && named.TypeArguments.Length > 0
            )
            {
                return new IrGeneratorTypeRef(Map(named.TypeArguments[0], originResolver, target));
            }

            if (
                named.SpecialType
                is SpecialType.System_Collections_IEnumerable
                    or SpecialType.System_Collections_IEnumerator
            )
                return new IrGeneratorTypeRef(new IrPrimitiveTypeRef(IrPrimitive.Object));
        }

        return Map(type, originResolver, target);
    }

    /// <summary>
    /// Extracts the bits of type metadata that backend runtime tests need —
    /// kind, string-enum values, inline-wrapper primitive, transpilability.
    /// Returns null for type parameters / non-classifiable types so the
    /// backend can fall back to a conservative default.
    /// </summary>
    private static IrNamedTypeSemantics? BuildNamedTypeSemantics(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        // Enums split into StringEnum (exhaustive value union) and NumericEnum
        // at the IR level — each maps to a different runtime test on the TS
        // side (`=== "value" || …` vs `typeof === "number"`).
        if (named.TypeKind == TypeKind.Enum)
        {
            if (SymbolHelper.HasStringEnum(named))
            {
                var values = named
                    .GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(f => f.HasConstantValue)
                    .Select(f => SymbolHelper.GetNameOverride(f, target) ?? f.Name)
                    .ToList();

                return new IrNamedTypeSemantics(
                    IrNamedTypeKind.StringEnum,
                    StringEnumValues: values,
                    IsTranspilable: IsTranspilableType(named, target),
                    IsIgnored: SymbolHelper.HasIgnore(named, target),
                    EnumZeroMember: ExtractEnumZeroMember(named, target),
                    IsValueType: true
                );
            }

            return new IrNamedTypeSemantics(
                IrNamedTypeKind.NumericEnum,
                IsTranspilable: IsTranspilableType(named, target),
                IsIgnored: SymbolHelper.HasIgnore(named, target),
                EnumZeroMember: ExtractEnumZeroMember(named, target),
                IsExternal: IsAmbient(named),
                IsValueType: true
            )
            {
                IsFlagsEnum = named.HasFlags(),
                EnumUnderlyingKind = NumericKindOf(named.EnumUnderlyingType),
            };
        }

        if (named.TypeKind == TypeKind.Interface)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Interface,
                IsTranspilable: IsTranspilableType(named, target),
                IsIgnored: SymbolHelper.HasIgnore(named, target),
                IsExternal: IsAmbient(named),
                HasGeneratedGuard: SymbolHelper.HasGenerateGuard(named),
                Supertypes: SupertypesOf(named, target)
            );

        if (named.TypeKind == TypeKind.Delegate)
            return new IrNamedTypeSemantics(IrNamedTypeKind.Delegate);

        // Inline wrappers collapse to a branded primitive at runtime. A
        // wrapper over anything a brand cannot intersect lowers as the record
        // or struct it is (#288), so it takes the arms below.
        if (SymbolHelper.IsPrimitiveBackedBranded(named))
        {
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Branded,
                BrandedUnderlyingPrimitive: BrandedUnderlyingPrimitiveOf(named),
                IsTranspilable: IsTranspilableType(named, target),
                IsIgnored: SymbolHelper.HasIgnore(named, target),
                IsExternal: IsAmbient(named),
                IsValueType: named.IsValueType
            );
        }

        if (IsExceptionType(named))
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Exception,
                IsTranspilable: IsTranspilableType(named, target),
                IsIgnored: SymbolHelper.HasIgnore(named, target),
                IsExternal: IsAmbient(named),
                ClrTypeName: BclExportReader.ExportKey(named),
                Supertypes: SupertypesOf(named, target)
            );

        // JSX recognition mirrors the declaration-side IrTypeSemantics flags
        // (IrClassExtractor.ExtractSemantics) so the TS expression bridge can
        // decide JSX lowering off the constructed value's type ref. The native
        // tag wins over the component flag because Html elements derive from the
        // [JsxComponentBuilder] base AND carry [JsxNativeElement] — a div must
        // render as <div>, not as a <Div/> component.
        var jsxNativeTag = SymbolHelper.GetJsxNativeElementTag(named);
        var isJsxComponent = SymbolHelper.IsJsxComponent(named);
        var rendersAsJsx = SymbolHelper.IsJsxRenderable(named);
        var isJsTuple = named.HasJsTuple();
        var jsTupleArity = isJsTuple ? JsTupleArityOf(named) : null;

        if (named.IsRecord)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Record,
                IsTranspilable: IsTranspilableType(named, target),
                IsIgnored: SymbolHelper.HasIgnore(named, target),
                IsExternal: IsAmbient(named),
                HasGeneratedGuard: SymbolHelper.HasGenerateGuard(named),
                IsPlainObject: SymbolHelper.HasPlainObject(named),
                IsJsTuple: isJsTuple,
                JsTupleArity: jsTupleArity,
                JsxNativeElementTag: jsxNativeTag,
                IsJsxComponent: isJsxComponent,
                RendersAsJsxElement: rendersAsJsx,
                IsDefaultConstructible: IsDefaultConstructible(named, target),
                IsValueType: named.IsValueType,
                HasImplicitParameterlessOverload: HasImplicitParameterlessOverload(named, target),
                StructuralEqualityLeaves: StructuralEqualityLeavesOf(named, target),
                Supertypes: SupertypesOf(named, target)
            )
            {
                HasExplicitParameterlessCtor = HasExplicitParameterlessCtor(named, target),
                HasZeroValueFactory = HasZeroValueFactory(named, target),
                IsClosed = named.IsClosed,
            };

        if (named.TypeKind == TypeKind.Struct)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Struct,
                IsTranspilable: IsTranspilableType(named, target),
                IsIgnored: SymbolHelper.HasIgnore(named, target),
                IsExternal: IsAmbient(named),
                HasGeneratedGuard: SymbolHelper.HasGenerateGuard(named),
                IsPlainObject: SymbolHelper.HasPlainObject(named),
                IsJsTuple: isJsTuple,
                JsTupleArity: jsTupleArity,
                IsDefaultConstructible: IsDefaultConstructible(named, target),
                IsValueType: true,
                HasImplicitParameterlessOverload: HasImplicitParameterlessOverload(named, target),
                StructuralEqualityLeaves: StructuralEqualityLeavesOf(named, target),
                Supertypes: SupertypesOf(named, target)
            )
            {
                HasExplicitParameterlessCtor = HasExplicitParameterlessCtor(named, target),
                HasZeroValueFactory = HasZeroValueFactory(named, target),
            };

        if (named.TypeKind == TypeKind.Class)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Class,
                IsTranspilable: IsTranspilableType(named, target),
                IsIgnored: SymbolHelper.HasIgnore(named, target),
                IsExternal: IsAmbient(named),
                HasGeneratedGuard: SymbolHelper.HasGenerateGuard(named),
                IsPlainObject: SymbolHelper.HasPlainObject(named),
                IsJsTuple: isJsTuple,
                JsTupleArity: jsTupleArity,
                JsxNativeElementTag: jsxNativeTag,
                IsJsxComponent: isJsxComponent,
                RendersAsJsxElement: rendersAsJsx,
                Supertypes: SupertypesOf(named, target)
            )
            {
                IsClosed = named.IsClosed,
            };

        return null;
    }

    /// <summary>
    /// Marks an interface the compilation does not transpile as declared by the target
    /// runtime when it carries the origin of <see cref="BclExportReader.RuntimeDeclaringPackage"/>:
    /// the origin resolver gives such an interface one only for its <c>[ExportFromBcl]</c>
    /// export, and that package declares it with its runtime descriptor (FR-111, W10-9a).
    /// A user's mapping to another package keeps its 3.2.0 shape. A target that binds no
    /// export passes no such origin, so the fact stays off there.
    /// </summary>
    private static IrNamedTypeSemantics? WithRuntimeDeclaration(
        IrNamedTypeSemantics? semantics,
        IrTypeOrigin? origin
    ) =>
        origin is not null
        && BclExportReader.IsRuntimeDeclaringPackage(origin.PackageId)
        && semantics
            is {
                Kind: IrNamedTypeKind.Interface,
                IsTranspilable: false,
                IsIgnored: false,
                IsExternal: false,
            }
            ? semantics with
            {
                IsRuntimeDeclared = true,
            }
            : semantics;

    /// <summary>
    /// The keys (<see cref="IrNamedTypeRef.SupertypeKey"/>) of the base classes
    /// of <paramref name="named"/> below <c>System.Object</c> and of every
    /// interface it implements; <c>null</c> when it has none.
    /// </summary>
    private static IReadOnlyList<SupertypeKey>? SupertypesOf(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var supertypes = new List<SupertypeKey>();

        for (var baseType = named.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (
                baseType.SpecialType
                is SpecialType.System_Object
                    or SpecialType.System_ValueType
                    or SpecialType.System_Enum
            )
                break;

            supertypes.Add(SupertypeKeyOf(baseType, target));
        }

        supertypes.AddRange(named.AllInterfaces.Select(type => SupertypeKeyOf(type, target)));

        return supertypes.Count == 0 ? null : supertypes;
    }

    /// <summary>The integral kind of an enum's underlying type; <c>null</c> for none.</summary>
    private static IrNumericKind? NumericKindOf(INamedTypeSymbol? underlying) =>
        underlying?.SpecialType switch
        {
            SpecialType.System_SByte => IrNumericKind.SByte,
            SpecialType.System_Byte => IrNumericKind.Byte,
            SpecialType.System_Int16 => IrNumericKind.Int16,
            SpecialType.System_UInt16 => IrNumericKind.UInt16,
            SpecialType.System_Int32 => IrNumericKind.Int32,
            SpecialType.System_UInt32 => IrNumericKind.UInt32,
            SpecialType.System_Int64 => IrNumericKind.Int64,
            SpecialType.System_UInt64 => IrNumericKind.UInt64,
            _ => null,
        };

    /// <summary>
    /// The key a reference to <paramref name="type"/> carries as
    /// <see cref="IrNamedTypeRef.SupertypeKey"/>: its namespace and its
    /// containing-type-qualified emitted name.
    /// </summary>
    public static SupertypeKey SupertypeKeyOf(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) => SupertypeKey.From(GetNamespace(type), BuildQualifiedName(type.OriginalDefinition, target));

    /// <summary>
    /// Whether the zero value of <paramref name="named"/> can be built with a
    /// parameterless construction of its emitted class: a transpiled struct with a
    /// runtime class (not <c>[PlainObject]</c>, <c>[JsTuple]</c>, <c>[External]</c> or
    /// <c>[Import]</c>) that declares no constructor. A struct that declares a
    /// parameterized or primary constructor emits that one, so a parameterless
    /// construction would not type-check; one that declares a parameterless
    /// constructor would run it, which C#'s zero value skips.
    /// </summary>
    private static bool IsDefaultConstructible(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    ) =>
        IsStructWithRuntimeClass(named, target)
        && named.InstanceConstructors.All(ctor => ctor.IsImplicitlyDeclared);

    /// <summary>
    /// Whether <paramref name="named"/> is a struct with a runtime class that declares
    /// a constructor: its zero value cannot be built by a parameterless construction
    /// (see <see cref="IsDefaultConstructible"/>), so a <c>default</c> of it reads the
    /// struct's own zero-value factory (#802).
    /// </summary>
    public static bool HasZeroValueFactory(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    ) =>
        IsStructWithRuntimeClass(named, target)
        && named.InstanceConstructors.Any(ctor => !ctor.IsImplicitlyDeclared);

    /// <summary>
    /// Whether <paramref name="named"/> is a struct with a runtime class whose
    /// dispatched constructor set leaves C#'s implicit parameterless
    /// constructor in place, which the targets add as one more overload
    /// (spec 013 FR-027).
    /// </summary>
    private static bool HasImplicitParameterlessOverload(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    ) =>
        IsStructWithRuntimeClass(named, target)
        && IrConstructorExtractor.IsDispatchedSet(named)
        && !IrConstructorExtractor
            .AllExplicitConstructors(named)
            .Any(ctor => ctor.Parameters.IsEmpty);

    private static bool HasExplicitParameterlessCtor(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    ) =>
        IsStructWithRuntimeClass(named, target)
        && IrConstructorExtractor
            .AllExplicitConstructors(named)
            .Any(ctor => ctor.Parameters.IsEmpty);

    /// <summary>
    /// A transpiled struct with a runtime class: not <c>[PlainObject]</c>,
    /// <c>[JsTuple]</c>, <c>[External]</c> or <c>[Import]</c>.
    /// </summary>
    private static bool IsStructWithRuntimeClass(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    ) =>
        named.TypeKind == TypeKind.Struct
        && IsTranspilableType(named, target)
        && !SymbolHelper.HasPlainObject(named)
        && !named.HasJsTuple()
        && !SymbolHelper.HasExternal(named)
        && !SymbolHelper.HasImport(named);

    /// <summary>
    /// The member that <c>default(E)</c> resolves to: the one whose constant is
    /// <c>0</c>, whatever the underlying type and the declaration order, also
    /// for a <c>[StringEnum]</c> (#409). <c>null</c> when the enum declares no
    /// zero member, where <c>default(E)</c> is a bare <c>0</c> no member names.
    /// </summary>
    private static IrEnumMemberInfo? ExtractEnumZeroMember(
        INamedTypeSymbol enumType,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (DefaultValueLiterals.FindEnumMember(enumType, 0) is not { } zero)
            return null;

        return new IrEnumMemberInfo(zero.Name, SymbolHelper.GetNameOverride(zero, target));
    }

    /// <summary>
    /// The primitive leaves of <paramref name="type"/>'s value equality, or
    /// <c>null</c> when some instance field compares by reference or by an
    /// equality the definition does not fix (see
    /// <see cref="IrNamedTypeSemantics.StructuralEqualityLeaves"/>). Walks the
    /// symbols of the transpiled records and structs it meets (never mapping
    /// one, whose own semantics would walk again), so a cycle ends.
    /// </summary>
    private static IReadOnlySet<IrPrimitive>? StructuralEqualityLeavesOf(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (!IsTranspilableType(type, target))
            return null;

        var leaves = new HashSet<IrPrimitive>();
        var visiting = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        return CollectEqualityLeaves(type, leaves, visiting, target) ? leaves : null;
    }

    private static bool CollectEqualityLeaves(
        ITypeSymbol type,
        HashSet<IrPrimitive> leaves,
        HashSet<ISymbol> visiting,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
        )
            return CollectEqualityLeaves(nullable.TypeArguments[0], leaves, visiting, target);

        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
            return tuple.TupleElements.All(element =>
                CollectEqualityLeaves(element.Type, leaves, visiting, target)
            );

        if (IsWalkedValueType(type, target))
            return InstanceFieldsHaveEqualityLeaves(
                (INamedTypeSymbol)type,
                leaves,
                visiting,
                target
            );

        // Only a primitive, an enum or a value type can be a leaf. An array, a
        // class, a delegate, a type parameter (one a generic record's own
        // definition leaves open) or a generic type compares by
        // reference or by an equality its definition does not fix, and mapping
        // one would also map the types it names, which may lead back here.
        if (!IsLeafCandidate(type))
            return false;

        var leaf = Map(
            type.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            null,
            target
        ) switch
        {
            IrPrimitiveTypeRef { Primitive: var primitive } => primitive,
            IrNamedTypeRef { Semantics.Kind: IrNamedTypeKind.NumericEnum } => IrPrimitive.Int32,
            IrNamedTypeRef { Semantics.Kind: IrNamedTypeKind.StringEnum } => IrPrimitive.String,
            IrNamedTypeRef
            {
                Semantics:
                { Kind: IrNamedTypeKind.Branded, BrandedUnderlyingPrimitive: { } underlying },
            } => underlying,
            _ => (IrPrimitive?)null,
        };

        if (leaf is not { } found)
            return false;

        leaves.Add(found);

        return true;
    }

    private static bool IsLeafCandidate(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: false } named
        && (
            named.SpecialType is not SpecialType.None
            || named.TypeKind is TypeKind.Enum or TypeKind.Struct
        );

    /// <summary>
    /// A transpiled record or struct the walk enters field by field: not a
    /// primitive-backed branded wrapper (one value) nor an enum.
    /// </summary>
    private static bool IsWalkedValueType(
        ITypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named
        && (named.IsRecord || named.TypeKind is TypeKind.Struct)
        && !SymbolHelper.IsPrimitiveBackedBranded(named)
        && IsTranspilableType(named, target);

    /// <summary>
    /// Whether the generated, field-wise equality of <paramref name="type"/>
    /// is the whole of its equality, read from fields the walk can see: no
    /// derived type can add state a static type does not show (a struct, a
    /// record struct, a sealed record), no user <c>Equals</c> replaces the
    /// generated one, and the definition and its bases come from source, as a
    /// metadata reference hides the private backing fields.
    /// </summary>
    private static bool HasVisibleFieldwiseEquality(INamedTypeSymbol type) =>
        (type.TypeKind is TypeKind.Struct || type.IsSealed)
        && !DeclaresOwnEquals(type)
        && BaseChainOf(type).All(current => !current.DeclaringSyntaxReferences.IsEmpty);

    /// <summary>
    /// Whether <paramref name="type"/> or a base declares an <c>Equals</c> of
    /// its own: an override or overload, or an explicit
    /// <c>IEquatable&lt;T&gt;.Equals</c>, whose metadata name is not
    /// <c>Equals</c>.
    /// </summary>
    private static bool DeclaresOwnEquals(INamedTypeSymbol type) =>
        BaseChainOf(type)
            .SelectMany(current => current.GetMembers().OfType<IMethodSymbol>())
            .Any(method => !method.IsImplicitlyDeclared && IsEqualsMethod(method));

    private static bool IsEqualsMethod(IMethodSymbol method) =>
        method.Name == nameof(Equals)
        || method.ExplicitInterfaceImplementations.Any(implemented =>
            implemented.Name == nameof(Equals)
        );

    /// <summary>
    /// <paramref name="type"/> and the base types its equality reads fields
    /// from, up to <c>object</c> or <c>ValueType</c>.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> BaseChainOf(INamedTypeSymbol type)
    {
        for (
            var current = type;
            current
                is {
                    SpecialType: not SpecialType.System_Object and not SpecialType.System_ValueType
                };
            current = current.BaseType
        )
            yield return current;
    }

    private static bool InstanceFieldsHaveEqualityLeaves(
        INamedTypeSymbol type,
        HashSet<IrPrimitive> leaves,
        HashSet<ISymbol> visiting,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (!HasVisibleFieldwiseEquality(type.OriginalDefinition))
            return false;

        // A constructed type is visited as itself: Box<Box<Account>> must walk
        // the inner Box, which a visit of the Box definition would skip. An
        // instantiation that grows without end (Node<T> holding a
        // Node<Node<T>>) is not a value the walk can settle.
        if (visiting.Count >= MaxEqualityWalkDepth)
            return false;

        if (!visiting.Add(type))
            return true;

        return InstanceFieldsOf(type)
            .All(field => CollectEqualityLeaves(field.Type, leaves, visiting, target));
    }

    /// <summary>
    /// The instance fields a record's or struct's equality compares, the
    /// inherited ones of a derived record included.
    /// </summary>
    private static IEnumerable<IFieldSymbol> InstanceFieldsOf(INamedTypeSymbol type) =>
        BaseChainOf(type)
            .SelectMany(current => current.GetMembers().OfType<IFieldSymbol>())
            .Where(field => !field.IsStatic && !field.IsConst);

    /// <summary>
    /// The primitive a primitive-backed <c>[Branded]</c> wrapper brands, read
    /// from its single value member (<see cref="SymbolHelper.GetBrandedValueType"/>),
    /// the rule the declaration side reads too. A static member of the wrapper's
    /// own type (<c>static readonly UId Zero</c>) is never the value member.
    /// </summary>
    private static IrPrimitive? BrandedUnderlyingPrimitiveOf(INamedTypeSymbol wrapper) =>
        SymbolHelper.GetBrandedValueType(wrapper.OriginalDefinition) is { } valueType
        && Map(valueType) is IrPrimitiveTypeRef { Primitive: var primitive }
            ? primitive
            : null;

    /// <summary>
    /// Whether <paramref name="type"/> is an exception with no declaration on
    /// the target side: a BCL exception, or one from another non-transpilable
    /// assembly. Its constructions are classified by argument role
    /// (<see cref="IrExceptionConstructionExtractor"/>) and the target lowers
    /// them to its own class for the type.
    /// </summary>
    internal static bool IsExceptionWithoutTargetDeclaration(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    ) => IsExceptionType(type) && !IsTranspilableType(type, target);

    internal static bool IsExceptionType(INamedTypeSymbol type)
    {
        // Include `System.Exception` itself so `new Exception(msg)` lowers
        // to the TS `Error` fallback — not just subclasses.
        if (type.ToDisplayString() == "System.Exception")
            return true;
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// The positional element count of a <c>[JsTuple]</c> type: its primary
    /// constructor's parameters when the declaration is source, otherwise its
    /// single <c>Deconstruct</c> (synthesized for every positional record and
    /// visible in metadata, so a referenced-package tuple keeps its arity).
    /// <c>null</c> when neither shape is available.
    /// </summary>
    private static int? JsTupleArityOf(INamedTypeSymbol named) =>
        SymbolHelper.GetPositionalPrimaryConstructor(named)?.Parameters.Length
        ?? SingleDeconstructArity(named);

    private static int? SingleDeconstructArity(INamedTypeSymbol named) =>
        named.GetMembers("Deconstruct").OfType<IMethodSymbol>().ToList() is [var deconstruct]
            ? deconstruct.Parameters.Length
            : null;

    /// <summary>
    /// Returns <c>true</c> when the type is transpilable — either it carries
    /// <c>[Transpile]</c> explicitly, or its containing assembly has
    /// <c>[assembly: TranspileAssembly]</c> and the type itself is public
    /// (and not opted out via <c>[Ignore]</c>).
    /// <para>
    /// <see cref="IrTypeRefMapper"/> can't see the compilation's assembly,
    /// so we check the symbol's own containing assembly here. That's
    /// sufficient because a <c>[TranspileAssembly]</c> attribute on a
    /// BCL / third-party assembly would be impossible to place from user
    /// code — only the user's own compilation can tag itself.
    /// </para>
    /// <para>
    /// <paramref name="target"/> threads the active backend so a
    /// <c>[Ignore(TargetLanguage.Dart)]</c> type is treated as
    /// non-transpilable for the Dart backend while remaining transpilable
    /// for TypeScript.
    /// </para>
    /// </summary>
    internal static bool IsTranspilableType(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (SymbolHelper.HasTranspile(named))
            return true;
        if (SymbolHelper.HasIgnore(named, target))
            return false;

        if (Frontend.Roslyn.InternalTypeReachability.IsReached(named))
            return true;

        if (
            named.DeclaredAccessibility != Accessibility.Public
            && !IsNestedInTranspilableContainer(named, target)
        )
            return false;

        var asm = named.ContainingAssembly;
        if (asm is null)
            return false;
        return asm.GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly"
            );
    }

    /// <summary>
    /// The type arguments of the generic containers of <paramref name="named"/>
    /// (<see cref="IrNamedTypeRef.ContainerTypeArguments"/>), or <c>null</c>.
    /// </summary>
    private static IReadOnlyList<IrTypeRef>? ContainerTypeArgumentsOf(
        INamedTypeSymbol named,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var arguments = GenericContainers.TypeArgumentsOf(named);

        return arguments.Count > 0
            ? arguments.Select(argument => Map(argument, originResolver, target)).ToList()
            : null;
    }

    /// <summary>
    /// A named, source-declared nested type of any accessibility whose container is
    /// itself transpilable: part of the container's emitted surface, as the
    /// <c>[assembly: TranspileAssembly]</c> inclusion rule has it (#264), so a
    /// reference to it — a base class among them — is kept (#422).
    /// </summary>
    private static bool IsNestedInTranspilableContainer(
        INamedTypeSymbol named,
        Metano.Annotations.TargetLanguage? target
    ) =>
        named.ContainingType is { } container
        && !string.IsNullOrEmpty(named.Name)
        && !named.IsImplicitlyDeclared
        && IsTranspilableType(container, target);

    private static IrFunctionTypeRef MapDelegateType(
        INamedTypeSymbol delegateType,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var invoke = delegateType.GetMembers("Invoke").OfType<IMethodSymbol>().FirstOrDefault();

        if (invoke is null)
            return new IrFunctionTypeRef([], new IrPrimitiveTypeRef(IrPrimitive.Void));

        return BuildFunctionType(invoke, originResolver, target);
    }

    /// <summary>
    /// Maps an <c>Invoke</c> method symbol (a delegate's, or a
    /// <c>[JsCallable]</c> interface's) to an <see cref="IrFunctionTypeRef"/>.
    /// <c>[This]</c> on the first parameter promotes it to the synthetic
    /// <c>this</c> receiver slot; the parameter itself is dropped from the
    /// positional list so each backend picks the emission shape it prefers —
    /// TypeScript prepends a <c>(this: T, …)</c> annotation, Dart re-introduces
    /// the parameter as a regular positional arg in its bridge.
    /// </summary>
    private static IrFunctionTypeRef BuildFunctionType(
        IMethodSymbol invoke,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        IrTypeRef? thisType = null;
        var sourceParameters = invoke.Parameters;
        if (sourceParameters.Length > 0 && SymbolHelper.HasThis(sourceParameters[0]))
        {
            thisType = Map(sourceParameters[0].Type, originResolver, target);
            sourceParameters = sourceParameters.RemoveAt(0);
        }

        var parameters = sourceParameters
            .Select(p => new IrParameter(
                p.Name,
                Map(p.Type, originResolver, target),
                IsParams: p.IsParams
            ))
            .ToList();

        var returnType = invoke.ReturnsVoid
            ? new IrPrimitiveTypeRef(IrPrimitive.Void)
            : Map(invoke.ReturnType, originResolver, target);

        return new IrFunctionTypeRef(parameters, returnType, thisType);
    }

    /// <summary>
    /// Builds a qualified name for nested types: <c>Outer.Inner</c>.
    /// Top-level types return their simple name. Each segment honors any
    /// <c>[Name]</c> override resolved against <paramref name="target"/>,
    /// so a class `User` tagged `[Name("ApiUser")]` surfaces as `ApiUser`
    /// in the resulting IR reference.
    /// </summary>
    public static string BuildQualifiedName(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        string Part(INamedTypeSymbol sym) => SymbolHelper.GetNameOverride(sym, target) ?? sym.Name;

        return string.Join(".", ContainmentPath(type).Select(Part));
    }

    /// <summary>
    /// Whether references to <paramref name="type"/> are ambient: the type, or any type
    /// containing it, is <c>[External]</c>. A type nested in an <c>[External]</c> container
    /// is never emitted, so it is reached through the container's global like the
    /// container itself.
    /// </summary>
    public static bool IsAmbient(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (SymbolHelper.HasExternal(current))
                return true;
        }

        return false;
    }

    /// <summary>
    /// How many leading segments of <see cref="BuildQualifiedName"/> lead to a
    /// <c>private</c> nested declaration (#412): the path up to and including the
    /// deepest <c>private</c> nested segment, which a target that hides private nested
    /// types may declare outside the container's exported surface. <c>0</c> when no
    /// segment is private. Only the fact is recorded — every IR type reference carries
    /// it next to its qualified name; how a target spells the prefix is its own naming
    /// policy.
    /// </summary>
    public static int PrivateNestedSegmentCount(INamedTypeSymbol type) =>
        ContainmentPath(type).FindLastIndex(SymbolHelper.IsContainerPrivateNestedType) + 1;

    /// <summary>The containers of <paramref name="type"/>, outermost first, ending with it.</summary>
    private static List<INamedTypeSymbol> ContainmentPath(INamedTypeSymbol type)
    {
        var path = new List<INamedTypeSymbol>();

        for (var current = type; current is not null; current = current.ContainingType)
            path.Insert(0, current);

        return path;
    }

    /// <summary>
    /// The C# namespace an <see cref="IrNamedTypeRef"/> of <paramref name="type"/>
    /// carries: <c>null</c> for the global one.
    /// </summary>
    internal static string? NamespaceOf(INamedTypeSymbol type) => GetNamespace(type);

    private static string? GetNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return null;
        return ns.ToDisplayString();
    }

    private static bool IsTaskLike(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition.ToDisplayString();
        return original
            is "System.Threading.Tasks.Task"
                or "System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask"
                or "System.Threading.Tasks.ValueTask<TResult>";
    }
}
