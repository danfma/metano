using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The implicit parameterless constructor of a dispatched struct (spec 013
/// FR-027, DECIDED-20): C# lets <c>new S()</c> build <c>default(S)</c> beside
/// the declared constructors, so the dispatcher gains a <c>constructor()</c>
/// signature and a first branch whose body this bridge builds — every
/// instance field takes the value <c>default</c> gives its type, whatever its
/// initializer says:
/// <list type="bullet">
///   <item>a reference type or a type parameter not constrained to a value
///   type: <c>null</c>,
///   asserted (<c>null as never</c>) since the field's TypeScript type need
///   not admit it;</item>
///   <item>a number, <c>bool</c>, <c>decimal</c>, enum or nullable: its
///   implicit initializer (for an enum without a zero member, its smallest
///   member — the class emitter's existing rule, not C#'s <c>0</c>); <c>char</c>: <c>"\0"</c>; <c>BigInteger</c>:
///   <c>0n</c>; a branded value: its underlying zero;</item>
///   <item>a struct that <c>new S()</c> zeroes — one declaring no constructor
///   (its implicit initializer), or one with an implicit parameterless
///   overload (<see cref="IrNamedTypeSemantics.HasImplicitParameterlessOverload"/>):
///   <c>new S()</c>.</item>
/// </list>
/// Any other value type is not zeroed yet: the date and time types, a struct
/// with a single declared constructor or one whose <c>new S()</c> runs a user
/// constructor, a tuple, a type parameter constrained to a value type, and
/// <c>Guid</c> (expressible as <c>UUID.empty</c>, not yet built here). The
/// field is left <c>undefined</c> and <c>MS0001</c> says so, as an
/// unrepresentable <c>default</c> out argument does (plan.md § 013b hand-offs). Every decision reads the
/// IR; the Roslyn type only locates the diagnostic.
/// </summary>
internal static class IrToTsStructDefaultConstructorBridge
{
    /// <summary>
    /// The branch body; <c>null</c> for a reference type, or a struct that
    /// declares its own parameterless constructor (an ordinary overload).
    /// </summary>
    /// <param name="ir">The type's IR.</param>
    /// <param name="type">The type, which locates an unrepresentable default.</param>
    /// <param name="report">Where an unrepresentable default is reported.</param>
    public static IReadOnlyList<TsStatement>? BuildBody(
        IrClassDeclaration ir,
        INamedTypeSymbol type,
        Action<MetanoDiagnostic> report
    )
    {
        if (!ir.Semantics.IsValueType || DeclaresParameterless(ir))
        {
            return null;
        }

        var typeParameters = ir.TypeParameters ?? [];

        return StorageOf(ir)
            .Select(storage => DefaultAssignment(storage, typeParameters, type, report))
            .ToList();
    }

    private static TsStatement DefaultAssignment(
        StructStorage storage,
        IReadOnlyList<IrTypeParameter> typeParameters,
        INamedTypeSymbol type,
        Action<MetanoDiagnostic> report
    )
    {
        var value =
            DefaultValueOf(storage.Type, typeParameters) ?? Unrepresentable(storage, type, report);

        return Assignment(storage, value);
    }

    private static bool DeclaresParameterless(IrClassDeclaration ir) =>
        ir.Constructor?.DeclarationsInOrder.Any(ctor => ctor.Parameters.Count == 0) == true;

    private static TsStatement Assignment(StructStorage storage, TsExpression value) =>
        new TsExpressionStatement(
            new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("this"), storage.Name),
                "=",
                value
            )
        );

    /// <summary>The value <c>default</c> gives the type; <c>null</c> when the target has none.</summary>
    private static TsExpression? DefaultValueOf(
        IrTypeRef type,
        IReadOnlyList<IrTypeParameter> typeParameters
    ) =>
        IrToTsClassBridge.ComputeDefaultInitializer(type)
        ?? type switch
        {
            IrPrimitiveTypeRef primitive => PrimitiveDefaultOf(primitive.Primitive),
            IrTypeParameterRef parameter => IsValueConstrained(parameter, typeParameters)
                ? null
                : AssertedNull(),
            IrNamedTypeRef named => NamedDefaultOf(named),
            IrTupleTypeRef or IrKeyValuePairTypeRef => null,
            // Every other IR shape (array, map, set, function, promise,
            // iterable…) lowers a reference type.
            _ => AssertedNull(),
        };

    /// <summary>
    /// <c>char</c>'s <c>"\0"</c>, <c>BigInteger</c>'s <c>0n</c>, <c>null</c>
    /// for <c>string</c> and <c>object</c>; none for <c>Guid</c> and the date
    /// and time types. The numbers, <c>bool</c> and <c>decimal</c> have their
    /// implicit initializer.
    /// </summary>
    private static TsExpression? PrimitiveDefaultOf(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Char => new TsStringLiteral("\0"),
            IrPrimitive.BigInteger => new TsLiteral("0n"),
            IrPrimitive.String or IrPrimitive.Object => AssertedNull(),
            _ => null,
        };

    private static TsExpression? NamedDefaultOf(IrNamedTypeRef named) =>
        named.Semantics switch
        {
            { BrandedUnderlyingPrimitive: { } underlying } => BrandedZeroOf(underlying),
            { HasImplicitParameterlessOverload: true } => ParameterlessNewOf(named),
            { IsValueType: true } => null,
            _ => AssertedNull(),
        };

    private static bool IsValueConstrained(
        IrTypeParameterRef parameter,
        IReadOnlyList<IrTypeParameter> typeParameters
    ) =>
        typeParameters.Any(declared =>
            declared.Name == parameter.Name
            && declared.KindConstraint is IrTypeKindConstraint.ValueType
        );

    private static TsExpression ParameterlessNewOf(IrNamedTypeRef named) =>
        IrToTsExpressionBridge.Map(new IrNewExpression(named, []), IrToTsBridgeContext.For(null));

    private static TsExpression? BrandedZeroOf(IrPrimitive underlying) =>
        IrToTsClassBridge.ComputeDefaultInitializer(new IrPrimitiveTypeRef(underlying)) is { } zero
            ? new TsCastExpression(zero, new TsNamedType("never"))
            : null;

    private static TsExpression AssertedNull() =>
        new TsCastExpression(new TsLiteral("null"), new TsNamedType("never"));

    private static ITypeSymbol? TypeOf(ISymbol? member) =>
        member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

    private static TsExpression Unrepresentable(
        StructStorage storage,
        INamedTypeSymbol type,
        Action<MetanoDiagnostic> report
    )
    {
        var member = type.GetMembers(storage.MemberName).FirstOrDefault();
        var typeName =
            TypeOf(member)?.ToDisplayString()
            ?? Printer.RenderType(IrToTsTypeMapper.Map(storage.Type));

        report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"The default value of '{typeName}' has no literal in the target: "
                    + $"'new {type.Name}()' leaves '{storage.Name}' undefined instead of the "
                    + "zeroed value (spec 013 FR-027).",
                (member ?? type).Locations.FirstOrDefault()
            )
        );

        return new TsCastExpression(new TsIdentifier("undefined"), new TsNamedType("never"));
    }

    /// <summary>
    /// The struct's instance storage, in declaration order: its promoted
    /// members, then its instance fields and auto-properties.
    /// </summary>
    private static IEnumerable<StructStorage> StorageOf(IrClassDeclaration ir)
    {
        var declared = (ir.Members ?? []).Select(DeclaredStorageOf).OfType<StructStorage>();

        return PromotedStorageOf(ir.Constructor)
            .Concat(declared)
            .DistinctBy(storage => storage.Name);
    }

    private static IEnumerable<StructStorage> PromotedStorageOf(IrConstructorDeclaration? ctor) =>
        ctor is { IsPrimary: true }
            ? ctor
                .Parameters.Where(parameter => parameter.Promotion is not IrParameterPromotion.None)
                .Select(parameter => new StructStorage(
                    IrToTsNamingPolicy.ToDataMemberName(
                        parameter.Parameter.Name,
                        parameter.EmittedName
                    ),
                    parameter.Parameter.Name,
                    parameter.Parameter.Type
                ))
            : [];

    private static StructStorage? DeclaredStorageOf(IrMemberDeclaration member) =>
        member switch
        {
            IrFieldDeclaration { IsStatic: false } field => new StructStorage(
                IrToTsNamingPolicy.ToDeclaredDataMemberName(field.Name, field.Attributes),
                field.Name,
                field.Type
            ),
            IrPropertyDeclaration { IsStatic: false } property when IsAutoProperty(property) =>
                new StructStorage(
                    IrToTsNamingPolicy.ToDeclaredDataMemberName(property.Name, property.Attributes),
                    property.Name,
                    property.Type
                ),
            _ => null,
        };

    /// <summary>
    /// A property the class emitter declares as a field: not abstract, with
    /// neither a getter nor a setter body.
    /// </summary>
    private static bool IsAutoProperty(IrPropertyDeclaration property) =>
        property.Semantics is not { IsAbstract: true }
        && property.Semantics?.HasGetterBody != true
        && property.Semantics?.HasSetterBody != true;

    /// <summary>
    /// One instance member that holds a struct's state: its emitted name, its
    /// C# name and its type.
    /// </summary>
    private sealed record StructStorage(string Name, string MemberName, IrTypeRef Type);
}
