using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
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
///   <item>a reference type or an unconstrained type parameter: <c>null</c>,
///   asserted (<c>null as never</c>) since the field's TypeScript type need
///   not admit it;</item>
///   <item>a number, <c>bool</c>, <c>decimal</c>, enum or nullable: its
///   implicit initializer; <c>char</c>: <c>"\0"</c>; <c>BigInteger</c>:
///   <c>0n</c>; a branded value: its underlying zero;</item>
///   <item>a transpiled struct that <c>new S()</c> zeroes — a dispatched one
///   without a declared parameterless constructor (one declaring no
///   constructor is covered by its implicit initializer): <c>new S()</c>.</item>
/// </list>
/// Any other value type (<c>Guid</c>, the date and time types, a struct whose
/// <c>new S()</c> runs a user constructor) has no zero value in the target:
/// the field is left <c>undefined</c> and <c>MS0001</c> says so, as an
/// unrepresentable <c>default</c> out argument does.
/// </summary>
internal static class IrToTsStructDefaultConstructorBridge
{
    /// <summary>
    /// The branch body; <c>null</c> for a reference type, or a struct that
    /// declares its own parameterless constructor (an ordinary overload).
    /// </summary>
    /// <param name="ir">The type's IR.</param>
    /// <param name="type">The type, whose members' C# types decide the defaults.</param>
    /// <param name="report">Where an unrepresentable default is reported.</param>
    public static IReadOnlyList<TsStatement>? BuildBody(
        IrClassDeclaration ir,
        INamedTypeSymbol type,
        Action<MetanoDiagnostic> report
    )
    {
        if (!ir.Semantics.IsValueType || DeclaresParameterless(type))
            return null;

        return StorageOf(ir).Select(storage => DefaultAssignment(storage, type, report)).ToList();
    }

    private static bool DeclaresParameterless(INamedTypeSymbol type) =>
        IrConstructorExtractor.AllExplicitConstructors(type).Any(ctor => ctor.Parameters.IsEmpty);

    private static TsStatement DefaultAssignment(
        StructStorage storage,
        INamedTypeSymbol type,
        Action<MetanoDiagnostic> report
    )
    {
        var member = type.GetMembers(storage.MemberName).FirstOrDefault();

        var value =
            DefaultValueOf(storage.Type, TypeOf(member))
            ?? Unrepresentable(storage, type, member, report);

        return new TsExpressionStatement(
            new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("this"), storage.Name),
                "=",
                value
            )
        );
    }

    private static ITypeSymbol? TypeOf(ISymbol? member) =>
        member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

    /// <summary>The value <c>default</c> gives the type; <c>null</c> when the target has none.</summary>
    private static TsExpression? DefaultValueOf(IrTypeRef irType, ITypeSymbol? type)
    {
        if (type is { IsValueType: false } || irType is IrTypeParameterRef)
            return IrToTsClassBridge.ComputeDefaultInitializer(irType) ?? AssertedNull();

        return IrToTsClassBridge.ComputeDefaultInitializer(irType)
            ?? ValueTypeDefaultOf(irType, type);
    }

    private static TsExpression? ValueTypeDefaultOf(IrTypeRef irType, ITypeSymbol? type) =>
        irType switch
        {
            IrPrimitiveTypeRef { Primitive: IrPrimitive.Char } => new TsStringLiteral("\0"),
            IrPrimitiveTypeRef { Primitive: IrPrimitive.BigInteger } => new TsLiteral("0n"),
            IrNamedTypeRef { Semantics.BrandedUnderlyingPrimitive: { } underlying } =>
                BrandedZeroOf(underlying),
            IrNamedTypeRef { Semantics.IsTranspilable: true } named
                when type is INamedTypeSymbol structType && ZeroesOnParameterlessNew(structType) =>
                IrToTsExpressionBridge.Map(
                    new IrNewExpression(named, []),
                    IrToTsBridgeContext.For(null)
                ),
            _ => null,
        };

    /// <summary>
    /// Whether <c>new S()</c> on the emitted class builds <c>default(S)</c>:
    /// the struct is dispatched (FR-027 gives it this very branch) and
    /// declares no parameterless constructor of its own.
    /// </summary>
    private static bool ZeroesOnParameterlessNew(INamedTypeSymbol structType)
    {
        var constructors = IrConstructorExtractor.AllExplicitConstructors(structType);

        return structType.IsValueType
            && constructors.Count >= 2
            && !constructors.Any(ctor => ctor.Parameters.IsEmpty);
    }

    private static TsExpression? BrandedZeroOf(IrPrimitive underlying) =>
        IrToTsClassBridge.ComputeDefaultInitializer(new IrPrimitiveTypeRef(underlying)) is { } zero
            ? new TsCastExpression(zero, new TsNamedType("never"))
            : null;

    private static TsExpression AssertedNull() =>
        new TsCastExpression(new TsLiteral("null"), new TsNamedType("never"));

    private static TsExpression Unrepresentable(
        StructStorage storage,
        INamedTypeSymbol type,
        ISymbol? member,
        Action<MetanoDiagnostic> report
    )
    {
        var typeName = TypeOf(member)?.ToDisplayString() ?? storage.MemberName;

        report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'default({typeName})' has no literal in the target: 'new {type.Name}()' "
                    + $"leaves '{storage.Name}' undefined instead of the zeroed value "
                    + "(spec 013 FR-027).",
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
