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
    /// no expression spells it: a struct that declares a constructor, a tuple,
    /// a <c>struct</c>-constrained type parameter, and a <c>[StringEnum]</c>
    /// that declares no zero member (#621).
    /// </summary>
    public static IrExpression? Of(
        ITypeSymbol type,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        var literal = DefaultValueLiterals.For(type, buildOrigin);

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

    /// <summary><c>0 as E</c> for a numeric enum without a zero member; <c>null</c> for a <c>[StringEnum]</c>.</summary>
    private static IrExpression? EnumZeroAssertion(
        ITypeSymbol enumType,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        var typeRef = mapType(enumType);

        return IrDefaultValues.KindOf(typeRef) is IrDefaultValueKind.EnumZeroAssertion
            ? new IrCastExpression(ZeroOfUnderlying(typeRef), typeRef, IsAssertion: true)
            : null;
    }

    /// <summary>
    /// The zero of an enum's underlying integer: a 64-bit literal for an enum
    /// over <c>long</c> / <c>ulong</c> (#701), an <c>int</c> one otherwise.
    /// </summary>
    private static IrLiteral ZeroOfUnderlying(IrTypeRef enumType) =>
        enumType is IrNamedTypeRef { Semantics.EnumUnderlyingKind: { } kind }
        && kind.IsSixtyFourBit()
            ? new IrLiteral(0L, IrLiteralKind.Int64)
            : new IrLiteral(0, IrLiteralKind.Int32);
}
