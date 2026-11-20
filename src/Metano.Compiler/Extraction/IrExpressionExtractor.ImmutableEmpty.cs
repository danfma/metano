using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>ImmutableList&lt;T&gt;.Empty</c> (and the <c>Empty</c> of
/// <c>ImmutableArray&lt;T&gt;</c>, <c>ImmutableHashSet&lt;T&gt;</c> and
/// <c>ImmutableDictionary&lt;K, V&gt;</c>) is the instance the parameterless
/// <c>Create&lt;T&gt;()</c> of the type's static factory class returns, so the
/// read becomes that call (spec 011 FR-054): a call carries the type
/// arguments, which a static field read does not. A target that keeps native
/// collections keeps the read.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string ImmutableCollectionsNamespace = "System.Collections.Immutable";

    /// <summary>
    /// <c>ImmutableList&lt;T&gt;.Empty</c> → <c>ImmutableList.Create&lt;T&gt;()</c>;
    /// <c>null</c> for any other member access.
    /// </summary>
    private IrCallExpression? TryExtractImmutableEmpty(ISymbol? symbol)
    {
        if (
            _context.KeepsNativeCollections
            || symbol is not IFieldSymbol { Name: "Empty", IsStatic: true } empty
            || CollectionKindResolver.KindOf(empty.ContainingType) is not { } kind
            || !IrCollectionKinds.IsImmutable(kind)
        )
            return null;

        var owner = empty.ContainingType;

        var create = _semantic
            .Compilation.GetTypeByMetadataName($"{ImmutableCollectionsNamespace}.{owner.Name}")
            ?.GetMembers("Create")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.IsEmpty)
            ?.Construct([.. owner.TypeArguments]);

        if (create is null)
            return null;

        return new IrCallExpression(
            new IrMemberAccess(new IrTypeReference(owner.Name), create.Name),
            [],
            [.. owner.TypeArguments.Select(MapElementType)],
            BuildOrigin(create)
        );
    }
}
