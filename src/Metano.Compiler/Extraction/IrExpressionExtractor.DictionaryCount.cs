using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Lowers every <c>Count</c> read that reports the entry count of a BCL
/// dictionary as the dictionary type's own <c>Count</c>, so the backends
/// apply the dictionary mapping instead of a collection one.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Two shapes count a dictionary's entries:
    /// <list type="bullet">
    ///   <item><c>dictionary.Keys.Count</c> / <c>dictionary.Values.Count</c>:
    ///   a view holds one element per entry, so the read folds into
    ///   <c>dictionary.Count</c> and the view is never built.</item>
    ///   <item><c>Count</c> on a <c>Dictionary&lt;K,V&gt;</c>,
    ///   <c>IDictionary&lt;K,V&gt;</c> or <c>IReadOnlyDictionary&lt;K,V&gt;</c>
    ///   receiver: the origin is rebound to the receiver's dictionary type.
    ///   For the interfaces, Roslyn binds the read to the inherited
    ///   <c>ICollection&lt;KeyValuePair&lt;K,V&gt;&gt;.Count</c> /
    ///   <c>IReadOnlyCollection&lt;…&gt;.Count</c>; for the concrete type the
    ///   rebinding changes nothing.</item>
    /// </list>
    /// The leading step of a null-conditional access
    /// (<c>dictionary?.Count</c>) matches too: its receiver is the conditional
    /// receiver, which extracts to the bound value. Returns <c>null</c> for
    /// any other member access.
    /// </summary>
    private IrMemberAccess? TryExtractDictionaryCount(QualifiedMemberSyntax member, ISymbol? symbol)
    {
        if (symbol is not IPropertySymbol { Name: "Count", IsStatic: false } count)
            return null;

        if (FindCountedDictionary(member.Receiver) is not (var dictionary, var dictionaryType))
            return null;

        // A property always has a containing type, so BuildOrigin returns an origin.
        var origin = BuildOrigin(count)! with
        {
            DeclaringTypeFullName = dictionaryType.GetStableFullName(),
        };

        return new IrMemberAccess(Extract(dictionary), count.Name, origin);
    }

    /// <summary>
    /// The dictionary whose entry count a <c>Count</c> read on
    /// <paramref name="receiver"/> reports, with the dictionary type whose
    /// mapping applies; <c>null</c> when the receiver is neither a view of a
    /// BCL dictionary nor a BCL dictionary.
    /// </summary>
    private (ExpressionSyntax Dictionary, INamedTypeSymbol Type)? FindCountedDictionary(
        ExpressionSyntax receiver
    )
    {
        if (DictionaryViewRead.Match(receiver, _semantic) is { } view)
            return (view.Dictionary, view.DictionaryType);

        if (_semantic.GetTypeInfo(receiver).Type is not INamedTypeSymbol type)
            return null;

        return type.IsBclDictionaryDefinition() ? (receiver, type) : null;
    }
}
