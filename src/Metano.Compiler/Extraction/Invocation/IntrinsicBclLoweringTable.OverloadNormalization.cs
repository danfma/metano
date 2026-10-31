using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Entries that rewrite a BCL call to an equivalent, canonical C# overload
/// before the declarative <c>[MapMethod]</c> registry sees it. The registry
/// tells overloads apart only by argument count, so every overload whose
/// count is ambiguous (<c>Math.Round(x, digits)</c> vs.
/// <c>Math.Round(x, mode)</c>) or whose arguments need shaping
/// (<c>params</c> elements, an implicit <c>default(TValue)</c>) is rewritten
/// here into a shape the count alone identifies. The output stays
/// target-agnostic IR: the backends lower the canonical call through their
/// own mappings.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>
    /// <list type="bullet">
    ///   <item><c>Math.Round(double, MidpointRounding)</c> →
    ///   <c>Math.Round(double, 0, MidpointRounding)</c>.</item>
    ///   <item><c>Math.Round(decimal…)</c> → <c>decimal.Round(decimal…)</c>,
    ///   whose <c>(decimal, MidpointRounding)</c> overload becomes
    ///   <c>(decimal, 0, MidpointRounding)</c> as well.</item>
    /// </list>
    /// </summary>
    private void SeedRounding(Compilation compilation)
    {
        var mathType = compilation.GetTypeByMetadataName("System.Math");
        var midpointType = compilation.GetTypeByMetadataName("System.MidpointRounding");

        if (mathType is null || midpointType is null)
            return;

        var decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        var doubleType = compilation.GetSpecialType(SpecialType.System_Double);
        // Any `decimal.Round` overload carries the origin the backends key
        // their mapping on (declaring type + name); the argument count picks
        // the overload there.
        var decimalRound = RoundOverloads(decimalType).First();

        foreach (var method in RoundOverloads(mathType))
        {
            var valueType = method.Parameters[0].Type;
            var takesModeWithoutDigits = TakesModeWithoutDigits(method, midpointType);

            if (SymbolEqualityComparer.Default.Equals(valueType, decimalType))
            {
                _entries[method.OriginalDefinition] = ctx =>
                    BuildRoundCall(ctx, decimalRound, takesModeWithoutDigits);
            }
            else if (
                SymbolEqualityComparer.Default.Equals(valueType, doubleType)
                && takesModeWithoutDigits
            )
            {
                _entries[method.OriginalDefinition] = ctx => BuildRoundCall(ctx, method, true);
            }
        }

        foreach (
            var method in RoundOverloads(decimalType)
                .Where(m => TakesModeWithoutDigits(m, midpointType))
        )
        {
            _entries[method.OriginalDefinition] = ctx => BuildRoundCall(ctx, method, true);
        }
    }

    /// <summary>
    /// <c>CollectionExtensions.GetValueOrDefault(dictionary, key)</c> →
    /// <c>dictionary.GetValueOrDefault(key, default(TValue))</c>, and the
    /// static-call form of the <c>(dictionary, key, defaultValue)</c>
    /// overload → its extension-call form, so both reach the backends as a
    /// receiver plus two arguments.
    /// </summary>
    private void SeedGetValueOrDefault(Compilation compilation)
    {
        var extensions = compilation.GetTypeByMetadataName(
            "System.Collections.Generic.CollectionExtensions"
        );

        if (extensions is null)
            return;

        foreach (
            var method in extensions
                .GetMembers("GetValueOrDefault")
                .OfType<IMethodSymbol>()
                .Where(m => m.IsExtensionMethod && m.Parameters.Length is 2 or 3)
        )
        {
            _entries[method.OriginalDefinition] = BuildGetValueOrDefaultCall;
        }
    }

    /// <summary>
    /// <c>string.Join(separator, values)</c> with the values gathered into a
    /// single array argument: expanded <c>params</c> elements become an
    /// array literal, an argument that already lowers to an array passes
    /// through, and any other enumerable is spread into an array literal.
    /// The <c>(separator, value, startIndex, count)</c> overloads are left
    /// alone.
    /// </summary>
    private void SeedStringJoin(Compilation compilation)
    {
        var stringType = compilation.GetSpecialType(SpecialType.System_String);

        foreach (
            var method in stringType
                .GetMembers("Join")
                .OfType<IMethodSymbol>()
                .Where(m => m.IsStatic && m.Parameters.Length == 2)
        )
        {
            _entries[method.OriginalDefinition] = BuildStringJoinCall;
        }
    }

    private static IEnumerable<IMethodSymbol> RoundOverloads(INamedTypeSymbol type) =>
        type.GetMembers("Round").OfType<IMethodSymbol>().Where(m => m.IsStatic);

    private static bool TakesModeWithoutDigits(IMethodSymbol method, ITypeSymbol midpointType) =>
        method.Parameters.Length == 2
        && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, midpointType);

    /// <summary>
    /// Rebuilds the call against <paramref name="canonical"/>'s declaring
    /// type, inserting a zero digit count ahead of the mode when the source
    /// overload takes a mode but no digits.
    /// </summary>
    private IrExpression BuildRoundCall(
        InvocationRewriteContext ctx,
        IMethodSymbol canonical,
        bool insertZeroDigits
    )
    {
        var arguments = ArgumentValues(ctx, BoundInvocation(ctx));

        if (insertZeroDigits)
        {
            arguments.Insert(1, new IrLiteral(0, IrLiteralKind.Int32));
        }

        var target = new IrMemberAccess(
            new IrTypeReference(canonical.ContainingType.Name),
            canonical.Name
        );

        return new IrCallExpression(
            target,
            arguments.Select(a => new IrArgument(a)).ToList(),
            Origin: _helpers.BuildOrigin(canonical)
        );
    }

    private IrExpression BuildGetValueOrDefaultCall(InvocationRewriteContext ctx)
    {
        var operation = BoundInvocation(ctx);
        var arguments = ArgumentValues(ctx, operation);
        var valueType = operation.TargetMethod.TypeArguments[1];
        var defaultValue = arguments.Count == 3 ? arguments[2] : DefaultValueOf(valueType);

        return new IrCallExpression(
            new IrMemberAccess(arguments[0], ctx.Symbol!.Name),
            [new IrArgument(arguments[1]), new IrArgument(defaultValue)],
            Origin: _helpers.BuildOrigin(ctx.Symbol)
        );
    }

    private IrExpression BuildStringJoinCall(InvocationRewriteContext ctx)
    {
        var operation = BoundInvocation(ctx);
        var arguments = operation.Arguments.OrderBy(a => a.Parameter?.Ordinal).ToList();
        var separator = ctx.Recurse((ExpressionSyntax)arguments[0].Value.Syntax);
        var values = JoinValuesArray(ctx, arguments[1]);

        return new IrCallExpression(
            new IrMemberAccess(new IrTypeReference("String"), ctx.Symbol!.Name),
            [new IrArgument(separator), new IrArgument(values)],
            Origin: _helpers.BuildOrigin(ctx.Symbol)
        );
    }

    private IrExpression JoinValuesArray(InvocationRewriteContext ctx, IArgumentOperation values)
    {
        // Expanded `params` elements: every source argument after the
        // separator, since `params` elements are always positional and last.
        if (values.ArgumentKind != ArgumentKind.Explicit)
        {
            return new IrArrayLiteral(
                ctx.Invocation.ArgumentList.Arguments.Skip(1)
                    .Select(a => ctx.Recurse(a.Expression))
                    .ToList()
            );
        }

        var value = ctx.Recurse((ExpressionSyntax)values.Value.Syntax);
        var valueType = UnconvertedType(values.Value);

        return valueType is not null && LowersToArray(valueType)
            ? value
            : new IrArrayLiteral([new IrSpreadExpression(value)]);
    }

    private bool LowersToArray(ITypeSymbol type) =>
        _helpers.MapTypeRef(type) switch
        {
            IrArrayTypeRef => true,
            IrNullableTypeRef { Inner: IrArrayTypeRef } => true,
            _ => false,
        };

    /// <summary>
    /// <c>default(T)</c> as IR: <c>null</c> for reference, nullable and
    /// type-parameter types, the zero of each numeric primitive,
    /// <c>false</c>, and the zero-valued member of an enum. Any other struct
    /// (<c>char</c>, <c>Guid</c>, user structs, …) keeps the opaque
    /// <see cref="IrLiteralKind.Default"/> marker, as a <c>default</c>
    /// literal does.
    /// </summary>
    private IrExpression DefaultValueOf(ITypeSymbol type)
    {
        if (type.IsReferenceType || type is ITypeParameterSymbol { IsValueType: false })
            return new IrLiteral(null, IrLiteralKind.Null);

        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            }
        )
            return new IrLiteral(null, IrLiteralKind.Null);

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            return EnumZero(enumType);

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => new IrLiteral(false, IrLiteralKind.Boolean),
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

    private IrExpression EnumZero(INamedTypeSymbol enumType)
    {
        var zeroMember = enumType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Convert.ToInt64(f.ConstantValue) == 0);

        if (zeroMember is null)
            return new IrLiteral(0, IrLiteralKind.Int32);

        return new IrMemberAccess(
            new IrTypeReference(enumType.Name),
            zeroMember.Name,
            _helpers.BuildOrigin(zeroMember)
        );
    }

    /// <summary>
    /// The call's argument values in parameter order (the receiver of an
    /// extension call first), read from the bound operation so named
    /// arguments land in their declared slot. Only used for overloads
    /// without <c>params</c> or optional parameters, where every argument
    /// value has its own source expression.
    /// </summary>
    private static List<IrExpression> ArgumentValues(
        InvocationRewriteContext ctx,
        IInvocationOperation operation
    ) =>
        operation
            .Arguments.OrderBy(a => a.Parameter?.Ordinal)
            .Select(a => ctx.Recurse((ExpressionSyntax)a.Value.Syntax))
            .ToList();

    /// <summary>
    /// The bound operation of a call site whose method symbol resolved,
    /// which Roslyn always produces for the BCL methods seeded here.
    /// </summary>
    private static IInvocationOperation BoundInvocation(InvocationRewriteContext ctx) =>
        ctx.SemanticModel.GetOperation(ctx.Invocation) as IInvocationOperation
        ?? throw new InvalidOperationException(
            $"No bound invocation for '{ctx.Invocation}' although its method resolved."
        );

    private static ITypeSymbol? UnconvertedType(IOperation value) =>
        value is IConversionOperation { IsImplicit: true } conversion
            ? conversion.Operand.Type
            : value.Type;
}
