using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// IR-driven BCL mapping: given an <see cref="IrMemberAccess"/> or
/// <see cref="IrCallExpression"/> (with its <see cref="IrMemberOrigin"/>) plus
/// already-lowered TypeScript receiver/arguments, returns the mapped
/// <see cref="TsExpression"/> when the declarative registry has an entry, or
/// <c>null</c> otherwise. The caller renders the raw form on <c>null</c>.
/// </summary>
public static class IrToTsBclMapper
{
    public static TsExpression? TryMapMemberAccess(
        IrMemberAccess access,
        TsExpression loweredTarget,
        DeclarativeMappingRegistry registry
    )
    {
        if (access.Origin is null)
            return null;
        if (
            !registry.TryGetPropertyByFullName(
                access.Origin.DeclaringTypeFullName,
                access.Origin.MemberName,
                out var entry
            )
        )
            return null;
        var receiver = access.Origin.IsStatic ? null : loweredTarget;
        return ApplyPropertyMapping(entry, receiver);
    }

    /// <summary>
    /// Tries to map a method call. <paramref name="typeArgumentNames"/> feeds
    /// <c>$T0</c>, <c>$T1</c>, … template placeholders; pass an empty list for
    /// non-generic calls.
    /// </summary>
    public static TsExpression? TryMapCall(
        IrCallExpression call,
        TsExpression? loweredReceiver,
        IReadOnlyList<TsExpression> loweredArgs,
        IReadOnlyList<string> typeArgumentNames,
        DeclarativeMappingRegistry registry
    )
    {
        if (call.Origin is null)
            return null;
        if (
            !registry.TryGetMethodsByFullName(
                call.Origin.DeclaringTypeFullName,
                call.Origin.MemberName,
                out var candidates
            )
        )
            return null;

        DeclarativeMappingEntry? match = null;
        foreach (var candidate in candidates)
        {
            if (DeclarativeMappingRendering.MatchesArgFilter(candidate, loweredArgs))
            {
                match = candidate;
                break;
            }
        }
        if (match is null)
            return null;

        var receiver =
            match.HasWrapReceiver && loweredReceiver is not null
                ? DeclarativeMappingRendering.WrapReceiverIfNeeded(
                    loweredReceiver,
                    match.WrapReceiver!,
                    registry
                )
                : loweredReceiver;

        return ApplyMethodMapping(match, receiver, loweredArgs, typeArgumentNames);
    }

    private static TsExpression ApplyPropertyMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver
    )
    {
        if (mapping.HasTemplate)
            return new TsTemplate(
                mapping.JsTemplate!,
                receiver,
                Arguments: [],
                TypeArgumentNames: [],
                RuntimeImports: mapping.RuntimeImportsList
            );

        var name = mapping.JsName!;
        return receiver is not null ? new TsPropertyAccess(receiver, name) : new TsIdentifier(name);
    }

    private static TsExpression ApplyMethodMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver,
        IReadOnlyList<TsExpression> args,
        IReadOnlyList<string> typeArgumentNames
    )
    {
        if (mapping.HasTemplate)
            return new TsTemplate(
                mapping.JsTemplate!,
                receiver,
                args,
                typeArgumentNames,
                mapping.RuntimeImportsList
            );

        var name = mapping.JsName!;
        var callee = receiver is not null
            ? (TsExpression)new TsPropertyAccess(receiver, name)
            : new TsIdentifier(name);
        return new TsCallExpression(callee, args);
    }
}
