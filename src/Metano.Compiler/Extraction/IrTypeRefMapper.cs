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
            return new IrNullableTypeRef(Map(nullable.TypeArguments[0], originResolver));
        }

        // Nullable reference types (string?, Money?, etc.)
        if (
            type.NullableAnnotation == NullableAnnotation.Annotated
            && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
        )
        {
            return new IrNullableTypeRef(
                Map(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), originResolver)
            );
        }

        // Primitives via SpecialType
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return new IrPrimitiveTypeRef(IrPrimitive.Boolean);
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                return new IrPrimitiveTypeRef(IrPrimitive.Byte);
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
                return new IrPrimitiveTypeRef(IrPrimitive.Int16);
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
                return new IrPrimitiveTypeRef(IrPrimitive.Int32);
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return new IrPrimitiveTypeRef(IrPrimitive.Int64);
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
                        ? Map(named.TypeArguments[0], originResolver)
                        : new IrPrimitiveTypeRef(IrPrimitive.Void);
                return new IrPromiseTypeRef(inner);
            }

            // BigInteger
            if (fullName == "System.Numerics.BigInteger")
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

            // Dictionary-like → Map
            if (named.IsDictionaryLike() && named.TypeArguments.Length >= 2)
                return new IrMapTypeRef(
                    Map(named.TypeArguments[0], originResolver),
                    Map(named.TypeArguments[1], originResolver)
                );

            // Set-like → Set
            if (named.IsSetLike() && named.TypeArguments.Length > 0)
                return new IrSetTypeRef(Map(named.TypeArguments[0], originResolver));

            // KeyValuePair<K,V> → KeyValuePair
            if (
                fullName.StartsWith("System.Collections.Generic.KeyValuePair")
                && named.TypeArguments.Length >= 2
            )
                return new IrKeyValuePairTypeRef(
                    Map(named.TypeArguments[0], originResolver),
                    Map(named.TypeArguments[1], originResolver)
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
                    named.TypeArguments.Select(a => Map(a, originResolver)).ToList()
                );

            // IGrouping<K,V>
            if (fullName.StartsWith("System.Linq.IGrouping") && named.TypeArguments.Length >= 2)
                return new IrGroupingTypeRef(
                    Map(named.TypeArguments[0], originResolver),
                    Map(named.TypeArguments[1], originResolver)
                );

            // IReadOnlyCollection<T> → Iterable
            if (
                fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection")
                && named.TypeArguments.Length > 0
            )
                return new IrIterableTypeRef(Map(named.TypeArguments[0], originResolver));

            // Collection-like → Array
            if (named.IsCollectionLike() && named.TypeArguments.Length > 0)
                return new IrArrayTypeRef(Map(named.TypeArguments[0], originResolver));

            // Delegate types → function type
            if (named.TypeKind == TypeKind.Delegate)
                return MapDelegateType(named, originResolver);

            // User-defined named types: may carry a cross-package origin.
            var origin = originResolver?.Invoke(named);
            var semantics = BuildNamedTypeSemantics(named, target);

            if (named.TypeArguments.Length > 0)
            {
                var args = named.TypeArguments.Select(a => Map(a, originResolver, target)).ToList();
                return new IrNamedTypeRef(
                    BuildQualifiedName(named, target),
                    GetNamespace(named),
                    args,
                    origin,
                    semantics
                );
            }

            return new IrNamedTypeRef(
                BuildQualifiedName(named, target),
                GetNamespace(named),
                Origin: origin,
                Semantics: semantics
            );
        }

        // Type parameters (T, K, V)
        if (type is ITypeParameterSymbol typeParam)
            return new IrTypeParameterRef(typeParam.Name);

        // Array types
        if (type is IArrayTypeSymbol array)
            return new IrArrayTypeRef(Map(array.ElementType, originResolver));

        return new IrUnknownTypeRef();
    }

    /// <summary>
    /// Maps a return type for iterator methods (yield). IEnumerable/IEnumerator → Generator.
    /// </summary>
    public static IrTypeRef MapForGeneratorReturn(
        ITypeSymbol type,
        IrTypeOriginResolver? originResolver = null
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
                return new IrGeneratorTypeRef(Map(named.TypeArguments[0], originResolver));
            }
        }

        return Map(type, originResolver);
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
                    IsTranspilable: IsTranspilableType(named),
                    IsNoEmit: SymbolHelper.HasNoEmit(named),
                    EnumDefaultMember: ExtractEnumDefaultMember(named, target)
                );
            }
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.NumericEnum,
                IsTranspilable: IsTranspilableType(named),
                IsNoEmit: SymbolHelper.HasNoEmit(named),
                EnumDefaultMember: ExtractEnumDefaultMember(named, target)
            );
        }

        if (named.TypeKind == TypeKind.Interface)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Interface,
                IsTranspilable: IsTranspilableType(named),
                IsNoEmit: SymbolHelper.HasNoEmit(named)
            );

        if (named.TypeKind == TypeKind.Delegate)
            return new IrNamedTypeSemantics(IrNamedTypeKind.Delegate);

        // Inline wrappers collapse to a branded primitive at runtime. The
        // underlying primitive comes from the first real field / property.
        if (SymbolHelper.HasInlineWrapper(named))
        {
            var primitive = DetectInlineWrapperPrimitive(named);
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.InlineWrapper,
                InlineWrappedPrimitive: primitive,
                IsTranspilable: IsTranspilableType(named),
                IsNoEmit: SymbolHelper.HasNoEmit(named)
            );
        }

        if (IsExceptionType(named))
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Exception,
                IsTranspilable: IsTranspilableType(named),
                IsNoEmit: SymbolHelper.HasNoEmit(named)
            );

        if (named.IsRecord)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Record,
                IsTranspilable: IsTranspilableType(named),
                IsNoEmit: SymbolHelper.HasNoEmit(named)
            );

        if (named.TypeKind == TypeKind.Struct)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Struct,
                IsTranspilable: IsTranspilableType(named),
                IsNoEmit: SymbolHelper.HasNoEmit(named)
            );

        if (named.TypeKind == TypeKind.Class)
            return new IrNamedTypeSemantics(
                IrNamedTypeKind.Class,
                IsTranspilable: IsTranspilableType(named),
                IsNoEmit: SymbolHelper.HasNoEmit(named)
            );

        return null;
    }

    /// <summary>
    /// Picks the member that <c>default(E)</c> resolves to. For numeric enums
    /// that's the member with the smallest constant value (typically 0); for
    /// string enums Roslyn doesn't expose ordering on the value, so we fall
    /// back to the source-declaration order. Returns <c>null</c> when the
    /// enum has no constant members (a malformed declaration).
    /// </summary>
    private static IrEnumMemberInfo? ExtractEnumDefaultMember(
        INamedTypeSymbol enumType,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var members = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).ToList();
        if (members.Count == 0)
            return null;

        var first = SymbolHelper.HasStringEnum(enumType)
            ? members[0]
            : members
                .OrderBy(f =>
                    f.ConstantValue switch
                    {
                        int i => (long)i,
                        long l => l,
                        _ => long.MaxValue,
                    }
                )
                .First();

        var emittedName = SymbolHelper.GetNameOverride(first, target);
        return new IrEnumMemberInfo(first.Name, emittedName);
    }

    private static IrPrimitive? DetectInlineWrapperPrimitive(INamedTypeSymbol wrapper)
    {
        var field = wrapper
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => !f.IsImplicitlyDeclared);
        if (field is not null && Map(field.Type) is IrPrimitiveTypeRef p)
            return p.Primitive;
        var prop = wrapper
            .GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsImplicitlyDeclared);
        if (prop is not null && Map(prop.Type) is IrPrimitiveTypeRef pp)
            return pp.Primitive;
        return null;
    }

    private static bool IsExceptionType(INamedTypeSymbol type)
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
    /// Returns <c>true</c> when the type is transpilable — either it carries
    /// <c>[Transpile]</c> explicitly, or its containing assembly has
    /// <c>[assembly: TranspileAssembly]</c> and the type itself is public
    /// (and not opted out via <c>[NoTranspile]</c>/<c>[NoEmit]</c>).
    /// <para>
    /// <see cref="IrTypeRefMapper"/> can't see the compilation's assembly,
    /// so we check the symbol's own containing assembly here. That's
    /// sufficient because a <c>[TranspileAssembly]</c> attribute on a
    /// BCL / third-party assembly would be impossible to place from user
    /// code — only the user's own compilation can tag itself.
    /// </para>
    /// </summary>
    private static bool IsTranspilableType(INamedTypeSymbol named)
    {
        if (SymbolHelper.HasTranspile(named))
            return true;
        if (SymbolHelper.HasNoTranspile(named) || SymbolHelper.HasNoEmit(named))
            return false;
        if (named.DeclaredAccessibility != Accessibility.Public)
            return false;
        var asm = named.ContainingAssembly;
        if (asm is null)
            return false;
        return asm.GetAttributes()
            .Any(a =>
                a.AttributeClass?.Name is "TranspileAssemblyAttribute" or "TranspileAssembly"
            );
    }

    private static IrFunctionTypeRef MapDelegateType(
        INamedTypeSymbol delegateType,
        IrTypeOriginResolver? originResolver
    )
    {
        var invoke = delegateType.GetMembers("Invoke").OfType<IMethodSymbol>().FirstOrDefault();

        if (invoke is null)
            return new IrFunctionTypeRef([], new IrPrimitiveTypeRef(IrPrimitive.Void));

        // `[This]` on the first parameter promotes it to the
        // synthetic `this` receiver slot. The parameter itself is
        // dropped from the positional list so each backend picks
        // the emission shape it prefers — TypeScript prepends a
        // `(this: T, …)` annotation, Dart re-introduces the
        // parameter as a regular positional arg in its bridge.
        IrTypeRef? thisType = null;
        var sourceParameters = invoke.Parameters;
        if (sourceParameters.Length > 0 && SymbolHelper.HasThis(sourceParameters[0]))
        {
            thisType = Map(sourceParameters[0].Type, originResolver);
            sourceParameters = sourceParameters.RemoveAt(0);
        }

        var parameters = sourceParameters
            .Select(p => new IrParameter(p.Name, Map(p.Type, originResolver)))
            .ToList();

        var returnType = invoke.ReturnsVoid
            ? new IrPrimitiveTypeRef(IrPrimitive.Void)
            : Map(invoke.ReturnType, originResolver);

        return new IrFunctionTypeRef(parameters, returnType, thisType);
    }

    /// <summary>
    /// Builds a qualified name for nested types: <c>Outer.Inner</c>.
    /// Top-level types return their simple name. Each segment honors any
    /// <c>[Name]</c> override resolved against <paramref name="target"/>,
    /// so a class `User` tagged `[Name("ApiUser")]` surfaces as `ApiUser`
    /// in the resulting IR reference.
    /// </summary>
    private static string BuildQualifiedName(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        string Part(INamedTypeSymbol sym) => SymbolHelper.GetNameOverride(sym, target) ?? sym.Name;
        if (type.ContainingType is null)
            return Part(type);

        var parts = new List<string> { Part(type) };
        var current = type.ContainingType;
        while (current is not null)
        {
            parts.Insert(0, Part(current));
            current = current.ContainingType;
        }
        return string.Join(".", parts);
    }

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
