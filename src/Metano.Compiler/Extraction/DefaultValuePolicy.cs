using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The core's one policy for the value C# gives <c>default(T)</c> in an
/// expression position: a <c>default</c> literal or <c>default(T)</c>
/// (#627), a parameter declared <c>= default</c> and the argument a named call
/// passes for it (#676), the value an <c>OrDefault</c> LINQ terminal or
/// <c>List&lt;T&gt;.Find</c> returns when no element qualifies (#601), and
/// the <c>GetValueOrDefault</c>, <c>out</c> and array-element defaults.
/// <see cref="DefaultValueLiterals"/> spells the literals and members;
/// <see cref="IrDefaultValues.KindOf"/>, the same decision a target's
/// implicit member initializer prints from, picks between the zero enum
/// member and a <c>0</c> asserted to the enum, and spells a struct that
/// declares no constructor as <c>new S()</c>.
/// </summary>
internal static class DefaultValuePolicy
{
    /// <summary>
    /// <c>default(T)</c> of <paramref name="type"/> as IR, or <c>null</c> when
    /// no expression spells it: a struct that declares a constructor, a tuple
    /// with such an element or more elements than a flat tuple holds, a
    /// <c>struct</c>-constrained type parameter, and a <c>[StringEnum]</c>
    /// that declares no zero member (#621). A tuple's default is the tuple of
    /// its elements' defaults (#403, #415).
    /// </summary>
    public static IrExpression? Of(
        ITypeSymbol type,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
            return TupleDefault(tuple, buildOrigin, mapType);

        var literal = DefaultValueLiterals.For(type, buildOrigin, mapType);

        return literal switch
        {
            IrLiteral { Kind: IrLiteralKind.Default } => StructDefault(type, buildOrigin, mapType),
            IrLiteral { Kind: IrLiteralKind.Int32 } when type.TypeKind is TypeKind.Enum =>
                EnumZeroAssertion(type, mapType),
            _ => literal,
        };
    }

    /// <summary>
    /// The fields of <paramref name="structType"/> whose value <c>default(S)</c>
    /// does not give when it is spelled <c>new S()</c>: the struct's declaration
    /// gives the field no default (<see cref="IrDefaultValueKind.None"/>: a type
    /// parameter, a <c>DateTime</c>, a tuple, a struct that declares a
    /// constructor) while the field's type in <paramref name="structType"/> is a
    /// value type, whose C# default is not <c>null</c>. A field of a reference
    /// type (<c>ZBoxed&lt;string&gt;.Value</c>) has the <c>null</c> default either way.
    /// </summary>
    public static IReadOnlyList<string> UnsetFieldsOf(
        ITypeSymbol structType,
        Func<ITypeSymbol, IrTypeRef> mapType
    ) =>
        [
            .. structType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => !field.IsStatic && !field.IsConst)
                .Where(field => HoldsAValue(field.Type))
                .Where(field =>
                    IrDefaultValues.KindOf(mapType(field.OriginalDefinition.Type))
                    is IrDefaultValueKind.None
                )
                .Select(field => field.AssociatedSymbol?.Name ?? field.Name),
        ];

    private static bool HoldsAValue(ITypeSymbol type) =>
        type.IsValueType || type is ITypeParameterSymbol { IsReferenceType: false };

    public static void ReportUnrepresentable(
        SyntaxNode call,
        IMethodSymbol method,
        ITypeSymbol elementType
    ) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'default({elementType.ToDisplayString()})' has no literal in the target: when no "
                    + $"element qualifies, '{method.Name}' returns null instead of the default value.",
                call.GetLocation()
            )
        );

    /// <summary>
    /// <c>new S()</c> for a transpiled struct that declares no constructor:
    /// the target's parameterless construction leaves every field at its
    /// default, the zeroed value C# gives it.
    /// </summary>
    private static IrExpression? StructDefault(
        ITypeSymbol type,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        var typeRef = mapType(type);

        if (IrDefaultValues.KindOf(typeRef) is not IrDefaultValueKind.StructConstruction)
            return null;

        var named = (INamedTypeSymbol)type;
        var constructor = named.InstanceConstructors.Single(ctor => ctor.Parameters.IsEmpty);

        IReadOnlyList<IrTypeRef>? typeArguments =
            named.TypeArguments.Length > 0 ? [.. named.TypeArguments.Select(mapType)] : null;

        return new IrNewExpression(
            typeRef,
            [],
            Origin: buildOrigin(constructor),
            TypeArguments: typeArguments
        );
    }

    /// <summary>
    /// The tuple of the element defaults of <paramref name="tuple"/>
    /// (<c>default((int, string))</c> is <c>(0, null)</c>); <c>null</c> when an
    /// element has no default expression or the tuple has more elements than
    /// a flat tuple holds.
    /// </summary>
    private static IrTupleLiteral? TupleDefault(
        INamedTypeSymbol tuple,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        if (tuple.TupleElements.Length > SymbolHelper.MaxFlatTupleArity)
            return null;

        var elements = tuple
            .TupleElements.Select(element => ElementDefault(element.Type, buildOrigin, mapType))
            .ToList();

        return elements.Contains(null)
            ? null
            : new IrTupleLiteral(
                [.. elements.OfType<IrExpression>()],
                mapType(tuple) as IrTupleTypeRef
            );
    }

    /// <summary>The default of one tuple element: <c>null</c> for a reference or nullable type.</summary>
    private static IrExpression? ElementDefault(
        ITypeSymbol type,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin,
        Func<ITypeSymbol, IrTypeRef> mapType
    ) =>
        type.IsReferenceType || type.IsNullableValueType()
            ? new IrLiteral(null, IrLiteralKind.Null)
            : Of(type, buildOrigin, mapType);

    /// <summary><c>0 as E</c> for a numeric enum without a zero member; <c>null</c> for a <c>[StringEnum]</c>.</summary>
    private static IrExpression? EnumZeroAssertion(
        ITypeSymbol enumType,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        var typeRef = mapType(enumType);

        return IrDefaultValues.KindOf(typeRef) is IrDefaultValueKind.EnumZeroAssertion
            ? new IrCastExpression(
                new IrLiteral(0, IrLiteralKind.Int32),
                typeRef,
                IsAssertion: true
            )
            : null;
    }
}
