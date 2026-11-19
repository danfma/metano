using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Gives every <c>GetValueOrDefault</c> call the same three-part shape (a
/// receiver, the key and an explicit default value), so the backends need a
/// single mapping for it.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>
    /// <c>CollectionExtensions.GetValueOrDefault(dictionary, key)</c> →
    /// <c>dictionary.GetValueOrDefault(key, default(TValue))</c>, and the
    /// static-call form of the <c>(dictionary, key, defaultValue)</c>
    /// overload → its extension-call form.
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

    private IrExpression BuildGetValueOrDefaultCall(InvocationRewriteContext ctx)
    {
        var operation = BoundInvocation(ctx);
        var arguments = ArgumentValues(ctx, operation);
        var valueType = operation.TargetMethod.TypeArguments[1];

        var defaultValue =
            arguments.Count == 3
                ? arguments[2]
                : DefaultValuePolicy.Of(valueType, _helpers.BuildOrigin, _helpers.MapTypeRef)
                    ?? new IrLiteral(null, IrLiteralKind.Default);

        return new IrCallExpression(
            new IrMemberAccess(arguments[0], ctx.Symbol!.Name),
            [new IrArgument(arguments[1]), new IrArgument(defaultValue)],
            Origin: ReceiverDictionaryOrigin(operation, ctx.Symbol.Name)
                ?? _helpers.BuildOrigin(ctx.Symbol)
        );
    }

    /// <summary>
    /// The member as the receiver's own dictionary type declares it, for a
    /// stage 011c dictionary receiver (spec 011 FR-007): its class has
    /// <c>getValueOrDefault</c>, its interfaces read it through
    /// <c>TryGetValue</c>, so the mapping depends on the receiver's kind, which
    /// the extension's declaring type (<c>CollectionExtensions</c>) does not
    /// say. <c>null</c> for any other receiver, and for a target that keeps
    /// native collections, whose mapping keeps the source shape.
    /// </summary>
    private IrMemberOrigin? ReceiverDictionaryOrigin(IInvocationOperation operation, string name)
    {
        if (
            IrExtractionContext.KeepsNativeCollectionsFor(_helpers.Target)
            || operation.Arguments.Length == 0
        )
            return null;

        var receiver = operation.Arguments[0].Value;

        var receiverType = receiver is IConversionOperation conversion
            ? conversion.Operand.Type
            : receiver.Type;

        return receiverType is INamedTypeSymbol named && IsDictionaryKind(named)
            ? new IrMemberOrigin(named.OriginalDefinition.GetStableFullName(), name)
            : null;
    }

    private static bool IsDictionaryKind(INamedTypeSymbol type) =>
        CollectionKindResolver.KindOf(type)
            is IrCollectionKind.Dictionary
                or IrCollectionKind.IDictionary
                or IrCollectionKind.IReadOnlyDictionary;
}
