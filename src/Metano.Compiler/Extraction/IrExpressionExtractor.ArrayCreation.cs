using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>new T[length]</c> without an initializer (spec 012 FR-004, FR-036): a
/// one-dimensional array whose elements all hold C#'s <c>default(T)</c>.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The <see cref="IrArrayCreation"/> of a sized one-dimensional array
    /// whose element default is a value every slot can share (a literal,
    /// an enum member, <c>Guid.Empty</c>); <c>null</c> for any other shape,
    /// which stays unsupported. Produced under the exact 64-bit lowering only,
    /// so the 2.x output keeps reporting it (STAGING(012k): every mode).
    /// Dart prints <c>List&lt;T&gt;.filled</c>, which has no <c>null</c> slots.
    /// </summary>
    private IrArrayCreation? TryExtractSizedArray(ArrayCreationExpressionSyntax creation)
    {
        if (!_context.ExactInt64Lowering || creation.Initializer is not null)
            return null;

        if (creation.Type.RankSpecifiers is not [{ Sizes: [var size] }])
            return null;

        if (_semantic.GetTypeInfo(creation).Type is not IArrayTypeSymbol { Rank: 1 } array)
            return null;

        var elementDefault = DefaultValueLiterals.For(array.ElementType, BuildOrigin);

        if (!IsShareableElementDefault(elementDefault))
            return null;

        return new IrArrayCreation(
            IrTypeRefMapper.Map(array.ElementType, _originResolver, _target),
            Extract(size),
            elementDefault,
            SixtyFourBitKindOf(size)
        );
    }

    /// <summary>
    /// A default every slot can share: not the opaque struct marker, and not
    /// <c>null</c> for Dart, whose sound null safety rejects a
    /// <c>List&lt;T&gt;</c> filled with <c>null</c> where C# types the slots
    /// as <c>T</c>.
    /// </summary>
    private bool IsShareableElementDefault(IrExpression elementDefault) =>
        elementDefault switch
        {
            IrLiteral { Kind: IrLiteralKind.Default } => false,
            IrLiteral { Kind: IrLiteralKind.Null } => _target
                is not Metano.Annotations.TargetLanguage.Dart,
            _ => true,
        };
}
