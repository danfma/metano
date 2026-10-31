using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

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
                : DefaultValueLiterals.For(valueType, _helpers.BuildOrigin);

        return new IrCallExpression(
            new IrMemberAccess(arguments[0], ctx.Symbol!.Name),
            [new IrArgument(arguments[1]), new IrArgument(defaultValue)],
            Origin: _helpers.BuildOrigin(ctx.Symbol)
        );
    }
}
