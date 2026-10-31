using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Builds the IR for the value C# gives <c>default(T)</c> when the compiler
/// has to synthesize it (no source expression exists), plus the enum-member
/// lookup shared with explicit parameter defaults.
/// <para>
/// A <c>default</c> literal written in source does not go through
/// <see cref="For"/>: <c>IrExpressionExtractor.ExtractDefaultLiteral</c> keeps
/// the opaque <see cref="IrLiteralKind.Default"/> marker for value types, so
/// the backends choose the per-target default there.
/// </para>
/// </summary>
internal static class DefaultValueLiterals
{
    /// <summary>
    /// <c>default(T)</c> as IR: <c>null</c> for reference, nullable and
    /// unconstrained type-parameter types; <c>false</c>; the zero of each
    /// numeric primitive; <c>'\0'</c>; <c>Guid.Empty</c>; and the zero-valued
    /// member of an enum (a bare <c>0</c> when the enum declares none). Any
    /// other struct keeps the opaque <see cref="IrLiteralKind.Default"/>
    /// marker, since no target-agnostic value exists for it.
    /// </summary>
    public static IrExpression For(ITypeSymbol type, Func<ISymbol?, IrMemberOrigin?> buildOrigin)
    {
        if (IsNullDefault(type))
            return new IrLiteral(null, IrLiteralKind.Null);

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            return EnumZero(enumType, buildOrigin);

        if (IsGuid(type))
            return StaticMember(type, nameof(Guid.Empty), buildOrigin);

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => new IrLiteral(false, IrLiteralKind.Boolean),
            SpecialType.System_Char => new IrLiteral('\0', IrLiteralKind.Char),
            SpecialType.System_Decimal => new IrLiteral(0m, IrLiteralKind.Decimal),
            SpecialType.System_Single or SpecialType.System_Double => new IrLiteral(
                0.0,
                IrLiteralKind.Float64
            ),
            SpecialType.System_Int64 or SpecialType.System_UInt64 => new IrLiteral(
                0L,
                IrLiteralKind.Int64
            ),
            SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32 => new IrLiteral(0, IrLiteralKind.Int32),
            _ => new IrLiteral(null, IrLiteralKind.Default),
        };
    }

    /// <summary>
    /// The member of <paramref name="enumType"/> whose constant equals
    /// <paramref name="value"/> (an underlying-type constant, as Roslyn
    /// reports enum defaults), or <c>null</c>. Compares through
    /// <see cref="decimal"/> so every underlying type, <c>ulong</c> included,
    /// compares without overflow.
    /// </summary>
    public static IFieldSymbol? FindEnumMember(INamedTypeSymbol enumType, object value)
    {
        var target = Convert.ToDecimal(value);

        return enumType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f =>
                f.HasConstantValue && Convert.ToDecimal(f.ConstantValue) == target
            );
    }

    private static bool IsNullDefault(ITypeSymbol type) =>
        type.IsReferenceType
        || type is ITypeParameterSymbol { IsValueType: false }
        || type
            is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static bool IsGuid(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: nameof(Guid), ContainingNamespace.Name: "System" }
        && type.ContainingNamespace.ContainingNamespace.IsGlobalNamespace;

    private static IrExpression EnumZero(
        INamedTypeSymbol enumType,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin
    )
    {
        var zeroMember = FindEnumMember(enumType, 0);

        if (zeroMember is null)
            return new IrLiteral(0, IrLiteralKind.Int32);

        return new IrMemberAccess(
            new IrTypeReference(enumType.Name),
            zeroMember.Name,
            buildOrigin(zeroMember)
        );
    }

    private static IrExpression StaticMember(
        ITypeSymbol type,
        string memberName,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin
    ) =>
        new IrMemberAccess(
            new IrTypeReference(type.Name),
            memberName,
            buildOrigin(type.GetMembers(memberName).FirstOrDefault())
        );
}
